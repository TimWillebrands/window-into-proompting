import readline from 'node:readline';
import type { Readable, Writable } from 'node:stream';

export interface PromptResult {
    stopReason: string;
    usage?: { inputTokens?: number; outputTokens?: number; totalTokens?: number };
}

interface Rpc {
    id?: number | string;
    method?: string;
    params?: Record<string, unknown>;
    result?: unknown;
    error?: { message?: string };
}

/** ACP client over a stdio pair, newline-delimited JSON-RPC. Only ever spans one host. */
export class Acp {
    private next = 0;
    private readonly waiting = new Map<number, { resolve: (v: unknown) => void; reject: (e: Error) => void }>();
    private readonly input: Writable;
    private readonly onUpdate: (update: Record<string, unknown>) => void;
    /** Settles when the agent's stdout ends; a dropped pipe ends the agent with it. */
    readonly closed: Promise<void>;

    constructor(input: Writable, output: Readable, onUpdate: (update: Record<string, unknown>) => void) {
        this.input = input;
        this.onUpdate = onUpdate;
        const lines = readline.createInterface({ input: output });
        lines.on('line', (line) => this.receive(line));
        this.closed = new Promise((resolve) => {
            lines.on('close', () => {
                for (const { reject } of this.waiting.values()) reject(new Error('agent exited'));
                this.waiting.clear();
                resolve();
            });
        });
    }

    private write(message: unknown) {
        this.input.write(`${JSON.stringify(message)}\n`);
    }

    private request<T>(method: string, params: unknown): Promise<T> {
        const id = ++this.next;
        return new Promise<T>((resolve, reject) => {
            this.waiting.set(id, { resolve: resolve as (v: unknown) => void, reject });
            this.write({ jsonrpc: '2.0', id, method, params });
        });
    }

    private receive(line: string) {
        let message: Rpc;
        try {
            message = JSON.parse(line);
        } catch {
            return;
        }
        if (message.method === 'session/update') {
            this.onUpdate(message.params?.update as Record<string, unknown>);
            return;
        }
        if (message.method) {
            this.answer(message);
            return;
        }
        const waiter = this.waiting.get(Number(message.id));
        if (!waiter) return;
        this.waiting.delete(Number(message.id));
        if (message.error) waiter.reject(new Error(message.error.message ?? 'acp error'));
        else waiter.resolve(message.result);
    }

    /** Nobody is at the keyboard: the sandbox is the boundary, so anything asked is allowed. */
    private answer(message: Rpc) {
        if (message.id === undefined) return;
        if (message.method === 'session/request_permission') {
            const options = (message.params?.options ?? []) as { optionId: string; kind: string }[];
            const pick = options.find((o) => o.kind.startsWith('allow')) ?? options[0];
            this.write({ jsonrpc: '2.0', id: message.id, result: { outcome: { outcome: 'selected', optionId: pick?.optionId } } });
            return;
        }
        this.write({ jsonrpc: '2.0', id: message.id, error: { code: -32601, message: 'not offered' } });
    }

    initialize() {
        return this.request('initialize', {
            protocolVersion: 1,
            clientCapabilities: { fs: { readTextFile: false, writeTextFile: false }, terminal: false },
        });
    }

    async newSession(cwd: string) {
        return (await this.request<{ sessionId: string }>('session/new', { cwd, mcpServers: [] })).sessionId;
    }

    async loadSession(sessionId: string, cwd: string) {
        await this.request('session/load', { sessionId, cwd, mcpServers: [] });
    }

    prompt(sessionId: string, text: string) {
        return this.request<PromptResult>('session/prompt', { sessionId, prompt: [{ type: 'text', text }] });
    }

    cancel(sessionId: string) {
        this.write({ jsonrpc: '2.0', method: 'session/cancel', params: { sessionId } });
    }
}
