import { DurableObject } from "cloudflare:workers";
import {
    type SQLSchemaMigration,
    SQLSchemaMigrations,
} from "durable-utils/sql-migrations";
import OpenAI from "openai";
import type { ChatCompletionMessageParam } from "openai/resources";
import type { Persona } from "@/components/personas";

async function* promptLlm(
    ai: OpenAI,
    messages: ChatCompletionMessageParam[],
    model: string,
) {
    const completion = await ai.chat.completions.create({
        model: model,
        messages: messages,
        stream: true,
    });

    for await (const chunk of completion) {
        yield chunk.choices[0].delta.content;
    }
}

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

type Observer = (chunk: Uint8Array, done: boolean) => void;

class Generation {
    private readonly ai: OpenAI;
    private readonly messageId: number;
    private readonly observers = new Set<Observer>();
    private readonly textEncoder = new TextEncoder();

    private message = "";
    private done = false;

    constructor(ai: OpenAI, messageId: number) {
        this.ai = ai;
        this.messageId = messageId;
    }

    observe(observer: Observer) {
        this.observers.add(observer);
        const chunk = this.textEncoder.encode(this.message);
        observer(chunk, this.done);
    }

    async generate(
        history: MessageType[],
        prompt: string,
        model: string,
        persona: Persona | null,
    ) {
        const systemPrompt =
            persona?.systemPrompt ?? "You are a helpful assistant.";

        const messages: ChatCompletionMessageParam[] = [
            { role: "system", content: systemPrompt, name: persona?.id },
            ...history.map(
                (message) =>
                    ({
                        role:
                            message.senderType === "user"
                                ? "user"
                                : "assistant",
                        content: message.message ?? "",
                        name: message.senderId,
                    }) as ChatCompletionMessageParam,
            ),
            { role: "user", content: prompt },
        ];
        const data = promptLlm(this.ai, messages, model);

        for await (const value of data) {
            if (typeof value !== "string") {
                continue;
            }
            this.message += value;
            const chunk = this.textEncoder.encode(value);
            for (const observer of this.observers) {
                observer(chunk, false);
            }
        }

        this.done = true;
        for (const observer of this.observers) {
            observer(new Uint8Array(0), true);
        }

        return { messageId: this.messageId, message: this.message };
    }
}

export class MyDurableObject extends DurableObject<CloudflareBindings> {
    private readonly generations = new Map<number, Generation>();

    private readonly ai: OpenAI;
    private readonly sql: SqlStorage;
    private readonly kv: KVNamespace;

    constructor(ctx: DurableObjectState, env: CloudflareBindings) {
        // Required, as we're extending the base class.
        super(ctx, env);
        this.ai = new OpenAI({
            baseURL: "https://openrouter.ai/api/v1",
            apiKey: env.GEMINI_API_KEY,
            defaultHeaders: {
                "HTTP-Referer": "https://proomting.party", // Optional. Site URL for rankings on openrouter.ai.
                "X-Title": "Proompting Party", // Optional. Site title for rankings on openrouter.ai.
            },
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
        senderType: string,
        senderId: string,
        personaId: string,
        model: string,
    ) {
        console.log("[Party.ts->sendPrompt] prompt:", prompt);

        // Add the user's prompt to the database as a message from the user
        // and generate a message-stub for the model.
        const newMessageIds = this.sql
            .exec<{ messageid: number }>(
                `INSERT INTO messages(message, senderType, senderId)
                VALUES (?, ?, ?), (?, ?, ?)
                RETURNING messageid`,
                prompt,
                senderType,
                senderId,
                null,
                "assistant",
                personaId,
            )
            .toArray()
            .map((row) => row.messageid);

        console.log(
            "[Party.ts->sendPrompt] new message IDs:",
            newMessageIds,
            "starting generation now.",
        );

        const generation = new Generation(this.ai, newMessageIds[1]);
        this.generations.set(newMessageIds[1], generation);

        const personaData = await this.kv.get<Persona>(`persona:${personaId}`, {
            type: "json",
        });

        // Fire and forget the generation
        const messages = this.sql
            .exec<MessageType>("SELECT * FROM messages")
            .toArray();

        generation.generate(messages, prompt, model, personaData).then((g) => {
            console.log(
                "[Party.ts->sendPrompt] generation finished",
                g.messageId,
            );
            this.sql.exec(
                `UPDATE messages SET message = ?, sendAt = ? WHERE messageid = ?`,
                g.message,
                new Date().toISOString(),
                g.messageId,
            );
            // Don't delete immediately from the cache after generation
            // finished since there can be a `sub` request incoming.
            // There shouldn't be more since we've updated the message
            // with a date
            setTimeout(() => this.generations.delete(g.messageId), 1000);
        });

        for (const socket of this.ctx.getWebSockets()) {
            console.log("user message", newMessageIds);
            socket.send(
                JSON.stringify({
                    type: "message",
                    message: {
                        messageid: newMessageIds[0],
                        message: prompt,
                        senderType: senderType,
                        senderId: senderId,
                        sendAt: new Date().getMilliseconds(),
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

    async fetch(request: Request): Promise<Response> {
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

    async streamMessage(
        request: Request,
        messageId: number,
    ): Promise<Response> {
        // Else if the message is still being generated we stream it to the client

        const generation = this.generations.get(messageId);
        if (!generation) {
            return new Response("Message not found", { status: 404 });
        }
        const stream = new ReadableStream({
            async start(controller) {
                if (request.signal.aborted) {
                    controller.close();
                    return;
                }
                generation.observe((chunk, done) => {
                    if (done) {
                        controller.close();
                    } else {
                        controller.enqueue(chunk);
                    }
                });
            },
            cancel() {
                console.error(`Subscription to message ${messageId} cancelled`);
            },
        });

        const headers = new Headers({
            "Content-Type": "application/octet-stream",
        });

        return new Response(stream, { headers });
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
