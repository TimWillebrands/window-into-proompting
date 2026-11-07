import assert from 'node:assert/strict';
import { writeFileSync } from 'node:fs';
import path from 'node:path';
import { test } from 'node:test';
import { Book } from '../src/book.ts';
import { asks, Cdu, compose, emailOf, type Mind, Store, type Turn } from '../src/cdu.ts';
import { must } from '../src/exec.ts';
import { Feed } from '../src/feed.ts';
import { Tide } from '../src/tide.ts';
import { FakeGitHub, git, world } from './fixture.ts';

/** Does what the test says on each waking, and remembers what it heard. */
class FakeMind implements Mind {
    readonly model = 'fake';
    heard: string[] = [];
    souls: string[] = [];
    next: (repo: string, book: string) => Promise<string> = async () => 'Done.';
    repo = '';
    book = '';

    async think(text: string, onEvent: (e: Record<string, unknown>) => void) {
        this.heard.push(text);
        onEvent({ type: 'tool_execution_start', toolName: 'bash', args: { command: 'dotnet build' } });
        return this.next(this.repo, this.book);
    }

    async resoul(soul: string) {
        this.souls.push(soul);
    }
}

async function setup(statePath?: string) {
    const w = await world();
    const github = new FakeGitHub();
    const tide = new Tide({ repo: w.repo, origin: w.origin, github, now: () => Date.parse('2026-11-08T12:00:00Z') });
    await tide.ensure();
    const book = new Book(path.join(w.root, 'book'), path.join(import.meta.dirname, '..', 'book'));
    await book.ensure();
    const mind = new FakeMind();
    mind.repo = w.repo;
    mind.book = book.dir;
    let spent = 0;
    const make = (store = new Store(statePath)) => new Cdu({
        mind, tide, book, store, lines: new Feed(), news: new Feed(),
        allowance: async () => ({ usd: spent, capUsd: 5 }),
    });
    return { ...w, github, tide, book, mind, cdu: make(), make, spend: (usd: number) => (spent += usd) };
}

const denise = { id: 'p-denise', name: 'Denise' };
const done = async (cdu: Cdu, turn: Turn) => {
    await cdu.idle();
    return cdu.turn(turn.id) as Turn;
};

test('a turn: the CDU hears who speaks, commits as them, and its reply is news for them', async () => {
    const s = await setup();
    s.mind.next = async (repo, book) => {
        writeFileSync(path.join(repo, 'fix.txt'), 'fixed');
        await git(repo, 'add', '-A');
        await must('git', ['-C', repo, 'commit', '-q', '-m', 'fix: the thing']);
        writeFileSync(path.join(book, 'people', 'denise.md'), 'Denise wants fewer timeouts.');
        s.spend(0.25);
        return 'I moved the Forms a little.\n\nShall I go further?';
    };
    const turn = await done(s.cdu, s.cdu.talk(denise, 'The timeouts are killing us.'));
    assert.equal(turn.status, 'done');
    assert.equal(turn.asked, true);
    assert.equal(turn.usd, 0.25);
    assert.deepEqual(turn.commits.map((c) => [c.subject, c.author]), [['fix: the thing', 'Denise']]);
    assert.match(s.mind.heard[0], /^Denise, a Cytheurgist, speaks to you:\n\nThe timeouts are killing us\.$/);
    assert.equal(await must('git', ['-C', s.repo, 'config', 'user.email']), emailOf(denise));
    assert.match(await git(s.book.dir, 'log', '-1', '--format=%s'), /^after Denise: The timeouts/);
    const [replied] = s.cdu.news.after(0);
    assert.equal(replied.kind, 'replied');
    assert.deepEqual(replied.data?.to, denise);
    assert.equal(replied.data?.asked, true);
    assert.deepEqual(s.cdu.lines.after(0).map((l) => l.kind), ['heard', 'tool']);
});

test('turns run one at a time, in order', async () => {
    const s = await setup();
    const order: string[] = [];
    s.mind.next = async () => {
        order.push(s.mind.heard.at(-1) ?? '');
        await new Promise((r) => setTimeout(r, 10));
        return 'ok';
    };
    s.cdu.talk(denise, 'one');
    s.cdu.talk({ id: 'p-justin', name: 'Justin' }, 'two', 'answer');
    await s.cdu.idle();
    assert.equal(order.length, 2);
    assert.match(order[0], /Denise.*speaks to you:\n\none/s);
    assert.match(order[1], /Justin.*answers you:\n\ntwo/s);
});

test('a fall-over is a failed turn, and what it should have heard waits for the next one', async () => {
    const s = await setup();
    s.mind.next = async (repo) => {
        writeFileSync(path.join(repo, 'a.txt'), 'a');
        await git(repo, 'add', '-A');
        await must('git', ['-C', repo, 'commit', '-q', '-m', 'feat: a']);
        return 'ok';
    };
    await done(s.cdu, s.cdu.talk(denise, 'make a'));
    await s.cdu.tideOut();
    s.github.judge(1, 'CLOSED');
    await s.cdu.watch();
    s.mind.next = async () => {
        throw new Error('bash temp file vanished');
    };
    const failed = await done(s.cdu, s.cdu.talk(denise, 'are you there'));
    assert.equal(failed.status, 'failed');
    assert.match(failed.error ?? '', /temp file/);
    s.mind.next = async () => 'here';
    await done(s.cdu, s.cdu.talk(denise, 'now?'));
    assert.match(s.mind.heard.at(-1) ?? '', /While you slept:\n(- .*\n)*- The One closed PR #1 "feat: a" without merging\. It's kept as drowned\/2026-11-08-pr1/);
    assert.match(s.mind.heard.at(-1) ?? '', /Your trunk went out with the tide: PR #1/);
});

test('a verdict is news once, journaled, and the trunk is master again', async () => {
    const s = await setup();
    s.mind.next = async (repo) => {
        writeFileSync(path.join(repo, 'a.txt'), 'a');
        await git(repo, 'add', '-A');
        await must('git', ['-C', repo, 'commit', '-q', '-m', 'feat: a']);
        return 'ok';
    };
    await done(s.cdu, s.cdu.talk(denise, 'make a'));
    await s.cdu.tideOut();
    await s.merge('cdu/tide');
    s.github.judge(1, 'MERGED');
    await s.cdu.watch();
    await s.cdu.watch();
    const verdicts = s.cdu.news.after(0).filter((n) => n.kind === 'merged');
    assert.equal(verdicts.length, 1);
    assert.equal(verdicts[0].data?.number, 1);
    assert.match(await git(s.book.dir, 'log', '-1', '--format=%s'), /^journal: The One merged PR #1/);
    assert.deepEqual(await s.tide.ahead(), []);
});

test('checks are told once per head commit', async () => {
    const s = await setup();
    s.mind.next = async (repo) => {
        writeFileSync(path.join(repo, `${Date.now()}.txt`), 'a');
        await git(repo, 'add', '-A');
        await must('git', ['-C', repo, 'commit', '-q', '-m', 'feat: a']);
        return 'ok';
    };
    await done(s.cdu, s.cdu.talk(denise, 'make a'));
    await s.cdu.tideOut();
    const pr = s.github.prs.get(1);
    assert.ok(pr);
    await s.cdu.watch();
    Object.assign(pr, { checks: 'failing', head: 'h1' });
    await s.cdu.watch();
    await s.cdu.watch();
    Object.assign(pr, { checks: 'passing', head: 'h2' });
    await s.cdu.watch();
    assert.deepEqual(s.cdu.news.after(0).filter((n) => n.kind === 'checks').map((n) => n.text), ['failing', 'passing']);
});

test('the tide coming in tells a conflict, and takes master\'s soul', async () => {
    const s = await setup();
    s.mind.next = async (repo) => {
        writeFileSync(path.join(repo, 'world.txt'), 'mine\n');
        await git(repo, 'add', '-A');
        await must('git', ['-C', repo, 'commit', '-q', '-m', 'feat: mine']);
        return 'ok';
    };
    await done(s.cdu, s.cdu.talk(denise, 'change the world'));
    await s.land('world.txt', 'theirs\n');
    await s.land('tools/cdu/soul/APPEND_SYSTEM.md', 'soul two\n');
    const result = await s.cdu.tideIn();
    assert.equal(result.kind, 'conflict');
    assert.ok(s.cdu.news.after(0).some((n) => n.kind === 'conflict'));
    assert.deepEqual(s.mind.souls, ['soul two\n']);
});

test('state survives a restart; a turn cut off by it is failed, not lost', async () => {
    const s = await setup();
    const statePath = path.join(s.root, 'state.json');
    const store = new Store(statePath);
    store.state.turns.push({ id: 't1', from: denise, text: 'hi', kind: 'talk', status: 'working', at: 1, commits: [] });
    store.state.pr = 7;
    store.save();
    const again = s.make(new Store(statePath));
    assert.equal(again.turn('t1')?.status, 'failed');
    assert.equal(new Store(statePath).state.pr, 7);
});

test('a question is in the last paragraph', () => {
    assert.equal(asks('Did it.\n\nWant more?'), true);
    assert.equal(asks('Why? Because.\n\nDone.'), false);
    assert.equal(asks(''), false);
});

test('compose without notes is just who speaks and what they say', () => {
    const turn: Turn = { id: 'x', from: denise, text: 'hello', kind: 'talk', status: 'queued', at: 0, commits: [] };
    assert.equal(compose(turn, []), 'Denise, a Cytheurgist, speaks to you:\n\nhello');
    assert.equal(emailOf({ id: 'x', name: 'Anne-Marie de Vries' }), 'anne.marie.de.vries@cytheurgists.proompting.party');
});
