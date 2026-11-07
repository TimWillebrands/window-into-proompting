export type LineKind = 'heard' | 'say' | 'thought' | 'tool' | 'failed' | 'status';

export interface Line {
    kind: LineKind;
    text: string;
}

const MaxLine = 400;
const clip = (text: string) => (text.length > MaxLine ? `${text.slice(0, MaxLine)}…` : text);

type Args = Record<string, unknown> | undefined;

/** What a tool call was about, in one line: the command, the path, or its arguments. */
export function describe(tool: string, args: Args, root = ''): string {
    const strip = (p: string) => (root && p.startsWith(`${root}/`) ? p.slice(root.length + 1) : p);
    const target = args?.command ?? args?.path ?? args?.file_path ?? args?.pattern;
    if (typeof target === 'string') return `${tool} ${strip(target)}`.trim();
    return args && Object.keys(args).length > 0 ? `${tool} ${JSON.stringify(args)}` : tool;
}

/** Pi session events as readable lines: deltas joined, each tool once, failures and retries said. */
export class Transcript {
    private buffer = '';
    private bufferKind: LineKind = 'say';
    private thinking = false;
    private readonly root: string;

    constructor(root = '') {
        this.root = root;
    }

    feed(event: Record<string, unknown>): Line[] {
        switch (event.type) {
            case 'message_update': {
                const delta = event.assistantMessageEvent as { type?: string; delta?: string } | undefined;
                if (delta?.type !== 'text_delta' && delta?.type !== 'thinking_delta') return [];
                const kind: LineKind = delta.type === 'text_delta' ? 'say' : 'thought';
                const lines = kind === this.bufferKind ? [] : this.flush();
                this.bufferKind = kind;
                this.buffer += delta.delta ?? '';
                return lines.concat(this.drain());
            }
            case 'message_end':
                return this.flush();
            case 'tool_execution_start':
                return [...this.flush(), { kind: 'tool', text: clip(describe(String(event.toolName), event.args as Args, this.root)) }];
            case 'tool_execution_end':
                return event.isError ? [{ kind: 'failed', text: clip(`${event.toolName} failed`) }] : [];
            case 'auto_retry_start':
                return [{ kind: 'status', text: clip(`trying again: ${event.errorMessage ?? 'the model failed'}`) }];
            case 'compaction_start':
                return [{ kind: 'status', text: 'folding older memory away' }];
            default:
                return [];
        }
    }

    private drain(): Line[] {
        const parts = this.buffer.split('\n');
        this.buffer = parts.pop() ?? '';
        return parts.flatMap((p) => this.line(p));
    }

    /** Some models think inline, between think tags: those lines are thoughts, not speech. */
    private line(raw: string): Line[] {
        let text = raw;
        let kind = this.thinking ? 'thought' : this.bufferKind;
        if (text.includes('<think>')) {
            this.thinking = true;
            kind = 'thought';
            text = text.replace('<think>', '');
        }
        if (text.includes('</think>')) {
            this.thinking = false;
            text = text.replace('</think>', '');
        }
        return text.trim() ? [{ kind: kind as LineKind, text: clip(text.trim()) }] : [];
    }

    /** Whatever is still mid-line, as a line of its own. */
    flush(): Line[] {
        const text = this.buffer;
        this.buffer = '';
        return this.line(text);
    }
}

/** What it said, without any inline thinking a model left in it. */
export const spoken = (reply: string) => reply.replace(/<think>[\s\S]*?(<\/think>|$)/g, '').trim();
