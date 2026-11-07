import http from 'node:http';
import type { Cdu, TurnKind } from './cdu.ts';

const MaxBody = 64 * 1024;

const send = (res: http.ServerResponse, status: number, body: unknown) => {
    res.writeHead(status, { 'content-type': 'application/json' });
    res.end(JSON.stringify(body));
};

async function readJson(req: http.IncomingMessage): Promise<Record<string, unknown> | null> {
    let raw = '';
    for await (const chunk of req) {
        raw += chunk;
        if (raw.length > MaxBody) return null;
    }
    try {
        return raw ? JSON.parse(raw) : {};
    } catch {
        return null;
    }
}

/** The World's one door to the CDU. Tide calls return at once; what came of them is news. */
export function createApi(cdu: Cdu, token: string) {
    return http.createServer(async (req, res) => {
        try {
            if (req.headers.authorization !== `Bearer ${token}`) return send(res, 401, { error: 'who are you' });
            const url = new URL(req.url ?? '/', 'http://cdu');
            const after = Number(url.searchParams.get('after') ?? 0);
            const route = `${req.method} ${url.pathname}`;
            if (route === 'GET /status') return send(res, 200, await cdu.status());
            if (route === 'GET /lines') return send(res, 200, cdu.lines.after(after));
            if (route === 'GET /news') return send(res, 200, cdu.news.after(after));
            if (route === 'POST /talk') {
                const body = await readJson(req);
                const from = body?.from as { id?: unknown; name?: unknown } | undefined;
                const text = typeof body?.text === 'string' ? body.text.trim() : '';
                if (!from || typeof from.id !== 'string' || typeof from.name !== 'string' || !text) return send(res, 400, { error: 'from {id, name} and text' });
                const kind: TurnKind = body?.kind === 'answer' ? 'answer' : 'talk';
                return send(res, 202, cdu.talk({ id: from.id, name: from.name }, text, kind));
            }
            if (req.method === 'GET' && url.pathname.startsWith('/turns/')) {
                const turn = cdu.turn(url.pathname.slice('/turns/'.length));
                return turn ? send(res, 200, turn) : send(res, 404, { error: 'no such turn' });
            }
            if (route === 'POST /tide/in' || route === 'POST /tide/out') {
                const job = route.endsWith('in') ? cdu.tideIn() : cdu.tideOut();
                job.catch((error) => cdu.news.push('tide-failed', String(error instanceof Error ? error.message : error).slice(0, 600)));
                return send(res, 202, { queued: true });
            }
            return send(res, 404, { error: 'nothing here' });
        } catch (error) {
            if (!res.headersSent) send(res, 500, { error: String(error) });
        }
    });
}
