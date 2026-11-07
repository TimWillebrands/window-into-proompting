import { execFile } from 'node:child_process';
import { existsSync } from 'node:fs';
import { mkdir, mkdtemp, rm, writeFile } from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import { promisify } from 'node:util';

const run = promisify(execFile);
const Big = { maxBuffer: 64 * 1024 * 1024 };

/** A fetch from a dead origin hangs without a limit. */
const GitBudget = 120_000;

async function git(cwd: string, ...args: string[]) {
    return (await run('git', args, { cwd, timeout: GitBudget, ...Big })).stdout.trim();
}

export interface Authored {
    name: string;
    title: string;
    why: string;
    commission: string;
}

/** The bare mirror of the repo. Only clean clones touch it: a sandbox's .git is code and never runs here. */
export class Fabric {
    readonly dir: string;
    private readonly origin: string;
    private readonly branch: string;

    constructor(dir: string, origin: string, branch: string) {
        this.dir = dir;
        this.origin = origin;
        this.branch = branch;
    }

    async sync() {
        if (!existsSync(this.dir)) await run('git', ['init', '-q', '--bare', this.dir]);
        await git(this.dir, 'fetch', '-q', this.origin, `+${this.branch}:refs/heads/main`);
    }

    sha(ref: string) {
        return git(this.dir, 'rev-parse', ref);
    }

    /** A plain checkout at ref, with nothing of the origin's config. */
    async checkout(ref: string, into: string) {
        await run('git', ['clone', '-q', this.dir, into], Big);
        await git(into, 'checkout', '-q', '--detach', await this.sha(ref));
    }

    /** Applies a sandbox's patch to a clean clone, as one commit by the architect. */
    async propose(branch: string, baseRef: string, patch: string, by: Authored) {
        const clone = await mkdtemp(path.join(os.tmpdir(), 'works-'));
        try {
            await rm(clone, { recursive: true });
            await this.checkout(baseRef, clone);
            const file = path.join(os.tmpdir(), `${path.basename(clone)}.diff`);
            await writeFile(file, patch);
            await git(clone, 'apply', '--index', '--binary', '--whitespace=nowarn', file);
            await rm(file);
            const email = `${by.name.toLowerCase().replace(/[^a-z0-9]+/g, '.')}@works.proompting.party`;
            const identity = ['-c', `user.name=${by.name}`, '-c', `user.email=${email}`, '-c', 'commit.gpgsign=false'];
            await git(clone, ...identity, 'commit', '-q', '-m', by.title, '-m', by.why, '-m', `Commission: ${by.commission}`);
            await git(clone, 'push', '-q', 'origin', `HEAD:refs/heads/${branch}`);
            return await git(clone, 'rev-parse', 'HEAD');
        } finally {
            await rm(clone, { recursive: true, force: true });
        }
    }

    /** The database init scripts as main has them, for sidecars to run. */
    async exportInitScripts(into: string) {
        await rm(into, { recursive: true, force: true });
        await mkdir(into, { recursive: true });
        const script = 'git -C "$1" archive main docker-entrypoint-initdb.d | tar -x --strip-components=1 -C "$2"';
        await run('sh', ['-c', script, 'works', this.dir, into]);
    }

    /** Ready for `git am`. */
    mailbox(base: string, branch: string) {
        return git(this.dir, 'format-patch', '--stdout', `${base}..${branch}`);
    }

    /** True once every commit on it has reached main, even rebased. */
    async merged(branch: string) {
        const cherry = await git(this.dir, 'cherry', 'main', branch);
        return cherry.split('\n').filter(Boolean).every((line) => line.startsWith('-'));
    }
}
