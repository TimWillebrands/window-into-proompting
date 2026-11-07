import assert from 'node:assert/strict';
import { existsSync, readFileSync, writeFileSync } from 'node:fs';
import path from 'node:path';
import { test } from 'node:test';
import { checksOf, prBody, Tide } from '../src/tide.ts';
import { FakeGitHub, git, world } from './fixture.ts';

async function setup() {
    const w = await world();
    const github = new FakeGitHub();
    const tide = new Tide({ repo: w.repo, origin: w.origin, github, now: () => Date.parse('2026-11-08T12:00:00Z') });
    await tide.ensure();
    const commit = async (who: string, file: string, text: string) => {
        await tide.author(who, `${who.toLowerCase()}@cytheurgists.proompting.party`);
        writeFileSync(path.join(w.repo, file), text);
        await git(w.repo, 'add', '-A');
        await (await import('../src/exec.ts')).must('git', ['-C', w.repo, 'commit', '-q', '-m', `feat: ${file}`]);
    };
    return { ...w, github, tide, commit };
}

test('a fresh clone has a trunk at master and nothing ahead', async () => {
    const { tide, repo } = await setup();
    assert.equal(await git(repo, 'rev-parse', '--abbrev-ref', 'HEAD'), 'trunk');
    assert.deepEqual(await tide.ahead(), []);
});

test('commits are authored by who asked and committed by the CDU', async () => {
    const { tide, repo, commit } = await setup();
    await commit('Denise', 'a.txt', 'a');
    const [c] = await tide.ahead();
    assert.equal(c.author, 'Denise');
    assert.equal(c.subject, 'feat: a.txt');
    assert.equal(await git(repo, 'log', '-1', '--format=%cn'), 'CDU');
});

test('the tide goes out as one PR, and later work updates it', async () => {
    const { tide, github, origin, commit } = await setup();
    assert.deepEqual(await tide.out(), { kind: 'calm' });
    await commit('Denise', 'a.txt', 'a');
    const first = await tide.out();
    assert.equal(first.kind, 'opened');
    assert.equal(tide.pr, 1);
    assert.equal(github.prs.get(1)?.title, 'feat: a.txt');
    assert.equal(await git(origin, 'rev-parse', 'cdu/tide'), await tide.head());

    await github.edit(1, `${github.prs.get(1)?.body}\n\nWritten by the CDU itself.`);
    await commit('Justin', 'b.txt', 'b');
    const second = await tide.out();
    assert.equal(second.kind, 'updated');
    assert.equal(github.prs.size, 1);
    const body = github.prs.get(1)?.body ?? '';
    assert.match(body, /feat: b\.txt \(asked by Justin\)/);
    assert.match(body, /Written by the CDU itself\./);
    assert.equal(await git(origin, 'rev-parse', 'cdu/tide'), await tide.head());
});

test('the tide coming in rebases the trunk on master', async () => {
    const { tide, land, repo, commit } = await setup();
    await commit('Denise', 'a.txt', 'a');
    await land('other.txt', 'from the One');
    assert.deepEqual(await tide.in(), { kind: 'rebased', ahead: 1 });
    assert.equal(readFileSync(path.join(repo, 'other.txt'), 'utf8'), 'from the One');
    assert.equal((await tide.ahead())[0].author, 'Denise');
});

test('a conflict leaves the trunk as it was', async () => {
    const { tide, land, repo, commit } = await setup();
    await commit('Denise', 'world.txt', 'denise was here\n');
    const before = await tide.head();
    await land('world.txt', 'the One was here\n');
    const result = await tide.in();
    assert.equal(result.kind, 'conflict');
    assert.equal(await tide.head(), before);
    assert.equal(existsSync(path.join(repo, '.git', 'rebase-merge')), false);
    assert.equal(await git(repo, 'rev-parse', '--abbrev-ref', 'HEAD'), 'trunk');
});

test('a merge resets the trunk to master', async () => {
    const { tide, github, merge, repo, commit } = await setup();
    await commit('Denise', 'a.txt', 'a');
    await tide.out();
    assert.equal(await tide.verdict(), null);
    await merge('cdu/tide');
    github.judge(1, 'MERGED');
    const verdict = await tide.verdict();
    assert.equal(verdict?.kind, 'merged');
    assert.equal(tide.pr, null);
    assert.equal(await tide.head(), await git(repo, 'rev-parse', 'origin/master'));
    assert.deepEqual(await tide.ahead(), []);
});

test('a closed PR is drowned: kept as a tag, the trunk reset, unsaved work stashed', async () => {
    const { tide, github, repo, commit } = await setup();
    await commit('Justin', 'a.txt', 'a');
    const head = await tide.head();
    await tide.out();
    writeFileSync(path.join(repo, 'half.txt'), 'half done');
    github.judge(1, 'CLOSED');
    const verdict = await tide.verdict();
    assert.equal(verdict?.tag, 'drowned/2026-11-08-pr1');
    assert.equal(await git(repo, 'rev-parse', 'drowned/2026-11-08-pr1'), head);
    assert.deepEqual(await tide.ahead(), []);
    assert.equal(existsSync(path.join(repo, 'half.txt')), false);
    assert.match(await git(repo, 'stash', 'list'), /before the trunk reset/);
});

test('the soul is read from master, not the trunk', async () => {
    const { tide, repo, commit } = await setup();
    await commit('Denise', 'tools/cdu/soul/APPEND_SYSTEM.md', 'soul two\n');
    assert.equal(readFileSync(path.join(repo, 'tools/cdu/soul/APPEND_SYSTEM.md'), 'utf8'), 'soul two\n');
    assert.equal(await tide.show('tools/cdu/soul/APPEND_SYSTEM.md'), 'soul one\n');
    assert.equal(await tide.show('nope.md'), null);
});

test('the PR body keeps what the CDU wrote around the commit list', () => {
    const c = { sha: 'abcdef1234', subject: 'fix: x', author: 'Denise' };
    const first = prBody([c]);
    assert.match(first, /abcdef1 fix: x \(asked by Denise\)/);
    const kept = prBody([c, { ...c, subject: 'fix: y' }], `Intro\n${first}\nOutro`);
    assert.match(kept, /^Intro\n/);
    assert.match(kept, /fix: y/);
    assert.match(kept, /Outro$/);
});

test('checks in one word', () => {
    assert.equal(checksOf([]), 'none');
    assert.equal(checksOf([{ status: 'COMPLETED', conclusion: 'SUCCESS' }, { state: 'SUCCESS' }]), 'passing');
    assert.equal(checksOf([{ status: 'IN_PROGRESS', conclusion: '' }, { status: 'COMPLETED', conclusion: 'SUCCESS' }]), 'pending');
    assert.equal(checksOf([{ status: 'IN_PROGRESS' }, { status: 'COMPLETED', conclusion: 'FAILURE' }]), 'failing');
    assert.equal(checksOf([{ state: 'PENDING' }]), 'pending');
});
