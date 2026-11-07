import { mkdirSync, readFileSync } from 'node:fs';
import path from 'node:path';
import { createApi } from './api.ts';
import { Book } from './book.ts';
import { type Allowance, Cdu, Store } from './cdu.ts';
import { loadConfig } from './config.ts';
import { Feed } from './feed.ts';
import { createPiMind, type MeterModel } from './pi.ts';
import { GhCli, Tide } from './tide.ts';

// Pi can crash the process from a tool (pi #10585): die loudly and let the container restart us.
process.on('uncaughtException', (error) => {
    console.error('uncaught, restarting:', error);
    process.exit(1);
});
process.on('unhandledRejection', (error) => console.error('unhandled:', error));

const config = loadConfig();
const here = path.dirname(new URL(import.meta.url).pathname);
mkdirSync(config.state, { recursive: true });
process.env.CDU_BOOK = config.book;
process.env.GIT_COMMITTER_NAME ??= 'CDU';
process.env.GIT_COMMITTER_EMAIL ??= 'cdu@proompting.party';

const tab = async (pathname: string) => {
    const answer = await fetch(`${config.meterUrl}/${pathname}`, {
        headers: { authorization: `Bearer ${process.env.CDU_METER_TAB}` },
        signal: AbortSignal.timeout(5000),
    });
    if (!answer.ok) throw new Error(`meter ${pathname}: ${answer.status}`);
    return answer.json();
};
const allowance = async (): Promise<Allowance> => {
    const t = (await tab('tab')) as Allowance;
    return { usd: t.usd, capUsd: t.capUsd };
};

const store = new Store(path.join(config.state, 'state.json'));
const tide = new Tide({
    repo: config.repo,
    origin: config.origin,
    github: new GhCli(config.githubRepo),
    base: config.base,
    branch: config.branch,
    pr: store.state.pr,
    onPr: (pr) => {
        store.state.pr = pr;
        store.save();
    },
});
const book = new Book(config.book, path.join(here, '..', 'book'));

await tide.ensure();
await book.ensure();
const soul = (await tide.show(config.soulPath)) ?? readFileSync(path.join(here, '..', 'soul', 'APPEND_SYSTEM.md'), 'utf8');
store.state.soul = soul;
const models = ((await tab('models')) as { data: MeterModel[] }).data;
const mind = await createPiMind({
    repo: config.repo,
    agentDir: config.agentDir,
    meterUrl: config.meterUrl,
    tabEnv: 'CDU_METER_TAB',
    models,
    model: config.model,
    thinking: config.thinking,
    databaseUrlEnv: config.databaseUrl ? 'CDU_DATABASE_URL' : undefined,
    soul,
});

const cdu = new Cdu({
    mind,
    tide,
    book,
    store,
    lines: new Feed(path.join(config.state, 'lines.jsonl'), 5000),
    news: new Feed(path.join(config.state, 'news.jsonl'), 1000),
    allowance,
    models: models.map((m) => ({ id: m.id, label: m.label, tier: m.tier, in: m.in, out: m.out })),
    soulPath: config.soulPath,
});

createApi(cdu, config.token).listen(config.port, config.bind, () => console.log(`CDU awake on ${config.bind}:${config.port}, thinking with ${mind.model}`));

setInterval(() => cdu.watch().catch((error) => console.error('watch:', error)), config.watchMs).unref();
