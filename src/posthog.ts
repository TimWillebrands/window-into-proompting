import { Hono } from "hono";

// PostHog reverse proxy configuration
const POSTHOG_API_HOST = "https://eu.i.posthog.com";
const POSTHOG_ASSET_HOST = "https://eu-assets.i.posthog.com";
const PROXY_PATH = "/phg-9b7e";

/**
 * Create a Hono router configured to proxy PostHog API requests and serve PostHog static assets.
 *
 * The returned router is typed for Cloudflare environment bindings and includes routes for cached
 * static asset delivery and request forwarding to the PostHog API.
 *
 * @returns A Hono router instance (typed with `Bindings: Cloudflare.Env`) that proxies requests to PostHog and serves its static assets.
 */
export function createPostHogProxy() {
    const posthogProxy = new Hono<{ Bindings: Cloudflare.Env }>();

    // Handle static assets with caching
    posthogProxy.get("/static/*", async (c) => {
        const url = new URL(c.req.url);
        const upstreamPath = url.pathname.replace(
            new RegExp(`^${PROXY_PATH}`),
            "",
        );
        const pathWithParams = upstreamPath + url.search;

        let response = await caches.default.match(c.req.raw);
        if (!response) {
            response = await fetch(`${POSTHOG_ASSET_HOST}${pathWithParams}`);
            c.executionCtx.waitUntil(
                caches.default.put(c.req.raw, response.clone()),
            );
        }
        return response;
    });

    // Forward all other requests to PostHog API
    posthogProxy.all("*", async (c) => {
        const url = new URL(c.req.url);
        const upstreamPath = url.pathname.replace(
            new RegExp(`^${PROXY_PATH}`),
            "",
        );
        const pathWithParams = upstreamPath + url.search;

        const originRequest = new Request(c.req.raw);
        originRequest.headers.delete("cookie");
        return await fetch(
            `${POSTHOG_API_HOST}${pathWithParams}`,
            originRequest,
        );
    });

    return posthogProxy;
}

export { PROXY_PATH };