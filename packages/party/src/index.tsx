import { getAuth } from "@hono/clerk-auth";
import {
    clerkClient,
    createLLMProvider,
    getAllPersonas,
} from "@proompting/backend";
import {
    type PartyInfo,
    type PartyInfoFull,
    type Party as PartyType,
    Subscription,
    type SubscriptionMessage,
} from "@proompting/core";
import { EventSourceParserStream } from "eventsource-parser/stream";
import { Hono } from "hono";
import { streamSSE } from "hono/streaming";
import { PostHog } from "posthog-node";
import { Message, MessageHeader } from "./components/message";
import { OpenParty } from "./components/openParty";
import { Party } from "./components/party";

const app = new Hono<{ Bindings: Cloudflare.Env }>();

const phClient = new PostHog(
    "phc_f44OvBqb7P19kNmbDBXlNy4UH8pdoiJcUVKZJ1aN950",
    { host: "https://eu.i.posthog.com" },
);

// List parties
app.get("/", async (c) => {
    const partyData = await c.env.DESKTOP_DATA.list<PartyType>({
        prefix: "party:",
    });
    const parties = partyData.keys
        .map((key) => key.metadata)
        .filter((party) => party !== undefined);

    return c.html(<OpenParty previousParties={parties} />);
});

// Create party
app.post("/create", async (c) => {
    const auth = getAuth(c);

    if (!auth?.userId || !auth.isAuthenticated) {
        return new Response("Unauthorized!!", { status: 401 });
    }

    const body = await c.req.formData();
    const partyName = body.get("partyName")?.toString();
    if (!partyName) return new Response("Invalid party name", { status: 400 });
    const partyId = crypto.randomUUID();

    const desktopData = c.env.DESKTOP_DATA;
    const user = await clerkClient(c).users.getUser(auth.userId);

    const partyInfo: PartyInfo = {
        id: partyId,
        name: partyName,
    };

    const participants = await getAllPersonas(c.env);

    const fullPartyInfo: PartyInfoFull = {
        ...partyInfo,
        participants: [
            ...participants,
            {
                id: user.id,
                name:
                    user.username ??
                    user.fullName ??
                    user.emailAddresses[0].emailAddress ??
                    user.id,
            },
        ],
    };

    await desktopData.put(`party:${partyId}`, JSON.stringify(fullPartyInfo), {
        metadata: partyInfo,
    });

    const party = c.env.MY_DURABLE_OBJECT.getByName(partyId);
    await party.setParticipants(fullPartyInfo.participants);

    return c.redirect(`/party/${partyId}`);
});

// Get party by ID
app.get("/:id", async (c) => {
    const id = c.req.param("id");
    const party = await c.env.DESKTOP_DATA.get<PartyType>(`party:${id}`, {
        type: "json",
    });

    if (!party) return new Response("Party not found", { status: 404 });

    const provider = createLLMProvider(c.env, phClient);
    const models = await provider.getModels();
    var personas = await getAllPersonas(c.env);

    return c.html(
        <Party room={id} models={models} personaParticipants={personas} />,
    );
});

// Reset participants
app.get("/:id/reset-participants", async (c) => {
    const id = c.req.param("id");
    const desktopData = c.env.DESKTOP_DATA;
    const partyInfoStr = await desktopData.get(`party:${id}`);

    if (!partyInfoStr) return new Response("Party not found", { status: 404 });

    const partyInfo = JSON.parse(partyInfoStr) as PartyInfoFull;

    const participants =
        partyInfo.participants === undefined ||
        partyInfo.participants.length === 0
            ? await getAllPersonas(c.env)
            : partyInfo.participants;

    partyInfo.participants = participants;

    const party = c.env.MY_DURABLE_OBJECT.getByName(id);
    await party.setParticipants(participants);
    await desktopData.put(`party:${id}`, JSON.stringify(partyInfo));

    const partyInfoStrFin = await desktopData.get(`party:${id}`);
    if (!partyInfoStrFin)
        return new Response("Party not found", { status: 404 });

    const partyInfoFin = JSON.parse(partyInfoStrFin) as PartyInfoFull;
    return c.json(partyInfoFin);
});

// Raw messages
app.get("/:id/messages/raw", async (c) => {
    const id = c.req.param("id");
    const party = c.env.MY_DURABLE_OBJECT.getByName(id);
    const responseType = c.req.header("Content-Type");

    const messages = await party.downloadMessages();

    if (responseType === "text/html") {
        return c.html(
            <ul>
                {messages.map((msg) => (
                    <li key={msg.messageid}>{msg.message}</li>
                ))}
            </ul>,
        );
    }

    return c.json(messages);
});

// Prompt
app.post("/:id/prompt", async (c) => {
    const auth = getAuth(c);

    if (!auth?.userId || !auth.isAuthenticated) {
        return new Response("Unauthorized!!", { status: 401 });
    }

    const id = c.req.param("id");
    const party = c.env.MY_DURABLE_OBJECT.getByName(id);

    const body = await c.req.formData();
    const prompt = body.get("prompt");
    const model = body.get("model");
    const personaId = body.get("personaId");

    if (typeof prompt !== "string")
        return new Response("Invalid prompt", { status: 400 });
    if (typeof model !== "string")
        return new Response("Invalid model", { status: 400 });
    if (typeof personaId !== "string")
        return new Response("Invalid persona", { status: 400 });

    const user = await clerkClient(c).users.getUser(auth.userId);

    await party.sendPrompt(
        prompt,
        user.username ??
            user.fullName ??
            user.emailAddresses[0].emailAddress ??
            user.id,
        model,
        id,
        personaId === "none" ? null : personaId,
    );

    return c.text("Proompt accepted", 202);
});

app.post("/:id/proceed", async (c) => {
    const auth = getAuth(c);

    if (!auth?.userId || !auth.isAuthenticated) {
        return new Response("Unauthorized!!", { status: 401 });
    }

    const id = c.req.param("id");
    const party = c.env.MY_DURABLE_OBJECT.getByName(id);

    const body = await c.req.formData();
    const model = body.get("model");
    const personaId = c.req.query("personaId");

    if (typeof model !== "string") {
        return new Response("Invalid model", { status: 400 });
    }

    if (typeof personaId !== "string") {
        return new Response("Invalid persona", { status: 400 });
    }

    const user = await clerkClient(c).users.getUser(auth.userId);

    await party.proceed(
        user.username ??
            user.fullName ??
            user.emailAddresses[0].emailAddress ??
            user.id,
        personaId,
        model,
        id,
    );

    return c.text("Proompt accepted", 202);
});

app.get("/:id/messages", async (c) => {
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
                data: `${performance.now() - startTime}ms`,
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
                case "deleteMessage":
                    await stream.writeSSE({
                        data: JSON.stringify({ messageId: message.messageId }),
                        event: "deleteMessage",
                    });
                    break;
                case "deleteMessagesAfter":
                    await stream.writeSSE({
                        data: JSON.stringify({ messageId: message.messageId }),
                        event: "deleteMessagesAfter",
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

app.get("/:id/messages/:messageid", async (c) => {
    const id = c.req.param("id");
    const messageid = Number(c.req.param("messageid"));
    if (Number.isNaN(messageid) || messageid < 0) {
        return new Response(`Invalid messageid: ${c.req.param("messageid")}`, {
            status: 400,
        });
    }
    const party = c.env.MY_DURABLE_OBJECT.getByName(id);
    const response = await party.streamMessage(messageid);

    if (!response.ok) {
        return response;
    }

    if (!response.body) {
        return new Response(
            JSON.stringify({
                error: "No body, are we streamin?",
                messageId: messageid,
                roomId: id,
            }),
            { status: 500 },
        );
    }

    const reader = response.body
        .pipeThrough(new TextDecoderStream())
        .pipeThrough(new EventSourceParserStream())
        .getReader();

    return streamSSE(c, async (stream) => {
        while (true) {
            const { value, done } = await reader.read();

            if (value) {
                if (value.event === "persona") {
                    const personaId = JSON.parse(value.data);
                    await stream.writeSSE({
                        data: (
                            <MessageHeader
                                personaId={personaId}
                                roomId={id}
                                sendAt={new Date().getUTCMilliseconds()}
                                messageId={messageid}
                            />
                        ),
                        event: value.event,
                    });
                } else {
                    await stream.writeSSE({
                        data: JSON.parse(value.data),
                        event: value.event,
                    });
                }
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

app.delete("/:id/messages/:messageid", async (c) => {
    const auth = getAuth(c);

    if (!auth?.userId || !auth.isAuthenticated) {
        return new Response("Unauthorized!!", { status: 401 });
    }

    const id = c.req.param("id");
    const messageid = Number(c.req.param("messageid"));
    if (Number.isNaN(messageid) || messageid < 0) {
        return new Response(`Invalid messageid: ${c.req.param("messageid")}`, {
            status: 400,
        });
    }

    const party = c.env.MY_DURABLE_OBJECT.getByName(id);
    return party.deleteMessage(messageid);
});

app.delete("/:id/messages-after/:messageid", async (c) => {
    const auth = getAuth(c);

    if (!auth?.userId || !auth.isAuthenticated) {
        return new Response("Unauthorized!!", { status: 401 });
    }

    const id = c.req.param("id");
    const messageId = Number(c.req.param("messageid"));
    if (Number.isNaN(messageId) || messageId < 0) {
        return new Response(`Invalid messageid: ${c.req.param("messageid")}`, {
            status: 400,
        });
    }

    const party = c.env.MY_DURABLE_OBJECT.getByName(id);
    return await party.deleteMessagesAfter(messageId);
});

// Re-prompt
app.post("/:id/reprompt/:messageid", async (c) => {
    const auth = getAuth(c);

    if (!auth?.userId || !auth.isAuthenticated) {
        return new Response("Unauthorized!!", { status: 401 });
    }
    const id = c.req.param("id");
    const messageId = Number(c.req.param("messageid"));
    const body = await c.req.formData();
    const model = body.get("model")?.toString();

    if (model === undefined || model === null) {
        return new Response("Invalid model", { status: 400 });
    }

    const party = c.env.MY_DURABLE_OBJECT.getByName(id);

    await party.deleteMessagesAfter(messageId);
    return party.proceed(auth.userId, null, model, id);
});

export default app;
