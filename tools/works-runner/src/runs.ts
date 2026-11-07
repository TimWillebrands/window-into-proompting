import { randomBytes } from 'node:crypto';
import { appendFileSync, existsSync, mkdirSync, readdirSync, readFileSync, writeFileSync } from 'node:fs';
import { rm } from 'node:fs/promises';
import path from 'node:path';
import { Acp } from './acp.ts';
import type { Config } from './config.ts';
import type { Fabric } from './fabric.ts';
import type { Model } from './meter.ts';
import { type CheckResult, checksIn, type DiffFacts, factsOf, hasSymlink, slugOf } from './patch.ts';
import { latestOpen, type Proposition, reject } from './propositions.ts';
import type { Sandbox } from './sandbox.ts';
import { type LineKind, Transcript } from './transcript.ts';

export type RunStatus = 'starting' | 'working' | 'idle' | 'checking' | 'proposed' | 'dropped' | 'done' | 'failed';
export type RunKind = 'change' | 'survey';

export interface Run {
    id: string;
    commission: string;
    kind: RunKind;
    status: RunStatus;
    model: string;
    capUsd: number;
    usd: number;
    tokens: number;
    baseRef: string;
    baseSha: string;
    parent: number | null;
    token: string;
    sessionId: string | null;
    report: string | null;
    diff: DiffFacts | null;
    /** Why the last turn ended early: budget, time or crash. */
    stop: string | null;
    error: string | null;
    proposition: number | null;
    checks: CheckResult[];
    createdAt: number;
    updatedAt: number;
}

export interface Event {
    n: number;
    at: number;
    kind: LineKind;
    text: string;
}

export interface StartRequest {
    commission: string;
    kind: RunKind;
    prompt: string;
    model: string;
    capUsd: number;
}

export interface ProposeRequest {
    title: string;
    why: string;
    author: string;
}

/** The refused request's HTTP status travels with it. */
export class Refused extends Error {
    readonly status: number;

    constructor(status: number, message: string) {
        super(message);
        this.status = status;
    }
}

export interface Meter {
    /** Opens a tab, or keeps the one there; a fresh tab starts from what was already spent. */
    open(token: string, run: string, capUsd: number, capTokens: number, usd: number, tokens: number): Promise<void>;
    raise(token: string, addUsd: number): Promise<void>;
    read(token: string): Promise<{ usd: number; tokens: number } | null>;
    close(token: string): Promise<void>;
    balance(): Promise<number | null>;
}

interface Live {
    acp: Acp;
    transcript: Transcript;
    replaying: boolean;
    stop: () => void;
}

const Busy = new Set<RunStatus>(['starting', 'working', 'checking']);
const Finished = new Set<RunStatus>(['proposed', 'dropped', 'done', 'failed']);
const Poll = 5_000;
/** How long a cancelled turn gets before the agent is killed; a full disk can hang one for good. */
const CancelGrace = 30_000;
/** Events kept in memory per run; the file keeps them all. */
const KeptEvents = 2_000;
const MaxEventsRead = 500;

export class Runs {
    private readonly config: Config;
    private readonly fabric: Fabric;
    private readonly sandbox: Sandbox;
    private readonly meter: Meter;
    private readonly runs = new Map<string, Run>();
    private readonly events = new Map<string, Event[]>();
    private readonly live = new Map<string, Live>();
    private readonly waiters = new Map<string, Set<() => void>>();
    propositions: Proposition[] = [];

    constructor(config: Config, fabric: Fabric, sandbox: Sandbox, meter: Meter) {
        this.config = config;
        this.fabric = fabric;
        this.sandbox = sandbox;
        this.meter = meter;
        this.recover();
    }

    private dir(id: string) {
        return path.join(this.config.state, 'runs', id);
    }

    private get initdb() {
        return path.join(this.config.state, 'initdb');
    }

    private get propositionsFile() {
        return path.join(this.config.state, 'propositions.json');
    }

    /** Agents die with the Runner: those runs wake idle, on their workspace, or are lost. */
    private recover() {
        mkdirSync(path.join(this.config.state, 'runs'), { recursive: true });
        if (existsSync(this.propositionsFile)) this.propositions = JSON.parse(readFileSync(this.propositionsFile, 'utf8'));
        for (const id of readdirSync(path.join(this.config.state, 'runs'))) {
            const file = path.join(this.dir(id), 'run.json');
            if (!existsSync(file)) continue;
            const run: Run = JSON.parse(readFileSync(file, 'utf8'));
            const log = path.join(this.dir(id), 'events.jsonl');
            this.events.set(id, existsSync(log) ? readFileSync(log, 'utf8').split('\n').filter(Boolean).map((l) => JSON.parse(l)) : []);
            this.runs.set(id, run);
            if (!Busy.has(run.status)) continue;
            run.status = existsSync(path.join(this.dir(id), 'work')) ? 'idle' : 'failed';
            run.stop = 'crash';
            this.save(run);
        }
    }

    private save(run: Run) {
        run.updatedAt = Date.now();
        writeFileSync(path.join(this.dir(run.id), 'run.json'), JSON.stringify(run, null, 1));
        this.wake(run.id);
    }

    private savePropositions() {
        writeFileSync(this.propositionsFile, JSON.stringify(this.propositions, null, 1));
    }

    private wake(id: string) {
        for (const wake of this.waiters.get(id) ?? []) wake();
    }

    private log(run: Run, kind: LineKind, text: string) {
        const list = this.events.get(run.id) ?? [];
        const event: Event = { n: (list.at(-1)?.n ?? 0) + 1, at: Date.now(), kind, text };
        list.push(event);
        if (list.length > KeptEvents) list.splice(0, list.length - KeptEvents);
        this.events.set(run.id, list);
        appendFileSync(path.join(this.dir(run.id), 'events.jsonl'), `${JSON.stringify(event)}\n`);
        this.wake(run.id);
    }

    get(id: string) {
        const run = this.runs.get(id);
        if (!run) throw new Refused(404, 'no such run');
        return run;
    }

    /** Public view: the Meter token stays here. */
    view(run: Run) {
        const { token: _, ...rest } = run;
        return rest;
    }

    private model(id: string): Model {
        const model = this.config.models.find((m) => m.id === id);
        if (!model) throw new Refused(400, `unknown model: ${id}`);
        return model;
    }

    get busy() {
        return [...this.runs.values()].some((r) => Busy.has(r.status));
    }

    /** Only one open change at a time; Surveys can run beside one that's idle. */
    private get openChange() {
        return [...this.runs.values()].find((r) => r.kind === 'change' && !Finished.has(r.status));
    }

    /** Answers at once, so a slow fetch can't time the World out; asking twice for one Commission gets the same run. */
    start(request: StartRequest) {
        this.model(request.model);
        const known = [...this.runs.values()].find((r) => r.commission === request.commission);
        if (known) return known;
        if (this.busy) throw new Refused(409, 'the builder is busy');
        if (request.kind === 'change' && this.openChange) throw new Refused(409, 'a change is already open');
        const id = `r${randomBytes(4).toString('hex')}`;
        mkdirSync(path.join(this.dir(id), 'home'), { recursive: true });
        const now = Date.now();
        const run: Run = {
            id, commission: request.commission, kind: request.kind, status: 'starting', model: request.model,
            capUsd: request.capUsd, usd: 0, tokens: 0, baseRef: '', baseSha: '', parent: null,
            token: randomBytes(24).toString('hex'), sessionId: null, report: null, diff: null, stop: null, error: null,
            proposition: null, checks: [], createdAt: now, updatedAt: now,
        };
        this.runs.set(id, run);
        this.events.set(id, []);
        this.save(run);
        this.launch(run, request.prompt).catch((error) => this.fail(run, error));
        return run;
    }

    private async launch(run: Run, prompt: string) {
        await this.fabric.sync();
        await this.fabric.exportInitScripts(this.initdb);
        const parent = latestOpen(this.propositions);
        run.baseRef = parent?.branch ?? 'main';
        run.parent = parent?.n ?? null;
        run.baseSha = await this.fabric.sha(run.baseRef);
        this.save(run);
        await this.fabric.checkout(run.baseSha, path.join(this.dir(run.id), 'work'));
        await this.attach(run);
        await this.turn(run, prompt);
    }

    private writeAgentConfig(run: Run) {
        const model = this.model(run.model);
        const config = {
            $schema: 'https://opencode.ai/config.json',
            share: 'disabled',
            autoupdate: false,
            enabled_providers: ['works'],
            model: `works/${model.id}`,
            provider: {
                works: {
                    npm: '@ai-sdk/openai-compatible',
                    name: 'Works',
                    options: { baseURL: this.config.meterProxy, apiKey: run.token },
                    models: { [model.id]: { name: model.id, tool_call: true, limit: { context: model.context, output: model.output } } },
                },
            },
            mcp: { aspire: { enabled: false } },
            permission: { '*': 'allow', webfetch: 'deny', websearch: 'deny', external_directory: 'deny' },
        };
        const file = path.join(this.dir(run.id), 'opencode.json');
        writeFileSync(file, JSON.stringify(config, null, 1));
        return file;
    }

    /** Boots the agent on the run's workspace, picking up its session when it has one. */
    private async attach(run: Run) {
        await this.meter.open(run.token, run.id, run.capUsd, this.config.capTokens, run.usd, run.tokens);
        const child = await this.sandbox.up(run.id, {
            work: path.join(this.dir(run.id), 'work'),
            home: path.join(this.dir(run.id), 'home'),
            config: this.writeAgentConfig(run),
            initdb: this.initdb,
        });
        const transcript = new Transcript();
        const stop = () => {
            child.kill();
            this.sandbox.kill(run.id).catch(() => undefined);
        };
        const entry: Live = { acp: undefined as unknown as Acp, transcript, replaying: false, stop };
        entry.acp = new Acp(child.stdin, child.stdout, (update) => {
            if (entry.replaying) return;
            for (const line of transcript.feed(update)) this.log(run, line.kind, line.text);
        });
        child.stderr.resume();
        entry.acp.closed.then(() => this.live.delete(run.id));
        this.live.set(run.id, entry);
        await entry.acp.initialize();
        if (run.sessionId) {
            entry.replaying = true;
            const loaded = await entry.acp.loadSession(run.sessionId, '/work').then(() => true, () => false);
            entry.replaying = false;
            if (loaded) return;
        }
        run.sessionId = await entry.acp.newSession('/work');
        this.save(run);
    }

    private overCap(run: Run) {
        return run.usd >= run.capUsd || run.tokens >= this.config.capTokens;
    }

    private async refreshSpend(run: Run) {
        const tab = await this.meter.read(run.token).catch(() => null);
        if (!tab) return;
        run.usd = Math.max(run.usd, tab.usd);
        run.tokens = Math.max(run.tokens, tab.tokens);
    }

    private async turn(run: Run, text: string) {
        const live = this.live.get(run.id);
        if (!live || !run.sessionId) throw new Error('agent is not attached');
        run.status = 'working';
        run.stop = null;
        this.save(run);
        live.transcript.startTurn();

        const halt = (why: string) => {
            if (run.stop) return;
            run.stop = why;
            this.log(run, 'status', `stopped: ${why}`);
            live.acp.cancel(run.sessionId as string);
            hardStop = setTimeout(live.stop, CancelGrace);
        };
        let hardStop: NodeJS.Timeout | undefined;
        const deadline = setTimeout(() => halt('time'), this.config.turnMs);
        const meterWatch = setInterval(async () => {
            await this.refreshSpend(run);
            this.save(run);
            if (this.overCap(run)) halt('budget');
        }, Poll);

        try {
            const result = await live.acp.prompt(run.sessionId, text);
            this.log(run, 'status', `turn ended: ${result.stopReason}`);
        } catch (error) {
            await this.refreshSpend(run);
            run.stop = run.stop ?? (this.overCap(run) ? 'budget' : 'crash');
            this.log(run, 'failed', String(error));
        } finally {
            clearTimeout(deadline);
            clearTimeout(hardStop);
            clearInterval(meterWatch);
        }
        for (const line of live.transcript.flush()) this.log(run, line.kind, line.text);
        if (run.status !== 'working') return;
        await this.refreshSpend(run);
        run.report = live.transcript.lastMessage || run.report;
        await this.collect(run);
        run.status = run.kind === 'survey' ? 'done' : 'idle';
        this.save(run);
        if (run.kind === 'survey') await this.close(run);
    }

    /** Keeps the last good patch when the agent is gone. */
    private async collect(run: Run) {
        if (run.kind === 'survey') return;
        const patch = await this.sandbox.diff(run.id, run.baseSha, path.join(this.dir(run.id), 'work')).catch(() => null);
        if (patch === null) return;
        if (patch.length > this.config.maxPatchBytes) {
            run.stop = 'patch too large';
            this.log(run, 'failed', `patch is ${patch.length} bytes, over the limit`);
            return;
        }
        writeFileSync(path.join(this.dir(run.id), 'patch.diff'), patch);
        run.diff = factsOf(patch);
    }

    private fail(run: Run, error: unknown) {
        run.status = 'failed';
        run.error = String(error).slice(0, 500);
        this.log(run, 'failed', run.error);
        this.save(run);
        this.close(run).catch(() => undefined);
    }

    /** Sandbox gone, workspace gone, tab closed. The record stays. */
    private async close(run: Run) {
        this.live.get(run.id)?.stop();
        await this.sandbox.down(run.id);
        await this.meter.close(run.token).catch(() => undefined);
        await rm(path.join(this.dir(run.id), 'work'), { recursive: true, force: true });
        await rm(path.join(this.dir(run.id), 'check'), { recursive: true, force: true });
        await rm(path.join(this.dir(run.id), 'check-home'), { recursive: true, force: true });
    }

    /** Stops idle agents to free the box; a follow-up boots one back on the same workspace. */
    async sweep() {
        for (const run of this.runs.values()) {
            if (run.status !== 'idle' || Date.now() - run.updatedAt < this.config.idleMs || !this.live.has(run.id)) continue;
            this.live.get(run.id)?.stop();
            this.live.delete(run.id);
            await this.sandbox.down(run.id);
        }
    }

    async prompt(id: string, text: string, addUsd: number) {
        const run = this.get(id);
        if (run.status !== 'idle') throw new Refused(409, `run is ${run.status}`);
        if (this.busy) throw new Refused(409, 'the builder is busy');
        if (addUsd > 0) {
            run.capUsd += addUsd;
            await this.meter.raise(run.token, addUsd).catch(() => undefined);
        }
        run.status = 'starting';
        this.save(run);
        (async () => {
            if (!this.live.has(id)) await this.attach(run);
            await this.turn(run, text);
        })().catch((error) => this.fail(run, error));
        return run;
    }

    async drop(id: string) {
        const run = this.get(id);
        if (Finished.has(run.status) || run.status === 'checking') throw new Refused(409, `run is ${run.status}`);
        await this.refreshSpend(run);
        run.status = 'dropped';
        this.save(run);
        await this.close(run);
        return run;
    }

    async propose(id: string, request: ProposeRequest) {
        const run = this.get(id);
        if (run.kind !== 'change' || run.status !== 'idle') throw new Refused(409, `run is ${run.status}`);
        const file = path.join(this.dir(id), 'patch.diff');
        if (!existsSync(file) || !run.diff?.files) throw new Refused(409, 'nothing changed');
        if (hasSymlink(readFileSync(file, 'utf8'))) throw new Refused(409, 'symlinks can not be proposed');
        run.status = 'checking';
        this.save(run);
        this.finishProposal(run, readFileSync(file, 'utf8'), request).catch((error) => this.fail(run, error));
        return run;
    }

    private async finishProposal(run: Run, patch: string, request: ProposeRequest) {
        const n = (this.propositions.at(-1)?.n ?? 0) + 1;
        const branch = `proposition/${n}-${slugOf(request.title)}`;
        const sha = await this.fabric.propose(branch, run.baseSha, patch, {
            name: request.author, title: request.title, why: request.why, commission: run.commission,
        });
        const proposition: Proposition = {
            n, commission: run.commission, title: request.title, why: request.why, author: request.author, branch,
            parent: run.parent, baseSha: run.baseSha, sha, status: 'open', note: null, checks: [], createdAt: Date.now(),
        };
        this.propositions.push(proposition);
        this.savePropositions();
        run.proposition = n;

        this.live.get(run.id)?.stop();
        await this.sandbox.down(run.id);
        const check = path.join(this.dir(run.id), 'check');
        await this.fabric.checkout(branch, check);
        this.log(run, 'status', `checking ${branch}`);
        const home = path.join(this.dir(run.id), 'check-home');
        mkdirSync(home, { recursive: true });
        const output = await this.sandbox.check(run.id, { work: check, home, initdb: this.initdb }, (line) => {
            if (line.startsWith('::check ')) this.log(run, 'check', line.slice(8));
        });
        run.checks = checksIn(output);
        proposition.checks = run.checks;
        this.savePropositions();
        run.status = 'proposed';
        this.save(run);
        await this.close(run);
    }

    async readEvents(id: string, after: number, waitMs: number, tail = MaxEventsRead) {
        const run = this.get(id);
        const fresh = () => (this.events.get(id) ?? []).filter((e) => e.n > after);
        if (fresh().length === 0 && waitMs > 0 && !Finished.has(run.status)) await this.idleWait(id, waitMs);
        return { events: fresh().slice(-Math.max(1, Math.min(tail, MaxEventsRead))), run: this.view(run) };
    }

    private idleWait(id: string, waitMs: number) {
        return new Promise<void>((resolve) => {
            const waiters = this.waiters.get(id) ?? new Set();
            this.waiters.set(id, waiters);
            const done = () => {
                clearTimeout(timer);
                waiters.delete(done);
                resolve();
            };
            const timer = setTimeout(done, waitMs);
            waiters.add(done);
        });
    }

    /** Merged is read from the repo itself, so whoever merges needs to tell nobody. */
    async listPropositions() {
        const open = this.propositions.filter((p) => p.status === 'open');
        if (open.length === 0) return this.propositions;
        const synced = await this.fabric.sync().then(() => true, () => false);
        if (!synced) return this.propositions;
        for (const p of open) {
            if (await this.fabric.merged(p.branch)) p.status = 'merged';
        }
        this.savePropositions();
        return this.propositions;
    }

    proposition(n: number) {
        const found = this.propositions.find((p) => p.n === n);
        if (!found) throw new Refused(404, 'no such proposition');
        return found;
    }

    rejectProposition(n: number, note: string | null) {
        if (!reject(this.propositions, n, note)) throw new Refused(409, 'not open');
        this.savePropositions();
        return this.proposition(n);
    }
}
