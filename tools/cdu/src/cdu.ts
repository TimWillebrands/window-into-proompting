import { randomUUID } from 'node:crypto';
import { existsSync, readFileSync, renameSync, writeFileSync } from 'node:fs';
import type { Book } from './book.ts';
import type { Feed } from './feed.ts';
import type { Checks, Commit, Pr, Tide } from './tide.ts';
import { spoken, Transcript } from './transcript.ts';

export interface Speaker {
    id: string;
    name: string;
}

export type TurnKind = 'talk' | 'answer';

export interface Turn {
    id: string;
    from: Speaker;
    text: string;
    kind: TurnKind;
    status: 'queued' | 'working' | 'done' | 'failed';
    at: number;
    doneAt?: number;
    /** What it said back. Its words, not a fact. */
    reply?: string;
    /** Its reply ends on a question for whoever spoke. */
    asked?: boolean;
    commits: Commit[];
    /** Spent on this turn, from the Meter. */
    usd?: number;
    error?: string;
}

/** The agent behind the CDU: Pi in production, a fake in tests. */
export interface Mind {
    readonly model: string;
    think(text: string, onEvent: (event: Record<string, unknown>) => void): Promise<string>;
    /** Master's soul changed; it applies from the next waking. */
    resoul?(soul: string): Promise<void>;
}

export interface Allowance {
    usd: number;
    capUsd: number;
}

export interface CduState {
    turns: Turn[];
    /** Facts for its next waking. */
    notes: string[];
    pr: number | null;
    /** The PR as last looked at, so status doesn't ask GitHub. */
    seen: Pr | null;
    /** The last checks reported, per head commit. */
    checked: { head: string; checks: Checks } | null;
    soul: string | null;
}

export const emptyState = (): CduState => ({ turns: [], notes: [], pr: null, seen: null, checked: null, soul: null });

/** A JSON file holding what must survive a restart. */
export class Store {
    readonly state: CduState;
    private readonly path?: string;

    constructor(path?: string) {
        this.path = path;
        this.state = path && existsSync(path) ? { ...emptyState(), ...JSON.parse(readFileSync(path, 'utf8')) } : emptyState();
    }

    save() {
        if (!this.path) return;
        writeFileSync(`${this.path}.tmp`, JSON.stringify(this.state));
        renameSync(`${this.path}.tmp`, this.path);
    }
}

const KeptTurns = 50;
const MaxText = 4000;

const clip = (text: string, max: number) => (text.length > max ? `${text.slice(0, max)}…` : text);

/** Its reply asks something when the last paragraph holds a question. */
export function asks(reply: string): boolean {
    const last = reply.trim().split(/\n\s*\n/).pop() ?? '';
    return last.includes('?');
}

export const emailOf = (who: Speaker) => `${who.name.toLowerCase().replace(/[^a-z0-9]+/g, '.').replace(/^\.|\.$/g, '') || 'someone'}@cytheurgists.proompting.party`;

/** What the CDU hears when it wakes: who speaks, what they say, and what happened while it slept. */
export function compose(turn: Turn, notes: string[]): string {
    const said = `${turn.from.name}, a Cytheurgist, ${turn.kind === 'answer' ? 'answers you' : 'speaks to you'}:\n\n${turn.text}`;
    return notes.length ? `${said}\n\nWhile you slept:\n${notes.map((n) => `- ${n}`).join('\n')}` : said;
}

export interface CduOptions {
    mind: Mind;
    tide: Tide;
    book: Book;
    lines: Feed;
    news: Feed;
    store: Store;
    allowance?: () => Promise<Allowance | null>;
    /** Where the soul lives in the repo. */
    soulPath?: string;
    now?: () => number;
}

/** One being, one queue: turns and the tide take their turn, so git is never touched twice at once. */
export class Cdu {
    readonly lines: Feed;
    readonly news: Feed;
    private readonly mind: Mind;
    private readonly tide: Tide;
    private readonly book: Book;
    private readonly store: Store;
    private readonly allowance: () => Promise<Allowance | null>;
    private readonly soulPath: string;
    private readonly now: () => number;
    private chain: Promise<unknown> = Promise.resolve();
    private waiting = 0;
    private current: Turn | null = null;
    private watching = false;

    constructor(options: CduOptions) {
        this.mind = options.mind;
        this.tide = options.tide;
        this.book = options.book;
        this.lines = options.lines;
        this.news = options.news;
        this.store = options.store;
        this.allowance = options.allowance ?? (async () => null);
        this.soulPath = options.soulPath ?? 'tools/cdu/soul/APPEND_SYSTEM.md';
        this.now = options.now ?? Date.now;
        // A turn cut off by a crash or restart is over: it won't finish now.
        for (const turn of this.state.turns.filter((t) => t.status === 'queued' || t.status === 'working')) {
            Object.assign(turn, { status: 'failed', error: 'the CDU restarted before it finished' });
        }
    }

    private get state() {
        return this.store.state;
    }

    private enqueue<T>(job: () => Promise<T>): Promise<T> {
        this.waiting++;
        const next = this.chain.then(job).finally(() => this.waiting--);
        this.chain = next.catch(() => {});
        return next;
    }

    /** Resolves once everything queued so far is done. */
    idle(): Promise<void> {
        return this.chain.then(() => {});
    }

    talk(from: Speaker, text: string, kind: TurnKind = 'talk'): Turn {
        const turn: Turn = { id: randomUUID(), from, text: clip(text.trim(), MaxText), kind, status: 'queued', at: this.now(), commits: [] };
        this.state.turns.push(turn);
        if (this.state.turns.length > KeptTurns) this.state.turns.splice(0, this.state.turns.length - KeptTurns);
        this.store.save();
        this.enqueue(() => this.run(turn)).catch(() => {});
        return turn;
    }

    turn(id: string): Turn | undefined {
        return this.state.turns.find((t) => t.id === id);
    }

    private async run(turn: Turn) {
        turn.status = 'working';
        this.current = turn;
        this.store.save();
        this.lines.push('heard', `${turn.from.name}: ${clip(turn.text, 400)}`, { turn: turn.id });
        const notes = this.state.notes.splice(0);
        const before = await this.tide.head().catch(() => null);
        const spent = await this.allowance().catch(() => null);
        const transcript = new Transcript(this.tide.repo);
        const line = (l: { kind: string; text: string }) => this.lines.push(l.kind, l.text, { turn: turn.id });
        try {
            await this.tide.author(turn.from.name, emailOf(turn.from));
            const reply = await this.mind.think(compose(turn, notes), (event) => transcript.feed(event).forEach(line));
            transcript.flush().forEach(line);
            turn.reply = spoken(reply);
            turn.asked = asks(turn.reply);
            turn.status = 'done';
        } catch (error) {
            transcript.flush().forEach(line);
            this.state.notes.unshift(...notes);
            turn.status = 'failed';
            turn.error = String(error instanceof Error ? error.message : error).slice(0, 600);
            this.lines.push('failed', `fell over: ${turn.error}`, { turn: turn.id });
        }
        if (before) turn.commits = await this.tide.since(before).catch(() => []);
        const after = await this.allowance().catch(() => null);
        if (spent && after) turn.usd = Math.max(0, after.usd - spent.usd);
        await this.book.keep(`after ${turn.from.name}: ${clip(turn.text.replace(/\s+/g, ' '), 60)}`).catch(() => false);
        turn.doneAt = this.now();
        this.current = null;
        this.store.save();
        this.news.push(turn.status === 'done' ? 'replied' : 'failed', turn.reply ?? turn.error ?? '', {
            turn: turn.id, to: turn.from, asked: turn.asked ?? false, commits: turn.commits.length, usd: turn.usd ?? null,
        });
    }

    /** Shift start: any verdict first, then the trunk onto master, then master's soul. */
    tideIn() {
        return this.enqueue(async () => {
            await this.settle();
            const result = await this.tide.in();
            if (result.kind === 'conflict') {
                this.state.notes.push(`The trunk didn't rebase cleanly on master at the start of the shift, so it's as you left it. Git said: ${result.detail}`);
                this.news.push('conflict', result.detail);
            } else {
                this.news.push('rebased', `${result.ahead} commits ahead of master`, { ahead: result.ahead });
            }
            await this.resoul();
            this.store.save();
            return result;
        });
    }

    /** Shift end: the trunk out to the Agora, when it holds anything. */
    tideOut() {
        return this.enqueue(async () => {
            const result = await this.tide.out();
            if (result.kind !== 'calm') {
                this.state.seen = result.pr;
                this.state.notes.push(`Your trunk went out with the tide: PR #${result.pr.number}, ${result.commits.length} commits.`);
                this.news.push('pr', result.pr.title, { number: result.pr.number, url: result.pr.url, opened: result.kind === 'opened', commits: result.commits.length });
                this.store.save();
            }
            return result;
        });
    }

    /** Looks at the Agora: a verdict resets the trunk; checks are told once per head commit. */
    watch() {
        if (this.watching) return Promise.resolve();
        this.watching = true;
        return this.enqueue(async () => {
            if (await this.settle()) return;
            const pr = await this.tide.look();
            this.state.seen = pr;
            const was = this.state.checked;
            if (pr && (pr.checks === 'passing' || pr.checks === 'failing') && (was?.head !== pr.head || was.checks !== pr.checks)) {
                this.state.checked = { head: pr.head, checks: pr.checks };
                if (pr.checks === 'failing') this.state.notes.push(`Checks are failing on PR #${pr.number}.`);
                this.news.push('checks', pr.checks, { number: pr.number, head: pr.head, url: pr.url });
            }
            this.store.save();
        }).finally(() => {
            this.watching = false;
        });
    }

    private async settle(): Promise<boolean> {
        const verdict = await this.tide.verdict();
        if (!verdict) return false;
        const { pr } = verdict;
        const fact = verdict.kind === 'merged'
            ? `The One merged PR #${pr.number} "${pr.title}". The trunk is master again.`
            : `The One closed PR #${pr.number} "${pr.title}" without merging. It's kept as ${verdict.tag}; the trunk is master again.`;
        await this.book.journal(fact).catch(() => {});
        this.state.notes.push(fact);
        this.state.seen = null;
        this.state.checked = null;
        this.news.push(verdict.kind, pr.title, { number: pr.number, url: pr.url, tag: verdict.tag ?? null });
        this.store.save();
        return true;
    }

    private async resoul() {
        const soul = await this.tide.show(this.soulPath);
        if (!soul || soul === this.state.soul) return;
        const first = this.state.soul === null;
        this.state.soul = soul;
        await this.mind.resoul?.(soul);
        if (!first) this.lines.push('status', 'its soul changed with the light from master');
    }

    async status() {
        const ahead = await this.tide.ahead().catch(() => null);
        const last = [...this.state.turns].reverse().find((t) => t.status === 'done' || t.status === 'failed');
        return {
            awake: this.current !== null,
            hearing: this.current ? this.current.from.name : null,
            queued: Math.max(0, this.waiting - (this.current ? 1 : 0)),
            model: this.mind.model,
            trunk: ahead && { head: await this.tide.head().catch(() => null), ahead },
            pr: this.state.seen,
            allowance: await this.allowance().catch(() => null),
            lines: this.lines.last,
            news: this.news.last,
            last: last ?? null,
        };
    }
}
