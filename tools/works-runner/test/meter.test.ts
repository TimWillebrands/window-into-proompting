import assert from 'node:assert/strict';
import http from 'node:http';
import type { AddressInfo } from 'node:net';
import { after, before, test } from 'node:test';
import { costOf, createMeter, type Model, outputCap, type Tab, usageIn } from '../src/meter.ts';

const model: Model = { id: 'cheap', label: 'cheap', in: 1, out: 2, context: 1000, output: 100 };
const port = (server: http.Server) => (server.address() as AddressInfo).port;
const seen: { auth?: string; body?: Record<string, unknown> } = {};

const upstream = http.createServer(async (req, res) => {
    let raw = '';
    for await (const chunk of req) raw += chunk;
    seen.auth = req.headers.authorization;
    seen.body = JSON.parse(raw);
    if (seen.body?.stream) {
        res.writeHead(200, { 'content-type': 'text/event-stream' });
        res.write('data: {"choices":[{"delta":{"content":"hi"}}]}\n\n');
        res.end('data: {"choices":[],"usage":{"prompt_tokens":1000000,"completion_tokens":0,"total_tokens":1000000}}\n\ndata: [DONE]\n\n');
        return;
    }
    res.writeHead(200, { 'content-type': 'application/json' });
    res.end(JSON.stringify({ usage: { prompt_tokens: 10, completion_tokens: 5, total_tokens: 15, cost: 0.5 } }));
});

let meter: ReturnType<typeof createMeter>;

before(async () => {
    await new Promise<void>((done) => upstream.listen(0, '127.0.0.1', done));
    meter = createMeter({ upstream: `http://127.0.0.1:${port(upstream)}/v1`, key: 'real-key', secret: 's3', models: [model] });
    await new Promise<void>((done) => meter.proxy.listen(0, '127.0.0.1', done));
    await new Promise<void>((done) => meter.admin.listen(0, '127.0.0.1', done));
});

after(() => {
    upstream.close();
    meter.proxy.close();
    meter.admin.close();
});

const call = (token: string, body: unknown) =>
    fetch(`http://127.0.0.1:${port(meter.proxy)}/v1/chat/completions`, {
        method: 'POST',
        headers: { authorization: `Bearer ${token}`, 'content-type': 'application/json' },
        body: JSON.stringify(body),
    });

const open = (token: string, capUsd: number) =>
    fetch(`http://127.0.0.1:${port(meter.admin)}/tabs`, {
        method: 'POST',
        headers: { 'x-meter-secret': 's3' },
        body: JSON.stringify({ token, run: 'r', capUsd, capTokens: 1e9 }),
    });

test('prefers the upstream cost, falls back to list price', () => {
    assert.equal(costOf({ cost: 0.25, prompt_tokens: 1e6 }, model), 0.25);
    assert.equal(costOf({ prompt_tokens: 1e6, completion_tokens: 1e6 }, model), 3);
});

test('finds usage in an SSE line and ignores the rest', () => {
    assert.deepEqual(usageIn('data: {"usage":{"total_tokens":3}}'), { total_tokens: 3 });
    assert.equal(usageIn('data: {"choices":[]}'), null);
    assert.equal(usageIn('data: [DONE]'), null);
});

test('refuses unknown runs and unknown models', async () => {
    assert.equal((await call('nobody', { model: 'cheap' })).status, 401);
    await open('t-model', 1);
    assert.equal((await call('t-model', { model: 'costly' })).status, 403);
});

test('swaps in the real key and tallies a plain response', async () => {
    await open('t-plain', 1);
    assert.equal((await call('t-plain', { model: 'cheap' })).status, 200);
    assert.equal(seen.auth, 'Bearer real-key');
    assert.equal(meter.tabs.get('t-plain')?.usd, 0.5);
    assert.equal(meter.tabs.get('t-plain')?.tokens, 15);
});

test('tallies a stream, then stops the run at its cap', async () => {
    await open('t-stream', 0.9);
    const first = await call('t-stream', { model: 'cheap', stream: true });
    assert.match(await first.text(), /"content":"hi"/);
    assert.equal(meter.tabs.get('t-stream')?.usd, 1);
    assert.equal((await call('t-stream', { model: 'cheap', stream: true })).status, 402);
});

test('a raised cap lets the run go on', async () => {
    await open('t-raise', 0.4);
    await call('t-raise', { model: 'cheap' });
    assert.equal((await call('t-raise', { model: 'cheap' })).status, 402);
    await fetch(`http://127.0.0.1:${port(meter.admin)}/tabs/t-raise/raise`, { method: 'POST', headers: { 'x-meter-secret': 's3' }, body: '{"addUsd":1}' });
    assert.equal((await call('t-raise', { model: 'cheap' })).status, 200);
});

test('the admin port wants the secret', async () => {
    const answer = await fetch(`http://127.0.0.1:${port(meter.admin)}/tabs/t-plain`);
    assert.equal(answer.status, 401);
});

test('only chat completions pass', async () => {
    const answer = await fetch(`http://127.0.0.1:${port(meter.proxy)}/v1/embeddings`, { method: 'POST' });
    assert.equal(answer.status, 404);
});

const tabOf = (over: Partial<Tab>): Tab => ({ token: 't', run: 'r', capUsd: 1, capTokens: 1e9, usd: 0, tokens: 0, calls: 0, ...over });

test('a call may only use what is left, by money and by tokens', () => {
    assert.equal(outputCap(tabOf({ usd: 0.5 }), model), 100);
    assert.equal(outputCap(tabOf({ capUsd: 1, usd: 0.99998 }), model), 10);
    assert.equal(outputCap(tabOf({ capTokens: 1000, tokens: 990 }), { ...model, out: 0 }), 10);
    assert.equal(outputCap(tabOf({ usd: 2 }), model), 1);
});

test('the cap reaches the upstream request', async () => {
    await open('t-clamp', 0.00002);
    await call('t-clamp', { model: 'cheap', max_tokens: 5000 });
    assert.equal(seen.body?.max_tokens, 10);
    await open('t-default', 1);
    await call('t-default', { model: 'cheap' });
    assert.equal(seen.body?.max_tokens, model.output);
});

test('a tab reopened after a restart starts from what was spent', async () => {
    await fetch(`http://127.0.0.1:${port(meter.admin)}/tabs`, {
        method: 'POST',
        headers: { 'x-meter-secret': 's3' },
        body: JSON.stringify({ token: 't-seed', run: 'r', capUsd: 1, capTokens: 1e9, usd: 1, tokens: 7 }),
    });
    assert.equal((await call('t-seed', { model: 'cheap' })).status, 402);
});
