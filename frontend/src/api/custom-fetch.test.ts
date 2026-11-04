import { afterEach, describe, expect, it, vi } from 'vitest';
import { customFetch } from './custom-fetch';

const stubClerk = (token: string | null) => {
    const redirectToSignIn = vi.fn().mockResolvedValue(undefined);
    vi.stubGlobal('window', {
        Clerk: { session: { getToken: async () => token }, redirectToSignIn },
    });
    return redirectToSignIn;
};

const stubFetch = (status: number) => {
    const fetchMock = vi.fn(
        async (_url: string, _init?: RequestInit) =>
            new Response('{}', {
                status,
                headers: { 'content-type': 'application/json' },
            }),
    );
    vi.stubGlobal('fetch', fetchMock);
    return fetchMock;
};

afterEach(() => vi.unstubAllGlobals());

describe('customFetch auth', () => {
    it('sends the session token as a bearer', async () => {
        stubClerk('tok');
        const fetchMock = stubFetch(200);

        await customFetch('/api/x', { headers: { 'x-a': '1' } });

        const headers = new Headers(fetchMock.mock.calls[0][1]?.headers);
        expect(headers.get('authorization')).toBe('Bearer tok');
        expect(headers.get('x-a')).toBe('1');
    });

    it('sends no bearer for a visitor', async () => {
        stubClerk(null);
        const fetchMock = stubFetch(200);

        await customFetch('/api/x');

        const headers = new Headers(fetchMock.mock.calls[0][1]?.headers);
        expect(headers.has('authorization')).toBe(false);
    });

    it('sends a visitor to sign-in when a write needs it', async () => {
        const redirect = stubClerk(null);
        stubFetch(401);

        await expect(
            customFetch('/api/x', { method: 'POST' }),
        ).rejects.toThrow();

        expect(redirect).toHaveBeenCalledOnce();
    });

    it('leaves a refused read to the window that asked', async () => {
        const redirect = stubClerk(null);
        stubFetch(401);

        await expect(customFetch('/api/x')).rejects.toThrow();

        expect(redirect).not.toHaveBeenCalled();
    });
});
