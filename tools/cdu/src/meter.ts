import { readFileSync, renameSync, writeFileSync } from 'node:fs';
import http from 'node:http';

export interface Model {
    id: string;
    label: string;
    in: number;
    out: number;
    context: number;
    output: number;
}

export interface Tab {
    token: string;
    run: string;
    capUsd: number;
    capTokens: number;
    usd: number;
    tokens: number;
    calls: number;
    daily?: boolean;
    day?: string;
}

interface Usage {
    prompt_tokens?: number;
    completion_tokens?: number;
    total_tokens?: number;
    cost?: number;
    opencode?: { allowance?: number; list_cost?: number };
}

export interface MeterOptions {
    upstream: string;
    key: string;
    secret: string;
    models: Model[];
    balanceUrl?: string;
    tabs?: Tab[];
    statePath?: string;
    now?: () => number;
}

const MaxBody = 32 * 1024 * 1024;

export const overCap = (tab: Tab) => tab.usd >= tab.capUsd || tab.tokens >= tab.capTokens;

export const dayOf = (ms: number) => new Date(ms).toISOString().slice(0, 10);

export function roll(tab: Tab, day: string) {
    if (!tab.daily || tab.day === day) return;
    Object.assign(tab, { usd: 0, tokens: 0, calls: 0, day });
}

export function restore(declared: Tab[], kept: Tab[]): Map<string, Tab> {
    const tabs = new Map(kept.filter((t) => !t.daily).map((t) => [t.token, t]));
    const before = new Map(kept.map((t) => [t.token, t]));
    for (const tab of declared) {
        const was = before.get(tab.token);
        tabs.set(tab.token, { ...tab, ...(was && { usd: was.usd, tokens: was.tokens, calls: was.calls, day: was.day }) });
    }
    return tabs;
}

export function costOf(usage: Usage, model: Model): number {
    const opencode = usage.opencode?.allowance ?? usage.opencode?.list_cost;
    if (typeof opencode === 'number') return opencode;
    if (typeof usage.cost === 'number') return usage.cost;
    return ((usage.prompt_tokens ?? 0) * model.in + (usage.completion_tokens ?? 0) * model.out) / 1_000_000;
}

export function outputCap(tab: Tab, model: Model): number {
    const byTokens = tab.capTokens - tab.tokens;
    const byMoney = model.out > 0 ? ((tab.capUsd - tab.usd) * 1_000_000) / model.out : Number.POSITIVE_INFINITY;
    return Math.max(1, Math.floor(Math.min(byTokens, byMoney, model.output)));
}

export function tally(tab: Tab, usage: Usage, model: Model) {
    tab.usd += costOf(usage, model);
    tab.tokens += usage.total_tokens ?? (usage.prompt_tokens ?? 0) + (usage.completion_tokens ?? 0);
    tab.calls += 1;
}

export function usageIn(line: string): Usage | null {
    if (!line.startsWith('data:') || !line.includes('"usage"')) return null;
    try {
        return JSON.parse(line.slice(5)).usage ?? null;
    } catch {
        return null;
    }
}

const Passed = ['x-opencode-session'];

const passed = (req: http.IncomingMessage) =>
    Object.fromEntries(Passed.flatMap((h) => (typeof req.headers[h] === 'string' ? [[h, req.headers[h] as string]] : [])));

function load(path?: string): Tab[] {
    if (!path) return [];
    try {
        return JSON.parse(readFileSync(path, 'utf8'));
    } catch {
        return [];
    }
}

const send = (res: http.ServerResponse, status: number, body: unknown) => {
    res.writeHead(status, { 'content-type': 'application/json' });
    res.end(JSON.stringify(body));
};

const refuse = (res: http.ServerResponse, status: number, message: string) => send(res, status, { error: { message } });

async function readBody(req: http.IncomingMessage): Promise<Buffer | null> {
    const chunks: Buffer[] = [];
    let size = 0;
    for await (const chunk of req) {
        size += chunk.length;
        if (size > MaxBody) return null;
        chunks.push(chunk);
    }
    return Buffer.concat(chunks);
}

export function createMeter(options: MeterOptions) {
    const now = options.now ?? Date.now;
    const tabs = restore(options.tabs ?? [], load(options.statePath));
    const models = new Map(options.models.map((m) => [m.id, m]));
    const save = () => {
        if (!options.statePath) return;
        writeFileSync(`${options.statePath}.tmp`, JSON.stringify([...tabs.values()]));
        renameSync(`${options.statePath}.tmp`, options.statePath);
    };
    const tabFor = (req: http.IncomingMessage) => {
        const tab = tabs.get((req.headers.authorization ?? '').replace(/^Bearer /, ''));
        if (tab) roll(tab, dayOf(now()));
        return tab;
    };

    const forward = async (req: http.IncomingMessage, res: http.ServerResponse) => {
        const tab = tabFor(req);
        if (!tab) return refuse(res, 401, 'unknown run');
        if (overCap(tab)) return refuse(res, 402, tab.daily ? 'allowance spent for today' : 'budget spent');
        const raw = await readBody(req);
        if (!raw) return refuse(res, 413, 'too large');
        let body: Record<string, unknown>;
        try {
            body = JSON.parse(raw.toString());
        } catch {
            return refuse(res, 400, 'not json');
        }
        const model = models.get(String(body.model));
        if (!model) return refuse(res, 403, `model not allowed: ${body.model}`);
        if (body.stream) body.stream_options = { include_usage: true };
        if (options.upstream.includes('openrouter.ai')) body.usage = { include: true };
        const cap = outputCap(tab, model);
        for (const field of ['max_tokens', 'max_completion_tokens']) {
            if (typeof body[field] === 'number') body[field] = Math.min(body[field] as number, cap);
        }
        if (body.max_tokens === undefined && body.max_completion_tokens === undefined) body.max_tokens = cap;

        const upstream = await fetch(`${options.upstream}/chat/completions`, {
            method: 'POST',
            headers: { ...passed(req), 'content-type': 'application/json', authorization: `Bearer ${options.key}` },
            body: JSON.stringify(body),
        });
        res.writeHead(upstream.status, { 'content-type': upstream.headers.get('content-type') ?? 'application/json' });
        if (!upstream.body) return res.end();

        const decoder = new TextDecoder();
        let tail = '';
        let usage: Usage | null = null;
        for await (const chunk of upstream.body) {
            res.write(chunk);
            const lines = (tail + decoder.decode(chunk, { stream: true })).split('\n');
            tail = lines.pop() ?? '';
            for (const line of lines) usage = usageIn(line) ?? usage;
        }
        if (!usage && tail) {
            try {
                usage = JSON.parse(tail).usage ?? null;
            } catch {
                usage = null;
            }
        }
        res.end();
        if (usage) {
            tally(tab, usage, model);
            save();
        }
    };

    const proxy = http.createServer((req, res) => {
        if (req.method === 'GET' && (req.url === '/v1/tab' || req.url === '/v1/models')) {
            const tab = tabFor(req);
            if (!tab) return refuse(res, 401, 'unknown run');
            return send(res, 200, req.url === '/v1/tab' ? { ...tab, token: undefined } : { data: options.models });
        }
        if (req.method !== 'POST' || req.url !== '/v1/chat/completions') return refuse(res, 404, 'nothing else here');
        forward(req, res).catch((error) => {
            if (res.headersSent) res.destroy();
            else refuse(res, 502, String(error));
        });
    });

    const balance = async (): Promise<number | null> => {
        if (!options.balanceUrl) return null;
        const answer = await fetch(options.balanceUrl, { headers: { authorization: `Bearer ${options.key}` } });
        if (!answer.ok) return null;
        const left = ((await answer.json()) as { data?: { limit_remaining?: number } }).data?.limit_remaining;
        return typeof left === 'number' ? left : null;
    };

    const admin = http.createServer(async (req, res) => {
        if (req.headers['x-meter-secret'] !== options.secret) return refuse(res, 401, 'no');
        const [, what, token, action] = (req.url ?? '').split('/');
        if (what === 'balance') return send(res, 200, { usd: await balance().catch(() => null) });
        if (what !== 'tabs') return refuse(res, 404, 'no');
        if (req.method === 'POST' && action === 'raise') {
            const raw = await readBody(req);
            const raised = tabs.get(token);
            if (!raised) return refuse(res, 404, 'no tab');
            raised.capUsd += Number(JSON.parse(raw?.toString() ?? '{}').addUsd ?? 0);
            save();
            return send(res, 200, raised);
        }
        if (req.method === 'POST') {
            const raw = await readBody(req);
            const { token: id, run, capUsd, capTokens, usd, tokens } = JSON.parse(raw?.toString() ?? '{}');
            const tab: Tab = { token: id, run, capUsd, capTokens, usd: Number(usd ?? 0), tokens: Number(tokens ?? 0), calls: 0 };
            tabs.set(id, tabs.get(id) ?? tab);
            save();
            return send(res, 200, tabs.get(id));
        }
        const tab = tabs.get(token);
        if (!tab) return refuse(res, 404, 'no tab');
        roll(tab, dayOf(now()));
        if (req.method === 'DELETE') {
            tabs.delete(token);
            save();
        }
        return send(res, 200, tab);
    });

    return { proxy, admin, tabs };
}
