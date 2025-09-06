import { DurableObject } from "cloudflare:workers";
import type { Env } from "hono";

async function* dataSource(signal: AbortSignal) {
    let counter = 0;
    while (!signal.aborted) {
        yield counter++;
        await new Promise((resolve) => setTimeout(resolve, 1_000));
    }

    console.log("Data source cancelled");
}

export class MyDurableObject extends DurableObject<Env> {
    private sessions: Map<string, WebSocket>;

    // biome-ignore lint: because
    constructor(ctx: DurableObjectState, env: Env) {
        // Required, as we're extending the base class.
        super(ctx, env);
        this.sessions = new Map();
    }

    async sayHello() {
        const result = this.ctx.storage.sql
            .exec("SELECT 'Hello, World!' as greeting")
            .one();
        return result.greeting?.toString();
    }

    async fetch(request: Request): Promise<Response> {
        const abortController = new AbortController();

        const stream = new ReadableStream({
            async start(controller) {
                if (request.signal.aborted) {
                    controller.close();
                    abortController.abort();
                    return;
                }

                for await (const value of dataSource(abortController.signal)) {
                    controller.enqueue(new TextEncoder().encode(String(value)));
                }
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
