import { Hono } from "hono";
import { jsxRenderer } from "hono/jsx-renderer";
import { streamText } from "hono/streaming";
import type { MyDurableObject } from "./party";

type Bindings = {
    MY_DURABLE_OBJECT: DurableObjectNamespace<MyDurableObject>;
};

const app = new Hono<{ Bindings: Bindings }>();

app.get(
    "/page/*",
    jsxRenderer(({ children }) => {
        return (
            <html>
                <body>
                    <header>Menu</header>
                    <div>{children}</div>
                </body>
            </html>
        );
    }),
);

app.get("/message", async (c) => {
    const stub = c.env.MY_DURABLE_OBJECT.getByName(new URL(c.req.url).pathname);
    const greeting = await stub.sayHello();

    return c.text(greeting ?? "nope");
});

app.get("/streamText", async (c) => {
    const stub = c.env.MY_DURABLE_OBJECT.getByName(new URL(c.req.url).pathname);
    const response = await stub.fetch(c.req.raw);
    if (!response.ok || !response.body) {
        return new Response("Invalid response", { status: 500 });
    }

    const reader = response.body
        .pipeThrough(new TextDecoderStream())
        .getReader();

    return streamText(c, async (stream) => {
        while (true) {
            const { value, done } = await reader.read();

            console.log(value, done);

            if (value) {
                await stream.write(value);
            }
            if (done) {
                await stream.close();
            }
        }
    });
});

export default app;
export { MyDurableObject } from "@/party";
