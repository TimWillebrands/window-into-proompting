import { Hono } from "hono";
import { html } from "hono/html";
import type { PropsWithChildren } from "hono/jsx";
import { streamSSE } from "hono/streaming";
import { Desktop } from "./components/desktop";
import { Message, UserMessage } from "./components/message";
import { OpenParty } from "./components/openParty";
import { Party } from "./components/party";
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
            <Desktop></Desktop>
        </Layout>,
    );
});

app.get("/party", (c) => {
    return c.html(<OpenParty previousParties={[]} />);
});

app.post("/party/create", async (c) => {
    const body = await c.req.formData();
    const partyName = body.get("partyName") ?? crypto.randomUUID();

    return c.redirect(`/party/${partyName}`);
});

app.get("/party/:id", (c) => {
    const id = c.req.param("id");
    return c.html(<Party room={id} />);
});

app.post("/party/:id/message", async (c) => {
    const id = c.req.param("id");
    const stub = c.env.MY_DURABLE_OBJECT.getByName(id);

    const body = await c.req.formData();
    const prompt = body.get("prompt");

    if (typeof prompt !== "string") {
        return new Response("Invalid prompt", { status: 400 });
    }

    await stub.prepare(prompt);

    return c.html(
        <>
            <UserMessage content={prompt} />
            <Message room={id} />
        </>,
    );
});

app.get("/party/:id/prompt", async (c) => {
    const id = c.req.param("id");
    const stub = c.env.MY_DURABLE_OBJECT.getByName(id);
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
