/** Same-origin path for PostHog; obvious names like /posthog get blocked. */
export const PROXY_PATH = '/party-line';

/** Our auth stays here; Origin makes PostHog redirect asset requests to its login. */
const DROPPED_REQUEST_HEADERS = [
    'authorization',
    'connection',
    'content-length',
    'cookie',
    'host',
    'origin',
    'referer',
];

/** fetch has already decompressed the body, and PostHog gets no cookies on our domain. */
const DROPPED_RESPONSE_HEADERS = [
    'content-encoding',
    'content-length',
    'set-cookie',
];

const assetHost = (host: string) =>
    host.replace('.i.posthog.com', '-assets.i.posthog.com');

/** Forwards the PostHog path upstream; null for every other request. */
export const proxyPostHog = async (
    request: Request,
    host: string | undefined,
    send: typeof fetch = fetch,
): Promise<Response | null> => {
    const url = new URL(request.url);
    if (!url.pathname.startsWith(`${PROXY_PATH}/`)) return null;
    if (!host) return new Response(null, { status: 404 });

    const path = url.pathname.slice(PROXY_PATH.length);
    const upstream = /^\/(static|array)\//.test(path) ? assetHost(host) : host;
    const headers = new Headers(request.headers);
    for (const name of DROPPED_REQUEST_HEADERS) headers.delete(name);

    const hasBody = request.method !== 'GET' && request.method !== 'HEAD';
    // Concatenated, not new URL(path, base): a //host path must not swap the upstream.
    const response = await send(`${upstream}${path}${url.search}`, {
        method: request.method,
        headers,
        body: hasBody ? await request.arrayBuffer() : undefined,
        redirect: 'manual',
    });

    const responseHeaders = new Headers(response.headers);
    for (const name of DROPPED_RESPONSE_HEADERS) responseHeaders.delete(name);
    return new Response(response.body, {
        status: response.status,
        statusText: response.statusText,
        headers: responseHeaders,
    });
};
