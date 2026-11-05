import { describe, expect, it, vi } from 'vitest';
import { proxyPostHog } from './posthog-proxy';

const HOST = 'https://eu.i.posthog.com';

const forward = async (path: string, init?: RequestInit) => {
    const send = vi.fn(async () => new Response('ok', { status: 200 }));
    const response = await proxyPostHog(
        new Request(`https://proompting.party${path}`, init),
        HOST,
        send as unknown as typeof fetch,
    );
    const [url, sent] = (send.mock.calls[0] ?? []) as unknown as [
        string,
        RequestInit,
    ];
    return { response, url, sent, send };
};

describe('proxyPostHog', () => {
    it('leaves other paths to the app', async () => {
        const { response, send } = await forward('/party-lines/e/');

        expect(response).toBeNull();
        expect(send).not.toHaveBeenCalled();
    });

    it('sends events to the API host, keeping the query', async () => {
        const { url } = await forward('/party-line/e/?compression=gzip-js');

        expect(url).toBe(`${HOST}/e/?compression=gzip-js`);
    });

    it.each(['/static/recorder.js', '/array/phc_x/config.js'])(
        'sends %s to the asset host',
        async (path) => {
            const { url } = await forward(`/party-line${path}`);

            expect(url).toBe(`https://eu-assets.i.posthog.com${path}`);
        },
    );

    it('cannot be pointed at another host', async () => {
        const { url } = await forward('/party-line//evil.example/steal');

        expect(new URL(url).host).toBe('eu.i.posthog.com');
    });

    it('never forwards our session, but keeps the client IP', async () => {
        const { sent } = await forward('/party-line/e/', {
            method: 'POST',
            body: 'payload',
            headers: {
                cookie: '__session=secret',
                authorization: 'Bearer secret',
                origin: 'https://proompting.party',
                'x-forwarded-for': '203.0.113.7',
            },
        });
        const headers = sent.headers as Headers;

        expect(headers.get('cookie')).toBeNull();
        expect(headers.get('authorization')).toBeNull();
        expect(headers.get('origin')).toBeNull();
        expect(headers.get('x-forwarded-for')).toBe('203.0.113.7');
        expect(new TextDecoder().decode(sent.body as ArrayBuffer)).toBe(
            'payload',
        );
    });

    it('lets no PostHog cookie onto our domain', async () => {
        const send = async () =>
            new Response('ok', {
                headers: { 'set-cookie': 'a=b', 'content-encoding': 'gzip' },
            });

        const response = await proxyPostHog(
            new Request('https://proompting.party/party-line/flags/'),
            HOST,
            send as unknown as typeof fetch,
        );

        expect(response?.headers.get('set-cookie')).toBeNull();
        expect(response?.headers.get('content-encoding')).toBeNull();
    });

    it('is a 404 when PostHog is off', async () => {
        const response = await proxyPostHog(
            new Request('https://proompting.party/party-line/e/'),
            undefined,
        );

        expect(response?.status).toBe(404);
    });
});
