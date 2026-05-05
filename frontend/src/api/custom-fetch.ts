export class HttpError extends Error {
    status: number;
    body: string;

    constructor(status: number, statusText: string, body: string) {
        const snippet = body ? `: ${body.slice(0, 200)}` : '';
        super(`HTTP ${status} ${statusText || ''}${snippet}`.trim());
        this.name = 'HttpError';
        this.status = status;
        this.body = body;
    }
}

const NO_BODY_STATUSES = new Set([204, 205, 304]);

export const customFetch = async <T>(
    url: string,
    init?: RequestInit,
): Promise<T> => {
    const res = await fetch(url, init);
    const body = NO_BODY_STATUSES.has(res.status) ? '' : await res.text();

    if (!res.ok) {
        throw new HttpError(res.status, res.statusText, body);
    }

    const data = body ? JSON.parse(body) : undefined;
    return { data, status: res.status, headers: res.headers } as T;
};
