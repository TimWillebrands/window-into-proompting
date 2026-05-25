/**
 * WebSocket helpers for /api/Import/classify-ws and /api/Import/identify-ws.
 *
 * Both endpoints follow the same lifecycle: open, send a single JSON request as
 * the first frame, then read a stream of envelopes until the server closes.
 * Envelope shape:  { type, sequence, timestamp, data }.
 *
 * Server runs the heavy LLM calls in parallel (cap=5); envelopes arrive in
 * completion order, NOT request order. Consumers must key by id.
 */
type WsEnvelope = {
    type: string;
    sequence: number;
    timestamp: number;
    data: unknown;
};

export type ImportChunkInput = {
    id: string;
    role: 'user' | 'model';
    text: string;
};

export type ImportRosterEntry = {
    id: string;
    name: string;
    archetype: string | null;
    summary: string;
};

export type ImportIdentifyMsg = {
    id: string;
    role: 'user' | 'model' | 'system';
    text: string;
};

async function* runWsStream(
    pathname: string,
    request: unknown,
    signal?: AbortSignal,
): AsyncGenerator<WsEnvelope, void, unknown> {
    const httpUrl = new URL(pathname, window.location.href);
    const wsUrl = `${httpUrl.protocol === 'https:' ? 'wss:' : 'ws:'}//${httpUrl.host}${httpUrl.pathname}`;

    const ws = new WebSocket(wsUrl);

    const queue: WsEnvelope[] = [];
    let wakeUp: (() => void) | null = null;
    let closed = false;
    let wsError: Error | null = null;

    const wake = () => {
        wakeUp?.();
        wakeUp = null;
    };

    ws.addEventListener('open', () => ws.send(JSON.stringify(request)));
    ws.addEventListener('message', (e) => {
        try {
            queue.push(JSON.parse(e.data as string) as WsEnvelope);
        } catch (err) {
            wsError = err instanceof Error ? err : new Error(String(err));
        } finally {
            wake();
        }
    });
    ws.addEventListener('error', () => {
        wsError = new Error('WebSocket connection failed');
        wake();
    });
    ws.addEventListener('close', () => {
        closed = true;
        wake();
    });

    signal?.addEventListener('abort', () => ws.close());

    try {
        while (!closed && !wsError) {
            while (queue.length > 0) {
                const next = queue.shift();
                if (next) yield next;
            }
            if (closed || wsError) break;
            await new Promise<void>((resolve) => {
                wakeUp = resolve;
            });
        }
        while (queue.length > 0) {
            const next = queue.shift();
            if (next) yield next;
        }
        if (wsError) throw wsError;
    } finally {
        if (
            ws.readyState === WebSocket.OPEN ||
            ws.readyState === WebSocket.CONNECTING
        ) {
            ws.close();
        }
    }
}

export function importClassifyWsStream(
    request: { chunks: ImportChunkInput[]; roster: ImportRosterEntry[] },
    signal?: AbortSignal,
): AsyncGenerator<WsEnvelope, void, unknown> {
    return runWsStream('/api/Import/classify-ws', request, signal);
}

export function importIdentifyWsStream(
    request: { systemInstructionText: string; msgs: ImportIdentifyMsg[] },
    signal?: AbortSignal,
): AsyncGenerator<WsEnvelope, void, unknown> {
    return runWsStream('/api/Import/identify-ws', request, signal);
}
