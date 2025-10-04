import { DurableObject } from "cloudflare:workers";
import OpenAI from "openai";
import type { ChatCompletionMessageParam } from "openai/resources";

export const models = [
    "z-ai/glm-4.5-air:free",
    "deepseek/deepseek-chat-v3.1:free",
    "nvidia/nemotron-nano-9b-v2:free",
    "openai/gpt-oss-20b:free",
    "qwen/qwen3-coder:free",
    "moonshotai/kimi-k2:free",
] as const;

type Model = (typeof models)[number];

async function* promptLlm(
    ai: OpenAI,
    messages: ChatCompletionMessageParam[],
    model: Model,
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
    sender: string;
    sendAt?: number;
    model?: string;
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
        model: Model = models[0],
    ) {
        const messages: ChatCompletionMessageParam[] = [
            { role: "system", content: "You are a helpful assistant." },
            ...history.map(
                (message) =>
                    ({
                        role: message.sender === "user" ? "user" : "assistant",
                        content: message.message ?? "",
                        name: undefined,
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

        ctx.blockConcurrencyWhile(async () => {
            for (const migrate of SqlMigrations) {
                migrate(this.sql);
            }
        });
    }

    async sendPrompt(prompt: string, sender: string, model: Model) {
        console.log("Party.ts - sendPrompt", prompt);

        // Add the user's prompt to the database as a message from the user
        // and generate a message-stub for the model.
        const newMessageIds = this.sql
            .exec<{ messageid: number }>(
                `INSERT INTO messages(message, sender)
                VALUES (?, ?), (?, ?)
                RETURNING messageid`,
                prompt,
                sender,
                null,
                "model",
            )
            .toArray()
            .map((row) => row.messageid);

        const generation = new Generation(this.ai, newMessageIds[1]);
        this.generations.set(newMessageIds[1], generation);

        // Fire and forget the generation
        const messages = this.sql
            .exec<MessageType>("SELECT * FROM messages")
            .toArray();

        generation.generate(messages, prompt, model).then((g) => {
            console.log("Generation finished", g.messageId);
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
                        sender: sender,
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

const SqlMigrations = [
    (sql: SqlStorage) => {
        sql.exec(`
        CREATE TABLE IF NOT EXISTS messages(
            messageid    INTEGER PRIMARY KEY AUTOINCREMENT,
            message      TEXT,
            sender       VARCHAR(255) NOT NULL,
            sendAt       DATETIME
        );`);

        console.log("Table created if it didn't exist yet");
    },
    (sql: SqlStorage) => {
        // Check if the model column already exists
        const columnExists = sql
            .exec(`PRAGMA table_info(messages)`)
            .toArray()
            .some((row: any) => row.name === "model");

        if (!columnExists) {
            sql.exec(`ALTER TABLE messages ADD COLUMN model TEXT;`);
            console.log("Column 'model' added to table 'messages'");
        } else {
            console.log("Column 'model' already exists in table 'messages'");
        }
    },
];
