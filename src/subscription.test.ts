import { expect, test } from "bun:test";
import { Subscription } from "./subscription";

type TestMessage = {
    type: string;
    data: string;
};

test("subscription fan-out behavior", async () => {
    // Create a mock WebSocket server
    const server = serveWebSuckits((ws) => {
        ws.send(JSON.stringify({ type: "test", data: "hello" }));
        ws.send(JSON.stringify({ type: "test", data: "world" }));
    });

    try {
        // Connect client WebSocket to the server
        const client = new WebSocket(`ws://localhost:${server.port}`);

        // Wait for connection to open
        await new Promise((resolve) => {
            client.addEventListener("open", resolve);
        });

        const subscription = new Subscription<TestMessage>(client);

        // Create two independent message streams
        const stream1 = subscription.messages();
        const stream2 = subscription.messages();

        // Both streams should receive the same messages
        const messages1Promise = async (stream: AsyncIterable<TestMessage>) => {
            const messages = [];
            for await (const message of stream) {
                messages.push(message);
            }
            return messages;
        };

        setTimeout(() => {
            // Clean up
            client.close();
        }, 50);

        const [result1, result2] = await Promise.all([
            messages1Promise(stream1),
            messages1Promise(stream2),
        ]);

        const expectedResult = [
            { type: "test", data: "hello" },
            { type: "test", data: "world" },
        ];

        expect(result1).toEqual(expectedResult);
        expect(result2).toEqual(expectedResult);
    } finally {
        server.stop();
    }
});

function serveWebSuckits(doStuff: (ws: Bun.ServerWebSocket<unknown>) => void) {
    return Bun.serve({
        port: 0, // Use random available port
        fetch(req, server) {
            const success = server.upgrade(req);
            if (success) {
                return undefined;
            }
            return new Response("Not found", { status: 404 });
        },
        websocket: {
            message(ws, message) {
                // Echo messages back for testing
                ws.send(message as string);
            },
            open(ws) {
                // Send test message when client connects
                doStuff(ws);
            },
        },
    });
}
