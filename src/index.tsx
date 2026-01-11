import { clerkMiddleware } from "@hono/clerk-auth";
import { Hono } from "hono";
import { html } from "hono/html";
import type { PropsWithChildren } from "hono/jsx";
import { Desktop } from "@proompting/desktop";
import partyApp from "@proompting/party";
import personaApp from "@proompting/persona";
import welcomeApp from "@proompting/welcome";
import { createPostHogProxy, PROXY_PATH } from "./posthog";

const app = new Hono<{ Bindings: Cloudflare.Env }>();

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
                        person_profiles: 'identified_only',
                    });

                    (function() {
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

// Proxy PostHog requests
app.all(PROXY_PATH, createPostHogProxy());

// Mount sub-apps
app.route("/party", partyApp);
app.route("/personas", personaApp);
app.route("/welcome", welcomeApp);

// Main route
app.get("/", (c) => {
    const isProduction = c.env.ENVIRONMENT === "production";
    return c.html(
        <Layout title="Proompt.party" isProduction={isProduction}>
            <Desktop />
        </Layout>,
    );
});

export default app;
export { MyDurableObject } from "@proompting/backend";
