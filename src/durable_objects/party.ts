import { DurableObject } from "cloudflare:workers";
import { GoogleGenAI } from "@google/genai";

async function* dataSource(
    ai: GoogleGenAI,
    signal: AbortSignal,
    prompt: string,
) {
    const response = await ai.models.generateContentStream({
        model: "gemini-2.0-flash",
        contents: prompt,
        config: {
            abortSignal: signal,
        },
    });

    for await (const chunk of response) {
        yield chunk.text;
    }
}

export class MyDurableObject extends DurableObject<CloudflareBindings> {
    private readonly sessions: Map<string, WebSocket>;
    private readonly ai: GoogleGenAI;
    private prompt?: string;

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

    async prepare(prompt: string) {
        console.log("Party.ts - prepare", prompt);
        this.prompt = prompt;
        return new Response();
    }

    async runPrompt(request: Request): Promise<Response> {
        console.log("Party.ts - runPrompt", this.prompt);
        const abortController = new AbortController();
        const ai = this.ai;

        if (!this.prompt) {
            throw new Error("Prompt not set");
        }

        const prompt = this.prompt;

        const stream = new ReadableStream({
            async start(controller) {
                if (request.signal.aborted) {
                    controller.close();
                    abortController.abort();
                    return;
                }

                const data = dataSource(ai, abortController.signal, prompt);
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
