import { type ChildProcessWithoutNullStreams, execFile, spawn } from 'node:child_process';
import { promisify } from 'node:util';
import type { Config } from './config.ts';

const exec = promisify(execFile);

export interface Mounts {
    work: string;
    home: string;
    config: string;
    initdb: string;
}

export const name = (id: string) => `works-${id}`;
const netName = (id: string) => `works-${id}-net`;

/** Docker calls for one run: a Postgres+AGE sidecar owning the netns, and the agent sharing it on an internal network. */
export class Sandbox {
    private readonly config: Config;

    constructor(config: Config) {
        this.config = config;
    }

    private limits() {
        const { cpus, memory, pids, runtime } = this.config;
        const user = `${process.getuid?.() ?? 1000}:${process.getgid?.() ?? 1000}`;
        return [
            '--user', user, '--cap-drop', 'ALL', '--security-opt', 'no-new-privileges',
            '--cpus', cpus, '--memory', memory, '--pids-limit', pids,
            ...(runtime ? ['--runtime', runtime] : []),
        ];
    }

    /** Orleans needs a real interface and tests need Postgres on 5455, so --network none won't do. Init scripts come from main, never a workspace. */
    private async sidecar(id: string, initdb: string) {
        const { runtime } = this.config;
        await exec('docker', ['rm', '-f', netName(id)]).catch(() => undefined);
        await exec('docker', [
            'run', '-d', '--name', netName(id), '--network', this.config.network, '--label', 'works=1',
            '-e', 'POSTGRES_USER=partytown', '-e', 'POSTGRES_DB=partytown', '-e', 'POSTGRES_PASSWORD=postgresPW', '-e', 'PGPORT=5455',
            '-v', `${initdb}:/docker-entrypoint-initdb.d:ro`, '--security-opt', 'no-new-privileges',
            ...(runtime ? ['--runtime', runtime] : []), '--memory', '2g', this.config.postgresImage,
        ]);
    }

    private shared(id: string, work: string, home: string) {
        return [
            '--network', `container:${netName(id)}`, '--label', 'works=1', ...this.limits(),
            '-v', `${work}:/work`, '-v', `${home}:/home/builder`, '-e', 'HOME=/home/builder', '-w', '/work',
        ];
    }

    /** Starts the agent; its stdio is the ACP pipe. */
    async up(id: string, mounts: Mounts): Promise<ChildProcessWithoutNullStreams> {
        await this.sidecar(id, mounts.initdb);
        await exec('docker', ['rm', '-f', name(id)]).catch(() => undefined);
        return spawn('docker', [
            'run', '-i', '--rm', '--name', name(id), ...this.shared(id, mounts.work, mounts.home),
            '-v', `${mounts.config}:/etc/opencode/opencode.json:ro`, this.config.image, 'works-entry',
        ]);
    }

    /** What changed since base, read inside a sandbox so the host never runs git on that .git. A dead agent gets a throwaway container. */
    async diff(id: string, base: string, work: string) {
        const script = `git config --global --add safe.directory /work; git add -A && git diff --cached --binary ${base}`;
        const big = { maxBuffer: 64 * 1024 * 1024 };
        const live = await exec('docker', ['exec', name(id), 'sh', '-c', script], big).catch(() => null);
        if (live) return live.stdout;
        const args = ['run', '--rm', '--network', 'none', '--label', 'works=1', ...this.limits(), '-v', `${work}:/work`, '-e', 'HOME=/tmp', '-w', '/work', this.config.image, 'sh', '-c', script];
        return (await exec('docker', args, big)).stdout;
    }

    /** Ends the agent outright, for a turn that won't answer to cancel. */
    async kill(id: string) {
        await exec('docker', ['rm', '-f', name(id)]).catch(() => undefined);
    }

    /** Runs the fixed checks on a clean checkout with a clean home; the commands live in the image, out of the patch's reach. */
    check(id: string, mounts: Omit<Mounts, 'config'>, onLine: (line: string) => void): Promise<string> {
        const { work, home, initdb } = mounts;
        return new Promise((resolve, reject) => {
            this.sidecar(id, initdb).then(() => {
                const child = spawn('docker', ['run', '--rm', '--name', `${name(id)}-check`, ...this.shared(id, work, home), this.config.image, 'works-checks']);
                let output = '';
                let tail = '';
                const take = (chunk: Buffer) => {
                    output += chunk;
                    const lines = (tail + chunk).split('\n');
                    tail = lines.pop() ?? '';
                    for (const line of lines) onLine(line);
                };
                child.stdout.on('data', take);
                child.stderr.on('data', take);
                child.on('error', reject);
                child.on('close', () => resolve(output));
            }, reject);
        });
    }

    async down(id: string) {
        await exec('docker', ['rm', '-f', name(id), `${name(id)}-check`, netName(id)]).catch(() => undefined);
    }
}
