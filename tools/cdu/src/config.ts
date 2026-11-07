import path from 'node:path';

export interface Config {
    token: string;
    bind: string;
    port: number;
    home: string;
    repo: string;
    book: string;
    agentDir: string;
    state: string;
    origin: string;
    githubRepo: string;
    base: string;
    branch: string;
    meterUrl: string;
    model?: string;
    thinking: 'off' | 'low' | 'medium' | 'high';
    databaseUrl: boolean;
    watchMs: number;
    soulPath: string;
}

export function loadConfig(env: NodeJS.ProcessEnv = process.env): Config {
    const need = (name: string) => {
        const value = env[name];
        if (!value) throw new Error(`${name} is not set`);
        return value;
    };
    need('CDU_METER_TAB');
    const home = path.resolve(env.CDU_HOME ?? '/cdu');
    const githubRepo = env.CDU_GITHUB_REPO ?? 'TimWillebrands/window-into-proompting';
    return {
        token: need('CDU_TOKEN'),
        bind: env.CDU_BIND ?? '0.0.0.0',
        port: Number(env.CDU_PORT ?? 8790),
        home,
        repo: path.join(home, 'repo'),
        book: env.CDU_BOOK ?? path.join(home, 'book'),
        agentDir: env.PI_CODING_AGENT_DIR ?? path.join(home, 'pi'),
        state: path.join(home, 'state'),
        origin: env.CDU_ORIGIN ?? `https://github.com/${githubRepo}.git`,
        githubRepo,
        base: env.CDU_BASE ?? 'master',
        branch: env.CDU_TIDE_BRANCH ?? 'cdu/tide',
        meterUrl: (env.CDU_METER_URL ?? 'http://cdu-meter:8788/v1').replace(/\/$/, ''),
        model: env.CDU_MODEL || undefined,
        thinking: (env.CDU_THINKING as Config['thinking']) ?? 'medium',
        databaseUrl: Boolean(env.CDU_DATABASE_URL),
        watchMs: Number(env.CDU_WATCH_SECONDS ?? 60) * 1000,
        soulPath: env.CDU_SOUL_PATH ?? 'tools/cdu/soul/APPEND_SYSTEM.md',
    };
}
