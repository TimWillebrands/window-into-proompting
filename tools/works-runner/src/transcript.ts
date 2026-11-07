export type LineKind = 'say' | 'thought' | 'tool' | 'failed' | 'status' | 'check';

export interface Line {
    kind: LineKind;
    text: string;
}

const MaxLine = 400;
const clip = (text: string) => (text.length > MaxLine ? `${text.slice(0, MaxLine)}…` : text);

interface ToolUpdate {
    toolCallId?: string;
    title?: string;
    name?: string;
    kind?: string;
    status?: string;
    locations?: { path?: string }[];
    rawInput?: { command?: string; description?: string };
}

const relative = (path: string) => path.replace(/^\/work\//, '');

function describe(update: ToolUpdate) {
    const where = update.locations?.map((l) => relative(l.path ?? '')).find(Boolean);
    const what = update.rawInput?.command ?? where ?? update.rawInput?.description ?? '';
    return `${update.title ?? update.name ?? update.kind ?? 'tool'} ${what}`.trim();
}

/** ACP updates as readable lines: chunks joined, each tool reported once it knows its target. */
export class Transcript {
    private buffer = '';
    private bufferKind: LineKind = 'say';
    private message = '';
    private readonly announced = new Set<string>();

    /** What the agent last said in full, which is its report for the turn. */
    get lastMessage() {
        return this.message.trim();
    }

    /** A new turn: its message replaces the last one. */
    startTurn() {
        this.message = '';
    }

    feed(update: Record<string, unknown>): Line[] {
        const kind = update.sessionUpdate;
        if (kind === 'agent_message_chunk' || kind === 'agent_thought_chunk') {
            const text = (update.content as { text?: string } | undefined)?.text ?? '';
            const lineKind: LineKind = kind === 'agent_message_chunk' ? 'say' : 'thought';
            const lines = lineKind === this.bufferKind ? [] : this.flush();
            this.bufferKind = lineKind;
            if (lineKind === 'say') this.message += text;
            this.buffer += text;
            return lines.concat(this.drain());
        }
        if (kind !== 'tool_call' && kind !== 'tool_call_update') return [];
        const tool = update as ToolUpdate;
        const id = tool.toolCallId ?? '';
        const lines = this.flush();
        if (tool.status === 'failed') lines.push({ kind: 'failed', text: clip(describe(tool)) });
        else if (tool.status === 'in_progress' && !this.announced.has(id)) {
            this.announced.add(id);
            lines.push({ kind: 'tool', text: clip(describe(tool)) });
        }
        return lines;
    }

    private drain(): Line[] {
        const parts = this.buffer.split('\n');
        this.buffer = parts.pop() ?? '';
        return parts.filter((p) => p.trim()).map((p) => ({ kind: this.bufferKind, text: clip(p) }));
    }

    /** Whatever is still mid-line, as a line of its own. */
    flush(): Line[] {
        const text = this.buffer.trim();
        this.buffer = '';
        return text ? [{ kind: this.bufferKind, text: clip(text) }] : [];
    }
}
