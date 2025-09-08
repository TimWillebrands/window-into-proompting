import { Hono } from "hono";
import { html } from "hono/html";
import type { PropsWithChildren } from "hono/jsx";
import { streamSSE } from "hono/streaming";
import { Message, PartiesZone, Party } from "./components/party";
import type { MyDurableObject } from "./party";

type Bindings = {
    MY_DURABLE_OBJECT: DurableObjectNamespace<MyDurableObject>;
    GEMINI_API_KEY: string;
};

const app = new Hono<{ Bindings: Bindings }>();

interface SiteData {
    title: string;
}

const Layout = (props: PropsWithChildren<SiteData>) =>
    html`<!doctype html>
        <html>
            <head>
                <title>${props.title}</title>
                <script src="https://cdn.jsdelivr.net/npm/htmx.org@2.0.6/dist/htmx.min.js"></script>
                <script src="https://cdn.jsdelivr.net/npm/htmx-ext-sse@2.2.2"></script>
                <script type="module" src="https://cdn.jsdelivr.net/npm/zero-md@3?register"></script>
                <script defer src="https://cdn.jsdelivr.net/npm/alpinejs@3.x.x/dist/cdn.min.js"></script>
                <link rel="stylesheet" href="https://unpkg.com/xp.css" >
                <link rel="stylesheet" href="output.css" >
            </head>
            <body hx-ext="sse" >
                ${props.children}
            </body>
        </html>`;

app.get("/", (c) => {
    return c.html(
        <Layout title="My Party">
            <PartiesZone>
                <Party room="First" />
            </PartiesZone>
        </Layout>,
    );
});

app.post("/:room/message", async (c) => {
    const room = c.req.param("room");
    const stub = c.env.MY_DURABLE_OBJECT.getByName(room);

    const body = await c.req.parseBody();
    const prompt = body.prompt;

    if (typeof prompt !== "string") {
        return new Response("Invalid prompt", { status: 400 });
    }

    stub.prepare(prompt);

    return c.html(<Message room={room} />);
});

app.get("/:room/prompt", async (c) => {
    const room = c.req.param("room");
    const stub = c.env.MY_DURABLE_OBJECT.getByName(room);
    const response = await stub.runPrompt(c.req.raw);

    if (!response.ok || !response.body) {
        return new Response("Invalid response", { status: 500 });
    }

    const reader = response.body
        .pipeThrough(new TextDecoderStream())
        .getReader();

    return streamSSE(c, async (stream) => {
        while (true) {
            const { value, done } = await reader.read();

            if (value) {
                await stream.writeSSE({
                    data: value, //`<span>${value}</span>`,
                    event: "message",
                });
            }
            if (done) {
                await stream.writeSSE({
                    data: "it is finished",
                    event: "finished",
                });
                await stream.close();
                break;
            }
        }
    });
});

export default app;
export { MyDurableObject } from "@/party";
