type WsEnvelope = {
    type: string;
    sequence: number;
    timestamp: number;
    data: unknown;
};

/**
 * Opens a WebSocket to /api/Persona/ws, sends `request` as the first message,
 * and yields incoming envelopes until the connection closes.
 */
export async function* personaWsStream(
    request: { type: string; [key: string]: unknown },
    signal?: AbortSignal,
): AsyncGenerator<WsEnvelope, void, unknown> {
    const httpUrl = new URL('/api/Persona/ws', window.location.href);
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
        // Drain any messages that arrived just before close
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
