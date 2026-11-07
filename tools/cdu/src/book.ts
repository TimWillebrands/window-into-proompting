import { appendFileSync, cpSync, existsSync, mkdirSync } from 'node:fs';
import path from 'node:path';
import { must, run } from './exec.ts';

const Me = ['-c', 'user.name=CDU', '-c', 'user.email=cdu@proompting.party'];

/** The Book of the CDU: plain files in a git store, kept after every turn so nothing it wrote is lost. */
export class Book {
    readonly dir: string;
    private readonly seed: string;
    private readonly now: () => number;

    constructor(dir: string, seed: string, now: () => number = Date.now) {
        this.dir = dir;
        this.seed = seed;
        this.now = now;
    }

    private git(...args: string[]) {
        return must('git', ['-C', this.dir, ...Me, ...args]);
    }

    /** Seeded once; after that it's the CDU's. */
    async ensure() {
        if (existsSync(path.join(this.dir, '.git'))) return;
        mkdirSync(this.dir, { recursive: true });
        cpSync(this.seed, this.dir, { recursive: true });
        await must('git', ['init', '-q', '-b', 'main', this.dir]);
        await this.keep('The Book begins');
    }

    /** Commits whatever changed; false when nothing did. */
    async keep(message: string): Promise<boolean> {
        await this.git('add', '-A');
        if ((await run('git', ['-C', this.dir, 'diff', '--cached', '--quiet'])).code === 0) return false;
        await this.git('commit', '-q', '-m', message);
        return true;
    }

    /** A fact written by the service, not by the CDU: dated and kept at once. */
    async journal(line: string) {
        appendFileSync(path.join(this.dir, 'journal.md'), `\n- ${new Date(this.now()).toISOString().slice(0, 16).replace('T', ' ')} UTC: ${line}\n`);
        await this.keep(`journal: ${line.slice(0, 60)}`);
    }
}
