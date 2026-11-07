import assert from 'node:assert/strict';
import type { AddressInfo } from 'node:net';
import path from 'node:path';
import { after, before, test } from 'node:test';
import { createApi } from '../src/api.ts';
import { Book } from '../src/book.ts';
import { Cdu, Store } from '../src/cdu.ts';
import { Feed } from '../src/feed.ts';
import { Tide } from '../src/tide.ts';
import { FakeGitHub, world } from './fixture.ts';

let server: ReturnType<typeof createApi>;
let cdu: Cdu;
const url = (p: string) => `http://127.0.0.1:${(server.address() as AddressInfo).port}${p}`;
const call = (p: string, init: RequestInit = {}, token = 'tok') =>
    fetch(url(p), { ...init, headers: { authorization: `Bearer ${token}`, 'content-type': 'application/json' } });

before(async () => {
    const w = await world();
    const tide = new Tide({ repo: w.repo, origin: w.origin, github: new FakeGitHub() });
    await tide.ensure();
    const book = new Book(path.join(w.root, 'book'), path.join(import.meta.dirname, '..', 'book'));
    await book.ensure();
    const mind = { model: 'fake', think: async () => 'Heard you.\n\nWhat next?' };
    cdu = new Cdu({ mind, tide, book, store: new Store(), lines: new Feed(), news: new Feed() });
    server = createApi(cdu, 'tok');
    await new Promise<void>((done) => server.listen(0, '127.0.0.1', done));
});

after(() => server.close());

test('wants the token', async () => {
    assert.equal((await call('/status', {}, 'nope')).status, 401);
});

test('talk queues a turn, and the turn and its news can be read back', async () => {
    const bad = await call('/talk', { method: 'POST', body: JSON.stringify({ text: 'hi' }) });
    assert.equal(bad.status, 400);
    const answer = await call('/talk', { method: 'POST', body: JSON.stringify({ from: { id: 'p1', name: 'Denise' }, text: 'hi', kind: 'answer' }) });
    assert.equal(answer.status, 202);
    const turn = await answer.json();
    assert.equal(turn.kind, 'answer');
    await cdu.idle();
    const read = await (await call(`/turns/${turn.id}`)).json();
    assert.deepEqual([read.status, read.reply, read.asked], ['done', 'Heard you.\n\nWhat next?', true]);
    const news = await (await call('/news?after=0')).json();
    assert.equal(news[0].kind, 'replied');
    const lines = await (await call(`/lines?after=0`)).json();
    assert.equal(lines[0].kind, 'heard');
    assert.equal((await (await call(`/lines?after=${lines[0].n}`)).json()).length, 0);
    assert.equal((await call('/turns/nope')).status, 404);
});

test('status says who it is and what the trunk holds', async () => {
    const status = await (await call('/status')).json();
    assert.equal(status.model, 'fake');
    assert.equal(status.awake, false);
    assert.deepEqual(status.trunk.ahead, []);
    assert.equal(status.last.reply, 'Heard you.\n\nWhat next?');
});

test('the tide is queued, and what came of it is news', async () => {
    assert.equal((await call('/tide/out', { method: 'POST' })).status, 202);
    assert.equal((await call('/tide/in', { method: 'POST' })).status, 202);
    await cdu.idle();
    assert.ok((await (await call('/news?after=0')).json()).some((n: { kind: string }) => n.kind === 'rebased'));
});
