import { Hono } from "hono";
import { html } from "hono/html";
import type { PropsWithChildren } from "hono/jsx";
import { streamSSE } from "hono/streaming";
import { Desktop } from "./components/desktop";
import { Message, UserMessage } from "./components/message";
import { OpenParty } from "./components/openParty";
import { Party } from "./components/party";
import type { MyDurableObject } from "./durable_objects/party";
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

// Party-durable-objects this worker is subscribed to so it can distribute
// knowledge of new messages to listeners over SSE
const subscriptions = new Map<string, Subscription<unknown>>();

app.get("/party/:id", async (c) => {
    const id = c.req.param("id");
    const socket = subscriptions.get(id);
    console.log(123);

    if (!socket) {
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
        const subscription = new Subscription(socket);
        subscriptions.set(id, subscription);

        socket.addEventListener("close", () => {
            subscriptions.delete(id);
        });
    }

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
    const subscription = subscriptions.get(id);
    if (!subscription) {
        return new Response("Party not found", { status: 404 });
    }

    return streamSSE(c, async (stream) => {
        while (true) {
            console.log("Waiting for messages for room", id);
            for await (const value of subscription.messages()) {
                console.log(1);
                await stream.writeSSE({
                    data: String(value), //`<span>${value}</span>`,
                    event: "message",
                });
            }
            console.log(2);

            await stream.writeSSE({
                data: "it is finished",
                event: "finished",
            });
            await stream.close();
            break;
        }
    });
});

app.post("/party/:id/messages/:messageid", async (c) => {
    const id = c.req.param("id");
    const messageid = Number(c.req.param("messageid"));
    if (isNaN(messageid) || messageid < 0) {
        return new Response(`Invalid messageid: ${c.req.param("messageid")}`, {
            status: 400,
        });
    }
    const party = c.env.MY_DURABLE_OBJECT.getByName(id);
    const message = await party.getMessage(messageid);

    // TODO switch on message null, if it's null we should sub if it isn't
    // we can just render it as-is? Or maybe do this branching in the component
    // itself?
    return c.html(<Message message={message} roomId={id} />);
});

app.get("/party/:id/messages/:messageid/sub", async (c) => {
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
