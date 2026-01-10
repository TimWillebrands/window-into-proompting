import { type ClerkClient, createClerkClient } from "@clerk/backend";
import { clerkMiddleware, getAuth } from "@hono/clerk-auth";
import { EventSourceParserStream } from "eventsource-parser/stream";
import { type Context, Hono } from "hono";
import { env } from "hono/adapter";
import { html } from "hono/html";
import type { PropsWithChildren } from "hono/jsx";
import { streamSSE } from "hono/streaming";
import { Desktop } from "./components/desktop/desktop";
import { Message, MessageHeader } from "./components/party/message";
import { OpenParty, type Party as PartyType } from "./components/party/openParty";
import { Party } from "./components/party/party";
import { Welcome } from "./components/welcome/welcome";
import type { Persona } from "./components/persona-management/personas";
import type {
    PartyInfo,
    PartyInfoFull,
    SubscriptionMessage,
} from "./durable_objects/party";
import { createLLMProvider } from "./providers/factory";
import { addPersonaRoutes, getAllPersonas } from "./personaRoutes";
import { createPostHogProxy, PROXY_PATH } from "./posthog";
import { Subscription } from "./subscription";

const app = new Hono<{ Bindings: Cloudflare.Env }>();

let _clerkClient: ClerkClient | null = null;
function clerkClient(c: Context) {
    if (_clerkClient === null) {
        _clerkClient = createClerkClient({ secretKey: c.env.CLERK_SECRET_KEY });
    }
    return _clerkClient;
}

export type AppType = typeof app;

interface SiteData {
    title: string;
    isProduction: boolean;
}

const Layout = (props: PropsWithChildren<SiteData>) =>
    html`<!doctype html>
        <html>
            <head>
                <title>${props.title}</title>
                <script src="script.js"></script>
                <script src="https://cdn.jsdelivr.net/npm/htmx.org@2.0.6/dist/htmx.min.js"></script>
                <script src="https://cdn.jsdelivr.net/npm/htmx-ext-sse@2.2.2"></script>
                <script type="module" src="vendor/streaming-md.js"></script>

                <script defer src="https://cdn.jsdelivr.net/npm/@alpinejs/resize@3.x.x/dist/cdn.min.js"></script>
                <script defer src="https://cdn.jsdelivr.net/npm/@alpinejs/persist@3.x.x/dist/cdn.min.js"></script>
                <script defer src="https://cdn.jsdelivr.net/npm/alpinejs@3.x.x/dist/cdn.min.js"></script>

                <link rel="stylesheet" href="https://unpkg.com/xp.css" >
                <link rel="stylesheet" href="output.css" >

                <script>
                    !function(t,e){var o,n,p,r;e.__SV||(window.posthog && window.posthog.__loaded)||(window.posthog=e,e._i=[],e.init=function(i,s,a){function g(t,e){var o=e.split(".");2==o.length&&(t=t[o[0]],e=o[1]),t[e]=function(){t.push([e].concat(Array.prototype.slice.call(arguments,0)))}}(p=t.createElement("script")).type="text/javascript",p.crossOrigin="anonymous",p.async=!0,p.src=s.api_host.replace(".i.posthog.com","-assets.i.posthog.com")+"/static/array.js",(r=t.getElementsByTagName("script")[0]).parentNode.insertBefore(p,r);var u=e;for(void 0!==a?u=e[a]=[]:a="posthog",u.people=u.people||[],u.toString=function(t){var e="posthog";return"posthog"!==a&&(e+="."+a),t||(e+=" (stub)"),e},u.people.toString=function(){return u.toString(1)+".people (stub)"},o="init hi $r kr ui wr Er capture Ri calculateEventProperties Ir register register_once register_for_session unregister unregister_for_session Fr getFeatureFlag getFeatureFlagPayload isFeatureEnabled reloadFeatureFlags updateEarlyAccessFeatureEnrollment getEarlyAccessFeatures on onFeatureFlags onSurveysLoaded onSessionId getSurveys getActiveMatchingSurveys renderSurvey displaySurvey canRenderSurvey canRenderSurveyAsync identify setPersonProperties group resetGroups setPersonPropertiesForFlags resetPersonPropertiesForFlags setGroupPropertiesForFlags resetGroupPropertiesForFlags reset get_distinct_id getGroups get_session_id get_session_replay_url alias set_config startSessionRecording stopSessionRecording sessionRecordingStarted captureException loadToolbar get_property getSessionProperty Cr Tr createPersonProfile Or yr Mr opt_in_capturing opt_out_capturing has_opted_in_capturing has_opted_out_capturing get_explicit_consent_status is_capturing clear_opt_in_out_capturing Pr debug L Rr getPageViewId captureTraceFeedback captureTraceMetric gr".split(" "),n=0;n<o.length;n++)g(u,o[n]);e._i.push([i,s,a])},e.__SV=1)}(document,window.posthog||[]);
                    posthog.init('phc_f44OvBqb7P19kNmbDBXlNy4UH8pdoiJcUVKZJ1aN950', {
                        api_host: '${PROXY_PATH}',
                        ui_host: 'https://eu.posthog.com',
                        defaults: '2025-05-24',
                        person_profiles: 'identified_only', // or 'always' to create profiles for anonymous users as well
                    });

                    // Set up user identification and session tracking
                    (function() {
                        // Track session end on page unload
                        window.addEventListener('beforeunload', function() {
                            if (window.analytics) {
                                window.analytics.trackSessionEnded();
                            }
                        });
                    })();
                </script>

                ${props.isProduction ? (
            <script
                async
                crossorigin="anonymous"
                data-clerk-publishable-key="pk_live_Y2xlcmsucHJvb21wdGluZy5wYXJ0eSQ"
                src="https://clerk.proompting.party/npm/@clerk/clerk-js@5/dist/clerk.browser.js"
                type="text/javascript"
            ></script>
        ) : (
            <script
                async
                crossorigin="anonymous"
                data-clerk-publishable-key="pk_test_aGFwcHktYmVuZ2FsLTY2LmNsZXJrLmFjY291bnRzLmRldiQ"
                src="https://happy-bengal-66.clerk.accounts.dev/npm/@clerk/clerk-js@5/dist/clerk.browser.js"
                type="text/javascript"
            ></script>
        )
        }
            </head>
            <body hx-ext="sse" >
                ${props.children}
            </body>
        </html>`;

app.use("*", clerkMiddleware());

// PostHog reverse proxy
app.route(PROXY_PATH, createPostHogProxy());

app.get("/", (c) => {
    const { PROD_ENV } = env<{ PROD_ENV?: string }>(c);
    return c.html(
        <Layout
            title="🎭 Proompting Party 🎉"
            isProduction={PROD_ENV === "Production"}
        >
            <Desktop></Desktop>
        </Layout>,
    );
});

// Welcome tour application
app.get("/welcome", (c) => {
    return c.html(<Welcome />);
});

// OpenParty application
app.get("/party", async (c) => {
    const partyData = await c.env.DESKTOP_DATA.list<PartyType>({
        prefix: "party:",
    });
    const parties = partyData.keys
        .map((key) => key.metadata)
        .filter((party) => party !== undefined);

    return c.html(<OpenParty previousParties={parties} />);
});

app.post("/party/create", async (c) => {
    const auth = getAuth(c);

    if (!auth?.userId || !auth.isAuthenticated) {
        return new Response("Unauthorized!!", { status: 401 });
    }

    const body = await c.req.formData();
    const partyName = body.get("partyName")?.toString();
    if (!partyName) return new Response("Ievalid party name", { status: 400 });
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

app.get("/party/:id", async (c) => {
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

app.get("/party/:id/reset-participants", async (c) => {
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
    const currentParticipants = await party.setParticipants(participants);
    await desktopData.put(`party:${id}`, JSON.stringify(partyInfo));

    const partyInfoStrFin = await desktopData.get(`party:${id}`);

    if (!partyInfoStrFin)
        return new Response("Party not found", { status: 404 });

    const partyInfoFin = JSON.parse(partyInfoStrFin) as PartyInfoFull;
    return c.json(partyInfoFin);
});

app.get("/party/:id/messages/raw", async (c) => {
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

app.post("/party/:id/prompt", async (c) => {
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

    if (typeof prompt !== "string") {
        return new Response("Invalid prompt", { status: 400 });
    }

    if (typeof model !== "string") {
        return new Response("Invalid model", { status: 400 });
    }

    if (typeof personaId !== "string") {
        return new Response("Invalid persona", { status: 400 });
    }

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

app.post("/party/:id/proceed", async (c) => {
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

        // Start fetching personas immediately, but don't await yet
        const personasPromise = getAllPersonas(c.env);

        // Start subscription immediately to catch "join" message
        const subscription = new Subscription<SubscriptionMessage>(socket);

        // Now await the personas
        const personas = await personasPromise;
        const personaMap = new Map(personas.map((p) => [p.id, p.name]));

        for await (const message of subscription.messages()) {
            console.log("subscription message received", message.type);
            switch (message.type) {
                case "join":
                    await stream.writeSSE({
                        data: (
                            <>
                                {message.messages.map((message) => (
                                    <Message
                                        roomId={id}
                                        message={message}
                                        personaName={
                                            personaMap.get(message.senderId) ??
                                            undefined
                                        }
                                    />
                                ))}
                            </>
                        ),
                        event: "message",
                    });
                    break;
                case "message":
                    await stream.writeSSE({
                        data: (
                            <Message
                                roomId={id}
                                message={message.message}
                                personaName={
                                    personaMap.get(message.message.senderId) ??
                                    undefined
                                }
                            />
                        ),
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
                                personaName={
                                    (
                                        await c.env.DESKTOP_DATA.get<Persona>(
                                            `persona:${personaId}`,
                                            { type: "json" },
                                        )
                                    )?.name
                                }
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

app.delete("/party/:id/messages/:messageid", async (c) => {
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

addPersonaRoutes(app);

export default app;
export { MyDurableObject } from "@/durable_objects/party";
