import { Hono } from "hono";
import { html } from "hono/html";
import { jsxRenderer } from "hono/jsx-renderer";
import { streamSSE, streamText } from "hono/streaming";
import { Party } from "./components/party";
import type { MyDurableObject } from "./party";

type Bindings = {
    MY_DURABLE_OBJECT: DurableObjectNamespace<MyDurableObject>;
    GEMINI_API_KEY: string;
};

const app = new Hono<{ Bindings: Bindings }>();

interface SiteData {
    title: string;
    children?: any;
}

const Layout = (props: SiteData) =>
    html`<!doctype html>
        <html>
            <head>
                <title>${props.title}</title>
                <script src="https://cdn.jsdelivr.net/npm/htmx.org@2.0.6/dist/htmx.min.js"></script>
                <script src="https://cdn.jsdelivr.net/npm/htmx-ext-sse@2.2.2"></script>
                <link rel="stylesheet" href="https://unpkg.com/xp.css" >
            </head>
            <body hx-ext="sse">
                ${props.children}
            </body>
        </html>`;

app.get("/", (c) => {
    return c.html(
        <Layout title="My Party">
            <Party />
        </Layout>,
    );
});

app.post("/message", async (c) => {
    const body = await c.req.parseBody();
    const prompt = body.prompt;
    return c.html(
        <div
            hx-ext="sse"
            sse-connect="/sse"
            sse-swap="message"
            hx-swap="beforeend"
            sse-close="finished"
        ></div>,
    );
});

app.get("/sse", async (c) => {
    const stub = c.env.MY_DURABLE_OBJECT.getByName(new URL(c.req.url).pathname);
    const response = await stub.fetch(c.req.raw);

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
                    data: `<p>${value}</p>`,
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

app.get("/streamText", async (c) => {
    c.header("Content-Encoding", "Identity");
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
                break;
            }
        }
    });
});

export default app;
export { MyDurableObject } from "@/party";
