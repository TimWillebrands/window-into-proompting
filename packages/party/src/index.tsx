import { getAuth } from "@hono/clerk-auth";
import { Hono } from "hono";
import { streamSSE } from "hono/streaming";
import {
    Subscription,
    type PartyInfo,
    type PartyInfoFull,
    type SubscriptionMessage,
    type Party as PartyType
} from "@proompting/core";
import {
    createLLMProvider,
    getAllPersonas,
    clerkClient
} from "@proompting/backend";
import { Message } from "./components/message";
import { OpenParty } from "./components/openParty";
import { Party } from "./components/party";

const app = new Hono<{ Bindings: Cloudflare.Env }>();

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

    const provider = createLLMProvider(c.env);
    const models = await provider.getModels();
    var personas = await getAllPersonas(c.env);

    return c.html(
        <Party
            room={id}
            models={models}
            personaParticipants={personas}
        />,
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
    if (!partyInfoStrFin) return new Response("Party not found", { status: 404 });

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

    if (typeof prompt !== "string") return new Response("Invalid prompt", { status: 400 });
    if (typeof model !== "string") return new Response("Invalid model", { status: 400 });
    if (typeof personaId !== "string") return new Response("Invalid persona", { status: 400 });

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

// Proceed
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

    if (typeof model !== "string") return new Response("Invalid model", { status: 400 });
    if (typeof personaId !== "string") return new Response("Invalid persona", { status: 400 });

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

// Messages stream
app.get("/:id/messages", async (c) => {
    const id = c.req.param("id");
    const party = c.env.MY_DURABLE_OBJECT.getByName(id);
    const request = new Request(c.req.url, {
        method: "GET",
        headers: { Upgrade: "websocket" },
    });
    const handle = await party.fetch(request);

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

        const personasPromise = getAllPersonas(c.env);
        const subscription = new Subscription<SubscriptionMessage>(socket);
        const personas = await personasPromise;
        const personaMap = new Map<string, string>(personas.map((p) => [p.id, p.name]));

        for await (const message of subscription.messages()) {
            switch (message.type) {
                case "join":
                    await stream.writeSSE({
                        data: (
                            <>
                                {message.messages.map((msg) => (
                                    <Message
                                        roomId={id}
                                        message={msg}
                                        personaName={personaMap.get(msg.senderId)}
                                    />
                                ))}
                            </>
                        ).toString(),
                        event: "message",
                    });
                    break;
                case "message":
                    await stream.writeSSE({
                        data: (
                            <Message
                                roomId={id}
                                message={message.message}
                                personaName={personaMap.get(message.message.senderId)}
                            />
                        ).toString(),
                        event: "message",
                    });
                    break;
                case "messageStream":
                    await stream.writeSSE({
                        data: (
                            <Message
                                roomId={id}
                                message={message.messageId}
                            />
                        ).toString(),
                        event: "message",
                    });
                    break;
            }
        }
        clearInterval(keepAlive);
    });
});

// Single message stream
app.get("/:id/messages/:msgId", async (c) => {
    const id = c.req.param("id");
    const msgId = c.req.param("msgId");

    const party = c.env.MY_DURABLE_OBJECT.getByName(id);
    const response = await party.fetch(
        new Request(`http://do/messages/${msgId}`, {
            headers: { Accept: "text/event-stream" },
        }),
    );

    return c.body(response.body as any, {
        headers: {
            "Content-Type": "text/event-stream",
            "Cache-Control": "no-cache",
            Connection: "keep-alive",
        },
    });
});

// Delete message
app.delete("/:id/messages/:msgId", async (c) => {
    const id = c.req.param("id");
    const msgId = c.req.param("msgId");

    const party = c.env.MY_DURABLE_OBJECT.getByName(id);
    const response = await party.fetch(
        new Request(`http://do/messages/${msgId}`, {
            method: "DELETE",
        }),
    );

    return response;
});

// Delete messages after
app.delete("/:id/messages-after/:msgId", async (c) => {
    const id = c.req.param("id");
    const msgId = c.req.param("msgId");

    const party = c.env.MY_DURABLE_OBJECT.getByName(id);
    const response = await party.fetch(
        new Request(`http://do/messages-after/${msgId}`, {
            method: "DELETE",
        }),
    );

    return response;
});

// Re-prompt
app.post("/:id/re-prompt/:msgId", async (c) => {
    const id = c.req.param("id");
    const msgId = c.req.param("msgId");
    const body = await c.req.formData();
    const model = body.get("model")?.toString();
    const personaId = body.get("personaId")?.toString();

    const party = c.env.MY_DURABLE_OBJECT.getByName(id);
    const response = await party.fetch(
        new Request(`http://do/re-prompt/${msgId}`, {
            method: "POST",
            body: JSON.stringify({ model, personaId }),
        }),
    );

    return response;
});

export default app;
