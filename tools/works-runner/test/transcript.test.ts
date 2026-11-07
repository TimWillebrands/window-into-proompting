import assert from 'node:assert/strict';
import { test } from 'node:test';
import { Transcript } from '../src/transcript.ts';

const say = (text: string) => ({ sessionUpdate: 'agent_message_chunk', content: { type: 'text', text } });
const think = (text: string) => ({ sessionUpdate: 'agent_thought_chunk', content: { type: 'text', text } });

test('joins chunks into lines and keeps the whole message', () => {
    const t = new Transcript();
    assert.deepEqual(t.feed(say('hel')), []);
    assert.deepEqual(t.feed(say('lo\nwor')), [{ kind: 'say', text: 'hello' }]);
    assert.deepEqual(t.flush(), [{ kind: 'say', text: 'wor' }]);
    assert.equal(t.lastMessage, 'hello\nwor');
});

test('a new turn replaces the report', () => {
    const t = new Transcript();
    t.feed(say('old'));
    t.startTurn();
    t.feed(say('new'));
    assert.equal(t.lastMessage, 'new');
});

test('thoughts are lines but not the report', () => {
    const t = new Transcript();
    t.feed(think('hmm\n'));
    t.feed(say('done'));
    assert.equal(t.lastMessage, 'done');
});

test('announces a tool once, when it knows its target', () => {
    const t = new Transcript();
    const pending = { sessionUpdate: 'tool_call', toolCallId: 'a', title: 'write', status: 'pending', locations: [] };
    const running = { sessionUpdate: 'tool_call_update', toolCallId: 'a', title: 'write', status: 'in_progress', locations: [{ path: '/work/note.txt' }] };
    assert.deepEqual(t.feed(pending), []);
    assert.deepEqual(t.feed(running), [{ kind: 'tool', text: 'write note.txt' }]);
    assert.deepEqual(t.feed(running), []);
});

test('a failed tool is a failed line, and mid-line text comes out first', () => {
    const t = new Transcript();
    t.feed(say('trying'));
    const lines = t.feed({ sessionUpdate: 'tool_call_update', toolCallId: 'b', title: 'bash', status: 'failed', rawInput: { command: 'dotnet test' } });
    assert.deepEqual(lines, [{ kind: 'say', text: 'trying' }, { kind: 'failed', text: 'bash dotnet test' }]);
});
