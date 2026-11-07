import { existsSync } from 'node:fs';
import path from 'node:path';
import { must, run } from './exec.ts';

export type Checks = 'none' | 'pending' | 'passing' | 'failing';

export interface Pr {
    number: number;
    url: string;
    title: string;
    state: 'OPEN' | 'MERGED' | 'CLOSED';
    /** The head commit the checks are for. */
    head: string;
    checks: Checks;
}

/** The Agora, as far as the tide needs it. */
export interface GitHub {
    view(number: number): Promise<Pr>;
    create(head: string, base: string, title: string, body: string): Promise<Pr>;
    body(number: number): Promise<string>;
    edit(number: number, body: string): Promise<void>;
}

export interface Commit {
    sha: string;
    subject: string;
    author: string;
}

export type TideIn = { kind: 'rebased'; ahead: number } | { kind: 'conflict'; detail: string };
export type TideOut = { kind: 'calm' } | { kind: 'opened' | 'updated'; pr: Pr; commits: Commit[] };
export interface Verdict {
    kind: 'merged' | 'closed';
    pr: Pr;
    /** Where closed work was kept. */
    tag?: string;
}

export interface TideOptions {
    repo: string;
    origin: string;
    github: GitHub;
    base?: string;
    branch?: string;
    /** The PR this tide is out as, from the last run. */
    pr?: number | null;
    onPr?: (pr: number | null) => void;
    now?: () => number;
}

const Trunk = 'trunk';
const Open = '<!-- cdu:commits -->';
const Close = '<!-- /cdu:commits -->';

export const prTitle = (commits: Commit[]) =>
    commits.length === 1 ? commits[0].subject : `${commits[0].subject} (+${commits.length - 1} more)`;

/** The commit list is the service's; anything the CDU wrote around it stays. */
export function prBody(commits: Commit[], before = ''): string {
    const list = `${Open}\n${commits.map((c) => `- ${c.sha.slice(0, 7)} ${c.subject} (asked by ${c.author})`).join('\n')}\n${Close}`;
    const start = before.indexOf(Open);
    const end = before.indexOf(Close);
    if (start >= 0 && end > start) return before.slice(0, start) + list + before.slice(end + Close.length);
    return `${list}\n\nThe CDU's trunk, out with the tide at the end of a shift. Each commit is authored by the Cytheurgist who asked for it.`;
}

/** The PR's checks in one word; anything still running is pending, any failure is failing. */
export function checksOf(rollup: Array<Record<string, unknown>> | undefined): Checks {
    if (!rollup || rollup.length === 0) return 'none';
    const bad = new Set(['FAILURE', 'TIMED_OUT', 'CANCELLED', 'ACTION_REQUIRED', 'STARTUP_FAILURE', 'ERROR']);
    if (rollup.some((c) => bad.has(String(c.conclusion ?? c.state ?? '')))) return 'failing';
    if (rollup.some((c) => (c.status !== undefined ? c.status !== 'COMPLETED' : ['PENDING', 'EXPECTED'].includes(String(c.state))))) return 'pending';
    return 'passing';
}

/** The trunk and how it meets master: in at shift start, out as the one PR at shift end, reset on any verdict. */
export class Tide {
    readonly repo: string;
    readonly base: string;
    readonly branch: string;
    private readonly origin: string;
    private readonly github: GitHub;
    private readonly onPr: (pr: number | null) => void;
    private readonly now: () => number;
    private openPr: number | null;

    constructor(options: TideOptions) {
        this.repo = options.repo;
        this.origin = options.origin;
        this.github = options.github;
        this.base = options.base ?? 'master';
        this.branch = options.branch ?? 'cdu/tide';
        this.openPr = options.pr ?? null;
        this.onPr = options.onPr ?? (() => {});
        this.now = options.now ?? Date.now;
    }

    get pr() {
        return this.openPr;
    }

    private git(...args: string[]) {
        return must('git', ['-C', this.repo, ...args]);
    }

    private set(pr: number | null) {
        this.openPr = pr;
        this.onPr(pr);
    }

    /** A clone with a trunk, made on first start and kept on the volume after. */
    async ensure() {
        if (!existsSync(path.join(this.repo, '.git'))) await must('git', ['clone', this.origin, this.repo]);
        await this.git('fetch', 'origin');
        const has = await run('git', ['-C', this.repo, 'rev-parse', '--verify', '--quiet', `refs/heads/${Trunk}`]);
        if (has.code === 0) await this.git('checkout', Trunk);
        else await this.git('checkout', '-B', Trunk, `origin/${this.base}`);
    }

    /** Who the next commits are by; the committer stays the CDU. */
    async author(name: string, email: string) {
        await this.git('config', 'user.name', name);
        await this.git('config', 'user.email', email);
    }

    head() {
        return this.git('rev-parse', Trunk);
    }

    private async log(range: string): Promise<Commit[]> {
        const out = await this.git('log', '--reverse', '--format=%H%x1f%s%x1f%an', range);
        return out ? out.split('\n').map((l) => {
            const [sha, subject, author] = l.split('\x1f');
            return { sha, subject, author };
        }) : [];
    }

    /** What the trunk holds that master doesn't. */
    ahead() {
        return this.log(`origin/${this.base}..${Trunk}`);
    }

    since(sha: string) {
        return this.log(`${sha}..${Trunk}`);
    }

    /** A file as master has it: what the One merged, not what the trunk holds. */
    async show(file: string): Promise<string | null> {
        const ran = await run('git', ['-C', this.repo, 'show', `origin/${this.base}:${file}`]);
        return ran.code === 0 ? ran.stdout : null;
    }

    /** Shift start: the trunk rebased on master. A conflict is left for the CDU, its trunk untouched. */
    async in(): Promise<TideIn> {
        await this.git('fetch', 'origin');
        await this.git('checkout', Trunk);
        const ran = await run('git', ['-C', this.repo, 'rebase', '--autostash', `origin/${this.base}`]);
        if (ran.code !== 0) {
            await run('git', ['-C', this.repo, 'rebase', '--abort']);
            return { kind: 'conflict', detail: (ran.stderr || ran.stdout).trim().slice(-600) };
        }
        return { kind: 'rebased', ahead: (await this.ahead()).length };
    }

    /** Shift end: the trunk out as the one PR, opened or brought up to date. */
    async out(): Promise<TideOut> {
        const commits = await this.ahead();
        if (commits.length === 0) return { kind: 'calm' };
        await this.git('push', '--force', 'origin', `${Trunk}:refs/heads/${this.branch}`);
        const open = this.openPr ? await this.github.view(this.openPr) : null;
        if (open?.state === 'OPEN') {
            await this.github.edit(open.number, prBody(commits, await this.github.body(open.number)));
            return { kind: 'updated', pr: await this.github.view(open.number), commits };
        }
        const pr = await this.github.create(this.branch, this.base, prTitle(commits), prBody(commits));
        this.set(pr.number);
        return { kind: 'opened', pr, commits };
    }

    /** The open PR as it stands, or null when the tide is in. */
    look(): Promise<Pr | null> {
        return this.openPr ? this.github.view(this.openPr) : Promise.resolve(null);
    }

    /** Any verdict on the open PR resets the trunk to master; closed work is kept as a tag first. */
    async verdict(): Promise<Verdict | null> {
        const pr = await this.look();
        if (!pr || pr.state === 'OPEN') return null;
        await this.git('fetch', 'origin');
        let tag: string | undefined;
        if (pr.state === 'CLOSED') {
            tag = `drowned/${new Date(this.now()).toISOString().slice(0, 10)}-pr${pr.number}`;
            await this.git('tag', '-f', tag, Trunk);
        }
        await this.reset();
        this.set(null);
        return { kind: pr.state === 'MERGED' ? 'merged' : 'closed', pr, tag };
    }

    private async reset() {
        if (await this.git('status', '--porcelain')) await this.git('stash', 'push', '-u', '-m', `before the trunk reset ${this.now()}`);
        await run('git', ['-C', this.repo, 'rebase', '--abort']);
        await this.git('checkout', '-B', Trunk, `origin/${this.base}`);
    }
}

/** GitHub through the `gh` CLI, with whatever token it was given. */
export class GhCli implements GitHub {
    private readonly repo: string;

    constructor(repo: string) {
        this.repo = repo;
    }

    private gh(...args: string[]) {
        return must('gh', [...args, '--repo', this.repo]);
    }

    async view(number: number): Promise<Pr> {
        const raw = JSON.parse(await this.gh('pr', 'view', String(number), '--json', 'number,url,title,state,headRefOid,statusCheckRollup'));
        return { number: raw.number, url: raw.url, title: raw.title, state: raw.state, head: raw.headRefOid, checks: checksOf(raw.statusCheckRollup) };
    }

    async create(head: string, base: string, title: string, body: string): Promise<Pr> {
        const url = await this.gh('pr', 'create', '--head', head, '--base', base, '--title', title, '--body', body);
        return this.view(Number(url.trim().split('/').pop()));
    }

    async body(number: number) {
        return JSON.parse(await this.gh('pr', 'view', String(number), '--json', 'body')).body as string;
    }

    async edit(number: number, body: string) {
        await this.gh('pr', 'edit', String(number), '--body', body);
    }
}
