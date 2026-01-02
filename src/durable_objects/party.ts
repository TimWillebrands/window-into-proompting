import { DurableObject } from "cloudflare:workers";
import { OpenAI } from "@posthog/ai";
import {
    type SQLSchemaMigration,
    SQLSchemaMigrations,
} from "durable-utils/sql-migrations";
import { PostHog } from "posthog-node";
import type { Persona, PersonaMetadata } from "@/components/personas";
import { Generation, type MessageWithSender } from "../services/generation";

export type MessageType = {
    messageid: number;
    message?: string;
    senderType: string;
    senderId: string;
    sendAt?: number;
    modelEndpointStub?: string;
};

export type SubscriptionMessage =
    | { type: "join"; messages: MessageType[] }
    | { type: "message"; message: MessageType }
    | { type: "messageStream"; messageId: number };

export type PartyInfoFull = PartyInfo & {
    participants: PersonaMetadata[];
};

export type PartyInfo = {
    id: string;
    name: string;
};

const phClient = new PostHog(
    "phc_f44OvBqb7P19kNmbDBXlNy4UH8pdoiJcUVKZJ1aN950",
    { host: "https://eu.i.posthog.com" },
);

export class MyDurableObject extends DurableObject<CloudflareBindings> {
    private readonly generations = new Map<number, Generation>();

    private readonly ai: OpenAI;
    private readonly sql: SqlStorage;
    private readonly kv: KVNamespace;

    private async tryGetPersona(id: string | null | undefined) {
        if (!id) return null;

        const personaData = await this.kv.get(`persona:${id}`);
        if (!personaData) {
            return null;
        }
        return JSON.parse(personaData) as Persona;
    }

    private async getParticipants(partyId: string) {
        const partyInfoStr = await this.kv.get(`party:${partyId}`);

        if (!partyInfoStr) throw new Error(`Party not found: ${partyId}.`);

        const partyInfo = JSON.parse(partyInfoStr) as PartyInfoFull;

        if (
            partyInfo.participants === undefined ||
            partyInfo.participants.length === 0
        ) {
            console.log(`Party ${partyId} has no participants.`, partyInfo);
            throw new Error(`Party has no participants.`);
        }

        const participants = await Promise.all(
            partyInfo.participants.map(
                async (p) =>
                    (await this.tryGetPersona(p.id)) ??
                    ({
                        ...p,
                        isUser: true,
                        systemPrompt:
                            "this is a real human user and has no systemprompt, use their answers in chat to evaluate their character",
                    } satisfies Persona),
            ),
        );

        return participants;
    }

    constructor(ctx: DurableObjectState, env: CloudflareBindings) {
        // Required, as we're extending the base class.
        super(ctx, env);
        this.ai = new OpenAI({
            baseURL: "https://openrouter.ai/api/v1",
            apiKey: env.GEMINI_API_KEY,
            defaultHeaders: {
                "HTTP-Referer": "https://proompting.party", // Optional. Site URL for rankings on openrouter.ai.
                "X-Title": "Proompting Party", // Optional. Site title for rankings on openrouter.ai.
            },
            posthog: phClient,
        });
        this.sql = ctx.storage.sql;
        this.kv = env.DESKTOP_DATA;

        const migrations = new SQLSchemaMigrations({
            doStorage: ctx.storage,
            migrations: Migrations,
        });

        ctx.blockConcurrencyWhile(async () => {
            await migrations.runAll();
        });
    }

    async sendPrompt(
        prompt: string,
        senderId: string,
        model: string,
        roomId: string,
        personaId: string | null,
    ) {
        // Add the user's prompt to the database as a message from the user
        // and generate a message-stub for the model's answer.
        const newMessageIds = this.sql
            .exec<{ messageid: number }>(
                `INSERT INTO messages(message, senderType, senderId)
                VALUES (?, ?, ?), (?, ?, ?)
                RETURNING messageid`,
                prompt,
                "user",
                senderId,
                null,
                "assistant",
                personaId ?? "__IN_PROGRESS",
            )
            .toArray()
            .map((row) => row.messageid);

        console.log(
            "[Party.ts->sendPrompt] new message IDs:",
            newMessageIds,
            "starting generation now.",
        );

        const newMessageId = newMessageIds[1];

        const messages = await this.getMessagesUntil(newMessageId);

        const personaData = await this.tryGetPersona(personaId);

        await this.initiateGeneration(
            newMessageId,
            model,
            roomId,
            personaData,
            senderId,
            messages,
        );

        for (const socket of this.ctx.getWebSockets()) {
            console.log("user message", newMessageIds);
            socket.send(
                JSON.stringify({
                    type: "message",
                    message: {
                        messageid: newMessageIds[0],
                        message: prompt,
                        senderType: "user",
                        senderId: senderId,
                        sendAt: new Date().getUTCMilliseconds(),
                    },
                } as SubscriptionMessage),
            );

            socket.send(
                JSON.stringify({
                    type: "messageStream",
                    messageId: newMessageIds[1],
                } as SubscriptionMessage),
            );
        }

        return new Response();
    }

    async proceed(
        senderId: string,
        personaId: string | null | undefined,
        model: string,
        roomId: string,
    ) {
        // generate a message-stub for the model.
        const newMessageId = this.sql
            .exec<{ messageid: number }>(
                `INSERT INTO messages(message, senderType, senderId)
                VALUES (?, ?, ?)
                RETURNING messageid`,
                null,
                "assistant",
                personaId,
            )
            .one().messageid;

        const messages = await this.getMessagesUntil(newMessageId);

        const personaData = await this.tryGetPersona(personaId);

        await this.initiateGeneration(
            newMessageId,
            model,
            roomId,
            personaData,
            senderId,
            messages,
        );

        for (const socket of this.ctx.getWebSockets()) {
            socket.send(
                JSON.stringify({
                    type: "messageStream",
                    messageId: newMessageId,
                } as SubscriptionMessage),
            );
        }

        return new Response();
    }

    async getMessagesUntil(newMessageId: number): Promise<MessageWithSender[]> {
        // Get message history up to this point
        const messagesQuery = this.sql
            .exec<MessageType>(
                // '<' not '<=' because lets not include the 'null' message we reserved
                // for the response
                "SELECT * FROM messages WHERE messageid < ?",
                newMessageId,
            )
            .toArray()
            .map(async (msg) => ({
                ...msg,
                senderName:
                    (await this.tryGetPersona(msg.senderId))?.name ||
                    msg.senderId,
            }));

        const messages = await Promise.all(messagesQuery);
        return messages;
    }

    /**
     * Initiate a generation, this only blocks on fetching resources (dependencies etc) not
     * on the actual generation itself.
     * @param newMessageId The Id of the message up unto which we should base the generation
     * @param model The model to use for the generation
     * @param roomId The chat-room id
     * @param personaData The optional persona to force a generation for
     * @param senderId The id of the sender (user || overseer)
     */
    async initiateGeneration(
        newMessageId: number,
        model: string,
        roomId: string,
        personaData: Persona | null,
        senderId: string,
        messages: MessageWithSender[],
    ) {
        const generation = new Generation(
            this.ai,
            messages,
            model,
            roomId,
            await this.getParticipants(roomId),
        );
        this.generations.set(newMessageId, generation);

        // Fire and forget the generation
        generation.generate(personaData, senderId).then(
            async ({ message, persona, stop }) => {
                console.log(
                    `[Party.ts->initiateGeneration] generation finished. stop: ${stop}, messageId: ${newMessageId}, weHazPerzona: ${persona === undefined}`,
                );

                if (stop) {
                    setTimeout(() => {
                        console.log(
                            `Generation stopped for message ${newMessageId}`,
                        );
                        this.deleteMessage(newMessageId);
                    }, 1000);
                    return;
                }

                if (persona === undefined) {
                    throw new Error(
                        "Persona not found. This should never happen at this point since the " +
                            "assertion happened in generation.ts",
                    );
                }

                try {
                    const cursor = this.sql.exec(
                        `UPDATE messages SET message = ?, sendAt = ?, senderId = ? WHERE messageid = ?`,
                        message,
                        new Date().toISOString(),
                        persona.id,
                        newMessageId,
                    );

                    console.log(
                        "[Party.ts->initiateGeneration] generation saved",
                        newMessageId,
                        cursor,
                    );

                    this.proceed(persona.id, null, model, roomId);
                } catch (error) {
                    console.error(
                        "Error saving generation",
                        error,
                        personaData,
                        newMessageId,
                        senderId,
                    );
                }

                // Don't delete immediately from the cache after generation
                // finished since there can be a `sub` request incoming.
                // There shouldn't be more since we've updated the message
                // with a date
                setTimeout(() => this.generations.delete(newMessageId), 1000);
            },
            (error) => {
                console.error(
                    "Error generating message",
                    error,
                    personaData,
                    newMessageId,
                    senderId,
                );
            },
        );
    }

    async fetch(_: Request): Promise<Response> {
        // Creates two ends of a WebSocket connection.
        const webSocketPair = new WebSocketPair();
        const [client, server] = Object.values(webSocketPair);

        // Calling `acceptWebSocket()` informs the runtime that this WebSocket is to begin terminating
        // request within the Durable Object. It has the effect of "accepting" the connection,
        // and allowing the WebSocket to send and receive messages.
        // Unlike `ws.accept()`, `this.ctx.acceptWebSocket(ws)` informs the Workers Runtime that the WebSocket
        // is "hibernatable", so the runtime does not need to pin this Durable Object to memory while
        // the connection is open. During periods of inactivity, the Durable Object can be evicted
        // from memory, but the WebSocket connection will remain open. If at some later point the
        // WebSocket receives a message, the runtime will recreate the Durable Object
        // (run the `constructor`) and deliver the message to the appropriate handler.
        this.ctx.acceptWebSocket(server);

        // Generate a random UUID for the session.
        const id = crypto.randomUUID();

        // Attach the session ID to the WebSocket connection and serialize it.
        // This is necessary to restore the state of the connection when the Durable Object wakes up.
        server.serializeAttachment({ id });

        // Send chat history to the client
        const messages = this.sql
            .exec<MessageType>("SELECT * FROM Messages")
            .toArray();
        server.send(
            JSON.stringify({ type: "join", messages } as SubscriptionMessage),
        );

        return new Response(null, {
            status: 101,
            webSocket: client,
        });
    }

    async getMessage(messageId: number): Promise<MessageType | null> {
        // If the message is complete in SQL we just send it
        const message = this.sql
            .exec<MessageType>(`SELECT * FROM messages WHERE id = ?`, [
                messageId,
            ])
            .one();

        if (message === undefined) {
            return null;
        }

        return message;
    }

    async streamMessage(messageId: number): Promise<Response> {
        const generation = this.generations.get(messageId);
        if (!generation) {
            return new Response("Message not found", { status: 404 });
        }

        const { readable, writable } = new TransformStream();

        const headers = new Headers({
            "Content-Type": "text/event-stream",
            "Cache-Control": "no-cache", // Important for SSE to prevent buffering
            Connection: "keep-alive", // Keep the connection open
        });

        const writer = writable.getWriter();

        try {
            generation.observe(async (chunk, done) => {
                if (done) {
                    writable.close();
                } else {
                    await writer.write(chunk);
                }
            });
        } catch (err) {
            console.error(err);
            writable.abort();
        } finally {
            writable.close();
        }

        return new Response(readable, { headers });
    }

    // TODO: temp implementation, we just dump all persona's in the party
    // we should implement a proper way to add and remove participants
    async setParticipants(_: PersonaMetadata[]) {
        return [];
    }

    async deleteMessage(messageId: number) {
        this.sql.exec(`DELETE FROM messages WHERE messageid = ?`, [messageId]);

        return new Response();
    }

    async downloadMessages(): Promise<
        (MessageType & { senderName: string })[]
    > {
        const messages = this.sql
            .exec<MessageType>("SELECT * FROM messages LIMIT 100")
            .toArray()
            .map(async (msg) => ({
                ...msg,
                senderName:
                    (await this.tryGetPersona(msg.senderId))?.name ?? "Unknown",
            }));

        return await Promise.all(messages);
    }
}
const Migrations: SQLSchemaMigration[] = [
    {
        idMonotonicInc: 1,
        description: "initial version",
        sql: `
            CREATE TABLE IF NOT EXISTS messages(
                messageid    INTEGER PRIMARY KEY AUTOINCREMENT,
                message      TEXT,
                sender       VARCHAR(255) NOT NULL,
                sendAt       DATETIME
            );
        `,
    },
    {
        idMonotonicInc: 2,
        description: "add model column to messages",
        sql: `
            ALTER TABLE messages
            ADD COLUMN modelEndpointStub VARCHAR(255) NOT NULL DEFAULT '-';
        `,
    },
    {
        idMonotonicInc: 3,
        description:
            "add senderId column to messages and rename sender to senderType",
        sql: `
            ALTER TABLE messages
            RENAME COLUMN sender TO senderType;

            ALTER TABLE messages
            ADD COLUMN senderId VARCHAR(127) NOT NULL DEFAULT '0000-0000';
        `,
    },
];
