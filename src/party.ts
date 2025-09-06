import { DurableObject } from "cloudflare:workers";
import { GoogleGenAI } from "@google/genai";
import { Env } from "hono";

async function* dataSource(ai: GoogleGenAI, signal: AbortSignal) {
    const response = await ai.models.generateContentStream({
        model: "gemini-2.0-flash",
        contents: "Explain how AI works in a few words",
        config: {
            abortSignal: signal,
        },
    });

    for await (const chunk of response) {
        yield chunk.text;
    }
}

export class MyDurableObject extends DurableObject<CloudflareBindings> {
    private sessions: Map<string, WebSocket>;
    private ai: GoogleGenAI;

    // biome-ignore lint: because
    constructor(ctx: DurableObjectState, env: CloudflareBindings) {
        // Required, as we're extending the base class.
        super(ctx, env);
        this.sessions = new Map();
        this.ai = new GoogleGenAI({ apiKey: env.GEMINI_API_KEY });
    }

    async sayHello() {
        const result = this.ctx.storage.sql
            .exec("SELECT 'Hello, World!' as greeting")
            .one();
        return result.greeting?.toString();
    }

    async fetch(request: Request): Promise<Response> {
        const abortController = new AbortController();
        const ai = this.ai;

        const stream = new ReadableStream({
            async start(controller) {
                if (request.signal.aborted) {
                    controller.close();
                    abortController.abort();
                    return;
                }

                const data = dataSource(ai, abortController.signal);
                for await (const value of data) {
                    controller.enqueue(new TextEncoder().encode(String(value)));
                }

                controller.close();
                abortController.abort();
            },
            cancel() {
                console.log("Stream cancelled");
                abortController.abort();
            },
        });

        const headers = new Headers({
            "Content-Type": "application/octet-stream",
        });

        return new Response(stream, { headers });
    }
}
