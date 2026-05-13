/**
 * WebSocket helper for /api/Import/classify-ws.
 *
 * Mirrors {@link personaWsStream} — opens the connection, sends the request as the
 * first message, then yields incoming envelopes (`import.classify.progress`,
 * `import.classify.completed`, `import.classify.error`) until the connection closes.
 *
 * The classifier endpoint runs N chunks in parallel server-side (capped at 5);
 * progress envelopes arrive as each chunk completes, not in the order chunks were
 * sent. The consumer must key results by `chunkId`.
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

export async function* importClassifyWsStream(
    request: { chunks: ImportChunkInput[]; roster: ImportRosterEntry[] },
    signal?: AbortSignal,
): AsyncGenerator<WsEnvelope, void, unknown> {
    const httpUrl = new URL('/api/Import/classify-ws', window.location.href);
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
