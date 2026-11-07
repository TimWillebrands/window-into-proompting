import http from 'node:http';
import type { Config } from './config.ts';
import type { Fabric } from './fabric.ts';
import { Refused, type Meter, type ProposeRequest, type Runs, type StartRequest } from './runs.ts';

const MaxBody = 256 * 1024;
const MaxWaitMs = 25_000;

async function readJson<T>(req: http.IncomingMessage): Promise<T> {
    let raw = '';
    for await (const chunk of req) {
        raw += chunk;
        if (raw.length > MaxBody) throw new Refused(413, 'too large');
    }
    try {
        return JSON.parse(raw || '{}');
    } catch {
        throw new Refused(400, 'not json');
    }
}

const text = (value: unknown, field: string) => {
    if (typeof value !== 'string' || !value.trim()) throw new Refused(400, `${field} is required`);
    return value;
};

const money = (value: unknown, field: string) => {
    if (typeof value !== 'number' || !Number.isFinite(value) || value < 0) throw new Refused(400, `${field} must be a number`);
    return value;
};

/** The narrow surface the World talks to; it never reaches the agent directly. */
export function createApi(config: Config, runs: Runs, fabric: Fabric, meter: Meter) {
    const route = async (req: http.IncomingMessage, url: URL): Promise<unknown> => {
        const path = url.pathname;
        const post = req.method === 'POST';
        if (req.method === 'GET' && path === '/status') {
            return {
                busy: runs.busy,
                reserveUsd: await meter.balance().catch(() => null),
                models: config.models.map(({ id, label, in: input, out }) => ({ id, label, in: input, out })),
            };
        }
        if (post && path === '/runs') {
            const body = await readJson<Partial<StartRequest>>(req);
            const kind = body.kind === 'survey' ? 'survey' : 'change';
            return runs.view(runs.start({
                commission: text(body.commission, 'commission'), kind, prompt: text(body.prompt, 'prompt'),
                model: text(body.model, 'model'), capUsd: money(body.capUsd, 'capUsd'),
            }));
        }
        if (req.method === 'GET' && path === '/propositions') return runs.listPropositions();

        const proposition = /^\/propositions\/(\d+)(?:\/(patch|reject))?$/.exec(path);
        if (proposition) {
            const n = Number(proposition[1]);
            if (post && proposition[2] === 'reject') {
                const body = await readJson<{ note?: string }>(req);
                return runs.rejectProposition(n, body.note ?? null);
            }
            if (req.method === 'GET' && proposition[2] === 'patch') {
                const found = runs.proposition(n);
                return { raw: await fabric.mailbox(found.baseSha, found.branch) };
            }
            if (req.method === 'GET' && !proposition[2]) return runs.proposition(n);
        }

        const run = /^\/runs\/([a-z0-9]+)(?:\/(events|prompt|propose|drop))?$/.exec(path);
        if (run) {
            const [, id, action] = run;
            if (req.method === 'GET' && !action) return runs.view(runs.get(id));
            if (req.method === 'GET' && action === 'events') {
                const wait = Math.min(Number(url.searchParams.get('wait') ?? 0) * 1000, MaxWaitMs);
                const tail = Number(url.searchParams.get('tail') ?? 500) || 500;
                return runs.readEvents(id, Number(url.searchParams.get('after') ?? 0), wait, tail);
            }
            if (post && action === 'prompt') {
                const body = await readJson<{ text?: string; addUsd?: number }>(req);
                return runs.view(await runs.prompt(id, text(body.text, 'text'), money(body.addUsd ?? 0, 'addUsd')));
            }
            if (post && action === 'propose') {
                const body = await readJson<Partial<ProposeRequest>>(req);
                return runs.view(await runs.propose(id, { title: text(body.title, 'title'), why: text(body.why, 'why'), author: text(body.author, 'author') }));
            }
            if (post && action === 'drop') return runs.view(await runs.drop(id));
        }
        throw new Refused(404, 'nothing here');
    };

    return http.createServer(async (req, res) => {
        const answer = (status: number, body: unknown) => {
            res.writeHead(status, { 'content-type': 'application/json' });
            res.end(JSON.stringify(body));
        };
        if (req.headers.authorization !== `Bearer ${config.token}`) return answer(401, { error: 'no' });
        try {
            const body = await route(req, new URL(req.url ?? '/', 'http://runner'));
            if (typeof body === 'object' && body !== null && 'raw' in body) {
                res.writeHead(200, { 'content-type': 'text/plain; charset=utf-8' });
                res.end(String(body.raw));
                return;
            }
            answer(200, body);
        } catch (error) {
            if (error instanceof Refused) answer(error.status, { error: error.message });
            else answer(500, { error: String(error) });
        }
    });
}
