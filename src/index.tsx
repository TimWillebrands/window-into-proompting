import { Hono } from "hono";
import { html } from "hono/html";
import type { PropsWithChildren } from "hono/jsx";
import { streamSSE } from "hono/streaming";
import { Desktop } from "./components/desktop";
import { Message } from "./components/message";
import { OpenParty, type Party as PartyType } from "./components/openParty";
import { Party } from "./components/party";
import { Welcome } from "./components/welcome";
import { models, type SubscriptionMessage } from "./durable_objects/party";
import { addPersonaRoutes } from "./personaRoutes";
import { createPostHogProxy, PROXY_PATH } from "./posthog";
import { Subscription } from "./subscription";

const app = new Hono<{ Bindings: Cloudflare.Env }>();
export type AppType = typeof app;

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
                        // Generate or retrieve user ID
                        let userId = localStorage.getItem('proompting_user_id');
                        if (!userId) {
                            userId = 'user_' + Date.now() + '_' + Math.random().toString(36).substr(2, 9);
                            localStorage.setItem('proompting_user_id', userId);
                        }
                        
                        // Identify user with PostHog
                        posthog.identify(userId, {
                            first_seen: new Date().toISOString(),
                            platform: 'web',
                            app_version: '1.0.0'
                        });
                        
                        // Track session end on page unload
                        window.addEventListener('beforeunload', function() {
                            if (window.analytics) {
                                window.analytics.trackSessionEnded();
                            }
                        });
                    })();
                </script>

                <script
                    async
                    crossorigin="anonymous"
                    data-clerk-publishable-key="pk_test_aGFwcHktYmVuZ2FsLTY2LmNsZXJrLmFjY291bnRzLmRldiQ"
                    src="https://happy-bengal-66.clerk.accounts.dev/npm/@clerk/clerk-js@5/dist/clerk.browser.js"
                    type="text/javascript"
                ></script>
            </head>
            <body hx-ext="sse" >
                ${props.children}
            </body>
        </html>`;

// PostHog reverse proxy
app.route(PROXY_PATH, createPostHogProxy());

app.get("/", (c) => {
    return c.html(
        <Layout title="My Party">
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

    console.log(partyData.keys);

    return c.html(<OpenParty previousParties={parties} />);
});

app.post("/party/create", async (c) => {
    const body = await c.req.formData();
    const partyName = body.get("partyName")?.toString();
    if (!partyName) return new Response("Invalid party name", { status: 400 });
    const partyId = crypto.randomUUID();

    const desktopData = c.env.DESKTOP_DATA;
    const party = {
        id: partyId,
        name: partyName,
    };

    await desktopData.put(`party:${partyId}`, JSON.stringify(party), {
        metadata: party,
    });

    return c.redirect(`/party/${partyId}`);
});

app.get("/party/:id", async (c) => {
    const id = c.req.param("id");
    const party = await c.env.DESKTOP_DATA.get<PartyType>(`party:${id}`, {
        type: "json",
    });

    if (!party) return new Response("Party not found", { status: 404 });

    return c.html(<Party room={id} />);
});

app.post("/party/:id/prompt", async (c) => {
    const id = c.req.param("id");
    const party = c.env.MY_DURABLE_OBJECT.getByName(id);

    const body = await c.req.formData();
    const prompt = body.get("prompt");
    const model = body.get("model");
    const finalModel = models.find((m) => m === model) ?? models[0];

    if (typeof prompt !== "string") {
        return new Response("Invalid prompt", { status: 400 });
    }

    await party.sendPrompt(prompt, "user", finalModel);

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
    if (Number.isNaN(messageid) || messageid < 0) {
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

addPersonaRoutes(app);

export default app;
export { MyDurableObject } from "@/durable_objects/party";
