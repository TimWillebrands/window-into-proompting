import { appendFileSync, existsSync, readFileSync, writeFileSync } from 'node:fs';

export interface Entry {
    n: number;
    at: number;
    kind: string;
    text: string;
    data?: Record<string, unknown>;
}

/** Numbered entries read by cursor, kept as JSONL so the World's cursor survives a restart. */
export class Feed {
    private entries: Entry[] = [];
    private n = 0;
    private readonly path?: string;
    private readonly keep: number;
    private readonly now: () => number;

    constructor(path?: string, keep = 2000, now: () => number = Date.now) {
        this.path = path;
        this.keep = keep;
        this.now = now;
        if (!path || !existsSync(path)) return;
        const lines = readFileSync(path, 'utf8').split('\n').filter(Boolean);
        this.entries = lines.slice(-keep).flatMap((l) => {
            try {
                return [JSON.parse(l) as Entry];
            } catch {
                return [];
            }
        });
        this.n = this.entries.at(-1)?.n ?? 0;
        writeFileSync(path, this.entries.map((e) => `${JSON.stringify(e)}\n`).join(''));
    }

    get last() {
        return this.n;
    }

    push(kind: string, text: string, data?: Record<string, unknown>): Entry {
        const entry: Entry = { n: ++this.n, at: this.now(), kind, text, ...(data && { data }) };
        this.entries.push(entry);
        if (this.entries.length > this.keep) this.entries.splice(0, this.entries.length - this.keep);
        if (this.path) appendFileSync(this.path, `${JSON.stringify(entry)}\n`);
        return entry;
    }

    after(n: number, limit = 500): Entry[] {
        return this.entries.filter((e) => e.n > n).slice(0, limit);
    }

    tail(count: number): Entry[] {
        return this.entries.slice(-count);
    }
}
