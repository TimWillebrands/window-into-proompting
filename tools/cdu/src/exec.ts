import { spawn } from 'node:child_process';

export interface Ran {
    code: number;
    stdout: string;
    stderr: string;
}

export interface RunOptions {
    cwd?: string;
    env?: NodeJS.ProcessEnv;
    input?: string;
}

/** Runs a program without a shell; never throws on a non-zero exit. */
export function run(cmd: string, args: string[], options: RunOptions = {}): Promise<Ran> {
    return new Promise((resolve, reject) => {
        const stdin = options.input === undefined ? 'ignore' : 'pipe';
        const child = spawn(cmd, args, { cwd: options.cwd, env: options.env ?? process.env, stdio: [stdin, 'pipe', 'pipe'] });
        let stdout = '';
        let stderr = '';
        child.stdout?.on('data', (d) => (stdout += d));
        child.stderr?.on('data', (d) => (stderr += d));
        child.on('error', reject);
        child.on('close', (code) => resolve({ code: code ?? 1, stdout, stderr }));
        child.stdin?.on('error', () => {});
        child.stdin?.end(options.input);
    });
}

/** Like run, but a non-zero exit is an error carrying what it said. */
export async function must(cmd: string, args: string[], options: RunOptions = {}): Promise<string> {
    const ran = await run(cmd, args, options);
    if (ran.code !== 0) throw new Error(`${cmd} ${args.join(' ')}: ${(ran.stderr || ran.stdout).trim().slice(-800)}`);
    return ran.stdout.trim();
}
