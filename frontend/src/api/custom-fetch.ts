import type { ProblemDetails } from './model/problemDetails';

export class ProblemDetailsError extends Error implements ProblemDetails {
    type?: string | null;
    title?: string | null;
    status?: number | string | null;
    detail?: string | null;
    instance?: string | null;

    constructor(problem: ProblemDetails, statusText?: string) {
        const headline =
            problem.title ||
            problem.detail ||
            statusText ||
            (problem.status != null ? `HTTP ${problem.status}` : 'Request failed');
        const snippet = problem.detail && problem.detail !== headline
            ? `: ${problem.detail.slice(0, 200)}`
            : '';
        super(`${headline}${snippet}`.trim());
        this.name = 'ProblemDetailsError';
        this.type = problem.type;
        this.title = problem.title;
        this.status = problem.status;
        this.detail = problem.detail;
        this.instance = problem.instance;
    }
}

const NO_BODY_STATUSES = new Set([204, 205, 304]);

const isJsonContentType = (ct: string | null): boolean => {
    if (!ct) return false;
    const lower = ct.toLowerCase();
    return lower.includes('application/json') || lower.includes('+json');
};

const tryParseJson = (text: string): unknown => {
    try {
        return JSON.parse(text);
    } catch {
        return undefined;
    }
};

const isProblemDetailsLike = (value: unknown): value is ProblemDetails => {
    if (!value || typeof value !== 'object') return false;
    const v = value as Record<string, unknown>;
    return (
        'type' in v ||
        'title' in v ||
        'status' in v ||
        'detail' in v ||
        'instance' in v
    );
};

export const customFetch = async <T>(
    url: string,
    init?: RequestInit,
): Promise<T> => {
    const res = await fetch(url, init);
    const contentType = res.headers.get('content-type');
    const hasBody = !NO_BODY_STATUSES.has(res.status);
    const body = hasBody ? await res.text() : '';

    if (!res.ok) {
        let problem: ProblemDetails;
        if (body && isJsonContentType(contentType)) {
            const parsed = tryParseJson(body);
            problem = isProblemDetailsLike(parsed)
                ? parsed
                : { status: res.status, detail: body.slice(0, 200) };
        } else {
            problem = {
                status: res.status,
                title: res.statusText || undefined,
                detail: body ? body.slice(0, 200) : undefined,
            };
        }
        throw new ProblemDetailsError(problem, res.statusText);
    }

    let data: unknown;
    if (!hasBody || !body) {
        data = undefined;
    } else if (isJsonContentType(contentType)) {
        data = JSON.parse(body);
    } else {
        data = body;
    }

    return { data, status: res.status, headers: res.headers } as T;
};
