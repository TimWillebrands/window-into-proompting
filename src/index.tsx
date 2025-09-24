import { Hono } from "hono";
import { html } from "hono/html";
import type { PropsWithChildren } from "hono/jsx";
import { streamSSE } from "hono/streaming";
import { Desktop } from "./components/desktop";
import { Message } from "./components/message";
import { OpenParty } from "./components/openParty";
import { Party } from "./components/party";
import {
    MessageType,
    type MyDurableObject,
    type SubscriptionMessage,
} from "./durable_objects/party";
import { Subscription } from "./subscription";

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
                <script src="script.js"></script>
                <script src="https://cdn.jsdelivr.net/npm/htmx.org@2.0.6/dist/htmx.min.js"></script>
                <script src="https://cdn.jsdelivr.net/npm/htmx-ext-sse@2.2.2"></script>
                <script type="module" src="https://cdn.jsdelivr.net/npm/zero-md@3?register"></script>

                <script defer src="https://cdn.jsdelivr.net/npm/@alpinejs/resize@3.x.x/dist/cdn.min.js"></script>
                <script defer src="https://cdn.jsdelivr.net/npm/@alpinejs/persist@3.x.x/dist/cdn.min.js"></script>
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

app.get("/party/:id", async (c) => {
    const id = c.req.param("id");

    return c.html(<Party room={id} />);
});

app.post("/party/:id/prompt", async (c) => {
    const id = c.req.param("id");
    const party = c.env.MY_DURABLE_OBJECT.getByName(id);

    const body = await c.req.formData();
    const prompt = body.get("prompt");

    if (typeof prompt !== "string") {
        return new Response("Invalid prompt", { status: 400 });
    }

    await party.sendPrompt(prompt, "user");

    return c.text("Proompt accepted", 202);
});

app.get("/party/:id/messages", async (c) => {
    const id = c.req.param("id");

    const party = c.env.MY_DURABLE_OBJECT.getByName(id);
    const request = new Request(c.req.url, {
        method: "GET",
        headers: { Upgrade: "websocket" },
    });
    const handle = await party.fetch(request); //.subscribe();

    if (handle.webSocket === null) {
        throw new Error("Subscription failed, no WebSocket in response");
    }

    const socket = handle.webSocket;
    socket.accept();

    return streamSSE(c, async (stream) => {
        await stream.writeSSE({ event: "started", data: "started" });
        const startTime = performance.now();
        const keepAlive = setInterval(() => {
            stream.writeSSE({
                event: "keepalive",
                data: performance.now() - startTime + "ms",
            });
        }, 5_000);

        const subscription = new Subscription<SubscriptionMessage>(socket);

        for await (const message of subscription.messages()) {
            console.log("subscription message received", message.type);
            switch (message.type) {
                case "join":
                    await stream.writeSSE({
                        data: (
                            <>
                                {message.messages.map((message) => (
                                    <Message roomId={id} message={message} />
                                ))}
                            </>
                        ),
                        event: "message",
                    });
                    break;
                case "message":
                    await stream.writeSSE({
                        data: <Message roomId={id} message={message.message} />,
                        event: "message",
                    });
                    break;
                case "messageStream":
                    await stream.writeSSE({
                        data: (
                            <Message roomId={id} message={message.messageId} />
                        ),
                        event: "message",
                    });
                    break;
            }
        }

        console.log("subscription closed");
        await stream.writeSSE({
            data: "it is finished",
            event: "finished",
        });
        await stream.close();
        clearInterval(keepAlive);
    });
});

app.get("/party/:id/messages/:messageid", async (c) => {
    const id = c.req.param("id");
    const messageid = Number(c.req.param("messageid"));
    if (isNaN(messageid) || messageid < 0) {
        return new Response(`Invalid messageid: ${c.req.param("messageid")}`, {
            status: 400,
        });
    }
    const party = c.env.MY_DURABLE_OBJECT.getByName(id);
    const response = await party.streamMessage(c.req.raw, messageid);

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
export { MyDurableObject } from "@/durable_objects/party";
