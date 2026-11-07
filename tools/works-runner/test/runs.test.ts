import assert from 'node:assert/strict';
import { execFileSync } from 'node:child_process';
import { mkdirSync, mkdtempSync, rmSync, writeFileSync } from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import readline from 'node:readline';
import { PassThrough } from 'node:stream';
import { after, before, test } from 'node:test';
import type { Config } from '../src/config.ts';
import { Fabric } from '../src/fabric.ts';
import { type Meter, Refused, Runs } from '../src/runs.ts';
import type { Sandbox } from '../src/sandbox.ts';

const root = mkdtempSync(path.join(os.tmpdir(), 'runs-test-'));
const origin = path.join(root, 'origin');
const git = (cwd: string, ...args: string[]) => execFileSync('git', args, { cwd, encoding: 'utf8' }).trim();

const patch = `diff --git a/world.txt b/world.txt
index 0000000..1111111 100644
--- a/world.txt
+++ b/world.txt
@@ -1 +1 @@
-old
+new
`;

before(() => {
    execFileSync('git', ['init', '-q', '-b', 'master', origin]);
    mkdirSync(path.join(origin, 'docker-entrypoint-initdb.d'));
    writeFileSync(path.join(origin, 'docker-entrypoint-initdb.d', '01.sql'), 'select 1;\n');
    writeFileSync(path.join(origin, 'world.txt'), 'old\n');
    git(origin, 'add', '.');
    git(origin, '-c', 'user.name=t', '-c', 'user.email=t@t', 'commit', '-q', '-m', 'first');
});

after(() => rmSync(root, { recursive: true, force: true }));

/** Answers ACP like an agent would; `script` decides how each prompt ends. */
function fakeAgent(script: () => 'ok' | 'error' | 'hang') {
    const stdin = new PassThrough();
    const stdout = new PassThrough();
    const reply = (m: unknown) => stdout.write(`${JSON.stringify(m)}\n`);
    readline.createInterface({ input: stdin }).on('line', (line) => {
        const { id, method } = JSON.parse(line);
        if (method === 'initialize') reply({ jsonrpc: '2.0', id, result: {} });
        if (method === 'session/new') reply({ jsonrpc: '2.0', id, result: { sessionId: 's1' } });
        if (method === 'session/load') reply({ jsonrpc: '2.0', id, result: {} });
        if (method === 'session/prompt') {
            const how = script();
            if (how === 'hang') return;
            if (how === 'error') return reply({ jsonrpc: '2.0', id, error: { message: 'budget spent' } });
            reply({ jsonrpc: '2.0', method: 'session/update', params: { update: { sessionUpdate: 'agent_message_chunk', content: { text: 'I did it.' } } } });
            reply({ jsonrpc: '2.0', id, result: { stopReason: 'end_turn' } });
        }
    });
    return { stdin, stdout, stderr: new PassThrough(), kill: () => stdout.end() };
}

function world(name: string, options: { script?: () => 'ok' | 'error' | 'hang'; tab?: { usd: number; tokens: number }; diff?: string } = {}) {
    const state = path.join(root, name);
    const config = { state, models: [{ id: 'm', label: 'm', in: 0, out: 0, context: 1, output: 1 }], capTokens: 1000, turnMs: 60_000, idleMs: 60_000, maxPatchBytes: 1000, meterProxy: 'x' } as unknown as Config;
    const fabric = new Fabric(path.join(state, 'fabric.git'), origin, 'master');
    const sandbox = {
        up: async () => fakeAgent(options.script ?? (() => 'ok')),
        diff: async () => options.diff ?? patch,
        check: async () => '::check backend-build pass 3\n::check backend-tests fail 4 Failed: 1\n',
        kill: async () => undefined,
        down: async () => undefined,
    } as unknown as Sandbox;
    const meter: Meter = {
        open: async () => undefined,
        raise: async () => undefined,
        read: async () => options.tab ?? { usd: 0.1, tokens: 50 },
        close: async () => undefined,
        balance: async () => null,
    };
    return { runs: new Runs(config, fabric, sandbox, meter), config, fabric, sandbox, meter };
}

const change = { commission: 'c1', kind: 'change' as const, prompt: 'go', model: 'm', capUsd: 1 };

async function until(check: () => boolean) {
    for (let i = 0; i < 400 && !check(); i++) await new Promise((done) => setTimeout(done, 5));
    assert.ok(check(), 'never reached the expected state');
}

test('a run reaches idle with the meter and diff as its facts', async () => {
    const { runs } = world('facts');
    const run = await runs.start(change);
    await until(() => run.status === 'idle');
    assert.deepEqual([run.report, run.usd, run.tokens, run.diff?.files, run.stop], ['I did it.', 0.1, 50, 1, null]);
    assert.equal('token' in runs.view(run), false);
});

test('only one change, and nothing beside a working agent', async () => {
    const { runs } = world('one');
    const first = await runs.start(change);
    assert.throws(() => runs.start({ ...change, commission: 'c-other' }), (e: Refused) => e.status === 409);
    await until(() => first.status === 'idle');
    assert.throws(() => runs.start({ ...change, commission: 'c-other' }), /already open/);
    const survey = await runs.start({ ...change, commission: 'c2', kind: 'survey' });
    await until(() => survey.status === 'done');
    assert.equal(survey.diff, null);
});

test('an unknown model is refused before anything starts', async () => {
    const { runs } = world('model');
    assert.throws(() => runs.start({ ...change, model: 'costly' }), (e: Refused) => e.status === 400);
});

test('an agent stopped by its cap reports budget, not a crash', async () => {
    const { runs } = world('budget', { script: () => 'error', tab: { usd: 1, tokens: 50 } });
    const run = await runs.start(change);
    await until(() => run.status === 'idle');
    assert.equal(run.stop, 'budget');
});

test('an agent failing under its cap reports a crash', async () => {
    const { runs } = world('crash', { script: () => 'error' });
    const run = await runs.start(change);
    await until(() => run.status === 'idle');
    assert.equal(run.stop, 'crash');
});

test('an oversized patch is refused and the last good one stands', async () => {
    const { runs } = world('big', { diff: 'x'.repeat(2000) });
    const run = await runs.start(change);
    await until(() => run.status === 'idle');
    assert.equal(run.stop, 'patch too large');
    assert.equal(run.diff, null);
    await assert.rejects(runs.propose(run.id, { title: 't', why: 'w', author: 'a' }), /nothing changed/);
});

test('a Proposition carries its checks, and the next run stacks on it until it is rejected', async () => {
    const { runs } = world('stack');
    const run = await runs.start(change);
    await until(() => run.status === 'idle');
    await runs.propose(run.id, { title: 'Mend It', why: 'Because', author: 'Ria' });
    await until(() => run.status === 'proposed');
    const [proposition] = runs.propositions;
    assert.deepEqual([proposition.n, proposition.branch, proposition.status, proposition.checks.length], [1, 'proposition/1-mend-it', 'open', 2]);
    assert.equal(run.checks[1].passed, false);

    const next = await runs.start({ ...change, commission: 'c2' });
    await until(() => next.baseSha !== '');
    assert.deepEqual([next.baseRef, next.parent], ['proposition/1-mend-it', 1]);
    await until(() => next.status === 'idle');
    await runs.drop(next.id);

    runs.rejectProposition(1, 'too loud');
    const after = await runs.start({ ...change, commission: 'c3' });
    await until(() => after.baseSha !== '');
    assert.deepEqual([after.baseRef, after.parent], ['main', null]);
    await until(() => after.status === 'idle');
});

test('nothing changed means nothing to propose', async () => {
    const { runs } = world('empty', { diff: '' });
    const run = await runs.start(change);
    await until(() => run.status === 'idle');
    await assert.rejects(runs.propose(run.id, { title: 't', why: 'w', author: 'a' }), /nothing changed/);
});

test('a follow-up raises the cap and runs another turn', async () => {
    const { runs } = world('steer');
    const run = await runs.start(change);
    await until(() => run.status === 'idle');
    await runs.prompt(run.id, 'more', 2);
    assert.equal(run.capUsd, 3);
    await until(() => run.status === 'idle');
    await assert.rejects(runs.prompt('rnope', 'x', 0), (e: Refused) => e.status === 404);
});

test('a restarted Runner wakes a working run idle, marked as crashed', async () => {
    const first = world('restart');
    const run = await first.runs.start(change);
    await until(() => run.status === 'idle');
    writeFileSync(path.join(first.config.state, 'runs', run.id, 'run.json'), JSON.stringify({ ...run, status: 'working' }));

    const woken = new Runs(first.config, first.fabric, first.sandbox, first.meter).get(run.id);

    assert.deepEqual([woken.status, woken.stop], ['idle', 'crash']);
});

test('events wait for news and report what is new', async () => {
    const { runs } = world('events');
    const run = await runs.start(change);
    await until(() => run.status === 'idle');
    const first = await runs.readEvents(run.id, 0, 0);
    assert.ok(first.events.length > 0);
    const last = first.events.at(-1)?.n ?? 0;
    const started = Date.now();
    const none = await runs.readEvents(run.id, last, 30);
    assert.equal(none.events.length, 0);
    assert.ok(Date.now() - started >= 25);
});

test('two starts at once get one run: the gate holds without awaiting', async () => {
    const { runs } = world('race');
    const first = runs.start(change);
    assert.throws(() => runs.start({ ...change, commission: 'c-race' }), (e: Refused) => e.status === 409);
    await until(() => first.status === 'idle');
});

test('asking again for the same Commission returns its run, even after a lost answer', async () => {
    const { runs } = world('again');
    const first = runs.start(change);
    assert.equal(runs.start(change), first);
    await until(() => first.status === 'idle');
    assert.equal(runs.start(change).id, first.id);
});

test('dropping a working run stays dropped when its turn ends', async () => {
    const { runs } = world('drop-working', { script: () => 'hang' });
    const run = runs.start(change);
    await until(() => run.status === 'working');
    await runs.drop(run.id);
    await new Promise((done) => setTimeout(done, 50));
    assert.equal(run.status, 'dropped');
});

test('a patch with a symlink can not become a Proposition', async () => {
    const link = 'diff --git a/l b/l\nnew file mode 120000\nindex 0000000..1111111\n--- /dev/null\n+++ b/l\n@@ -0,0 +1 @@\n+/etc\n';
    const { runs } = world('symlink', { diff: link });
    const run = runs.start(change);
    await until(() => run.status === 'idle');
    await assert.rejects(runs.propose(run.id, { title: 't', why: 'w', author: 'a' }), /symlinks/);
});

test('reading events keeps to a tail', async () => {
    const { runs } = world('tail');
    const run = runs.start(change);
    await until(() => run.status === 'idle');
    const all = await runs.readEvents(run.id, 0, 0);
    const one = await runs.readEvents(run.id, 0, 0, 1);
    assert.deepEqual(one.events, all.events.slice(-1));
});
