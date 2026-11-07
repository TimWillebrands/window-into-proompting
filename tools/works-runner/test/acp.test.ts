import assert from 'node:assert/strict';
import { PassThrough } from 'node:stream';
import { test } from 'node:test';
import { Acp } from '../src/acp.ts';

function pair() {
    const toAgent = new PassThrough();
    const fromAgent = new PassThrough();
    const updates: Record<string, unknown>[] = [];
    const acp = new Acp(toAgent, fromAgent, (u) => updates.push(u));
    const sent: Record<string, unknown>[] = [];
    toAgent.on('data', (chunk) => {
        for (const line of String(chunk).split('\n').filter(Boolean)) sent.push(JSON.parse(line));
    });
    const reply = (message: unknown) => fromAgent.write(`${JSON.stringify(message)}\n`);
    return { acp, sent, reply, updates, fromAgent };
}

const tick = () => new Promise((done) => setTimeout(done, 5));

test('matches replies to requests and streams updates between them', async () => {
    const { acp, reply, updates, sent } = pair();
    const session = acp.newSession('/work');
    await tick();
    reply({ jsonrpc: '2.0', method: 'session/update', params: { update: { sessionUpdate: 'x' } } });
    reply({ jsonrpc: '2.0', id: sent[0].id, result: { sessionId: 's1' } });
    assert.equal(await session, 's1');
    assert.deepEqual(updates, [{ sessionUpdate: 'x' }]);
});

test('allows what the agent asks, since the sandbox is the boundary', async () => {
    const { reply, sent } = pair();
    reply({ jsonrpc: '2.0', id: 9, method: 'session/request_permission', params: { options: [{ optionId: 'no', kind: 'reject_once' }, { optionId: 'yes', kind: 'allow_once' }] } });
    await tick();
    assert.deepEqual((sent[0].result as { outcome: unknown }).outcome, { outcome: 'selected', optionId: 'yes' });
});

test('turns down client methods it never offered', async () => {
    const { reply, sent } = pair();
    reply({ jsonrpc: '2.0', id: 10, method: 'fs/write_text_file', params: {} });
    await tick();
    assert.equal((sent[0].error as { code: number }).code, -32601);
});

test('rejects what is in flight when the agent exits', async () => {
    const { acp, fromAgent } = pair();
    const turn = acp.prompt('s1', 'go');
    fromAgent.end();
    await assert.rejects(turn, /agent exited/);
    await acp.closed;
});
