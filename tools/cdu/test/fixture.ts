import { mkdirSync, mkdtempSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { must } from '../src/exec.ts';
import type { GitHub, Pr } from '../src/tide.ts';

process.env.GIT_COMMITTER_NAME = 'CDU';
process.env.GIT_COMMITTER_EMAIL = 'cdu@proompting.party';

export const git = (dir: string, ...args: string[]) => must('git', ['-C', dir, '-c', 'user.name=Tim', '-c', 'user.email=tim@example.com', ...args]);

/** An origin with a master, and a desk where the One lands changes on it. */
export async function world() {
    const root = mkdtempSync(path.join(tmpdir(), 'cdu-'));
    const origin = path.join(root, 'origin.git');
    const desk = path.join(root, 'desk');
    await must('git', ['init', '-q', '--bare', '-b', 'master', origin]);
    await must('git', ['clone', '-q', origin, desk]);
    mkdirSync(path.join(desk, 'tools/cdu/soul'), { recursive: true });
    writeFileSync(path.join(desk, 'tools/cdu/soul/APPEND_SYSTEM.md'), 'soul one\n');
    writeFileSync(path.join(desk, 'world.txt'), 'line one\n');
    await git(desk, 'add', '-A');
    await git(desk, 'commit', '-q', '-m', 'the world begins');
    await git(desk, 'push', '-q', 'origin', 'HEAD:master');
    const land = async (file: string, text: string, message = `change ${file}`) => {
        writeFileSync(path.join(desk, file), text);
        await git(desk, 'add', '-A');
        await git(desk, 'commit', '-q', '-m', message);
        await git(desk, 'push', '-q', 'origin', 'HEAD:master');
    };
    /** What the One does on GitHub when merging: the tide branch lands on master. */
    const merge = async (branch: string) => {
        await git(desk, 'fetch', '-q', 'origin');
        await git(desk, 'reset', '-q', '--hard', `origin/${branch}`);
        await git(desk, 'push', '-q', 'origin', 'HEAD:master');
    };
    return { root, origin, desk, repo: path.join(root, 'repo'), land, merge };
}

/** GitHub in memory: PRs open when created, and the test plays the One. */
export class FakeGitHub implements GitHub {
    prs = new Map<number, Pr & { body: string; base: string; branch: string }>();
    private n = 0;

    async view(number: number): Promise<Pr> {
        const pr = this.prs.get(number);
        if (!pr) throw new Error(`no PR ${number}`);
        const { body: _, base: __, branch: ___, ...view } = pr;
        return view;
    }

    async create(branch: string, base: string, title: string, body: string): Promise<Pr> {
        const number = ++this.n;
        this.prs.set(number, { number, url: `https://example.com/pull/${number}`, title, state: 'OPEN', head: 'h', checks: 'pending', body, base, branch });
        return this.view(number);
    }

    async body(number: number) {
        return this.prs.get(number)?.body ?? '';
    }

    async edit(number: number, body: string) {
        const pr = this.prs.get(number);
        if (pr) pr.body = body;
    }

    judge(number: number, state: Pr['state']) {
        const pr = this.prs.get(number);
        if (pr) pr.state = state;
    }
}
