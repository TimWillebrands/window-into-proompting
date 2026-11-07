import assert from 'node:assert/strict';
import { test } from 'node:test';
import { describe, spoken, Transcript } from '../src/transcript.ts';

const say = (delta: string) => ({ type: 'message_update', assistantMessageEvent: { type: 'text_delta', delta } });
const think = (delta: string) => ({ type: 'message_update', assistantMessageEvent: { type: 'thinking_delta', delta } });

test('joins deltas into lines and flushes at the end of a message', () => {
    const t = new Transcript();
    assert.deepEqual(t.feed(say('hel')), []);
    assert.deepEqual(t.feed(say('lo\nwor')), [{ kind: 'say', text: 'hello' }]);
    assert.deepEqual(t.feed({ type: 'message_end' }), [{ kind: 'say', text: 'wor' }]);
});

test('a thought then speech come out as separate lines', () => {
    const t = new Transcript();
    t.feed(think('hmm'));
    assert.deepEqual(t.feed(say('done')), [{ kind: 'thought', text: 'hmm' }]);
    assert.deepEqual(t.flush(), [{ kind: 'say', text: 'done' }]);
});

test('a tool is announced once with its target, relative to the repo', () => {
    const t = new Transcript('/cdu/repo');
    t.feed(say('let me look'));
    assert.deepEqual(t.feed({ type: 'tool_execution_start', toolName: 'read', args: { path: '/cdu/repo/CONTEXT.md' } }), [
        { kind: 'say', text: 'let me look' },
        { kind: 'tool', text: 'read CONTEXT.md' },
    ]);
    assert.deepEqual(t.feed({ type: 'tool_execution_end', toolName: 'read', isError: false }), []);
});

test('failures and retries are said', () => {
    const t = new Transcript();
    assert.deepEqual(t.feed({ type: 'tool_execution_end', toolName: 'bash', isError: true }), [{ kind: 'failed', text: 'bash failed' }]);
    assert.deepEqual(t.feed({ type: 'auto_retry_start', errorMessage: '429' }), [{ kind: 'status', text: 'trying again: 429' }]);
});

test('describes a command, a path, or plain arguments', () => {
    assert.equal(describe('bash', { command: 'dotnet build' }), 'bash dotnet build');
    assert.equal(describe('mcp__postgres__execute_sql', { sql: 'select 1' }), 'mcp__postgres__execute_sql {"sql":"select 1"}');
    assert.equal(describe('ls', {}), 'ls');
});

test('inline thinking is a thought, and not part of what it said', () => {
    const t = new Transcript();
    const lines = [...t.feed(say('<think>\nwe are an assistant\nhmm</think>\n\nThe Forms move.\n')), ...t.flush()];
    assert.deepEqual(lines, [
        { kind: 'thought', text: 'we are an assistant' },
        { kind: 'thought', text: 'hmm' },
        { kind: 'say', text: 'The Forms move.' },
    ]);
    assert.equal(spoken('<think>\nplan\n</think>\n\nDone.'), 'Done.');
    assert.equal(spoken('<think>never closed'), '');
    assert.equal(spoken('Plain.'), 'Plain.');
});
