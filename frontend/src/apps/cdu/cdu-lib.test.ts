import { describe, expect, it } from 'vitest';
import type { CduExchangeSummary } from '../../api/model';
import {
    allowanceLine,
    exchangeWord,
    linePrefix,
    mergeLines,
    money,
    prLine,
    shortSha,
} from './cdu-lib';

const exchange = (over: Partial<CduExchangeSummary>): CduExchangeSummary => ({
    turnId: 't',
    speakerId: 'p',
    speakerName: 'Denise',
    kind: 'talk',
    text: 'hi',
    status: 'done',
    reply: 'ok',
    asked: false,
    answered: false,
    commits: 0,
    at: 0,
    doneAt: 1,
    ...over,
});

describe('mergeLines', () => {
    it('adds only lines it has not seen, in order', () => {
        const held = [{ n: 1, at: 0, kind: 'say', text: 'a' }];
        const merged = mergeLines(held, [
            { n: 3, at: 0, kind: 'tool', text: 'c' },
            { n: 1, at: 0, kind: 'say', text: 'a' },
            { n: 2, at: 0, kind: 'say', text: 'b' },
        ]);
        expect(merged.map((l) => l.n)).toEqual([1, 2, 3]);
    });

    it('keeps the same array when nothing is new', () => {
        const held = [{ n: 1, at: 0, kind: 'say', text: 'a' }];
        expect(mergeLines(held, held)).toBe(held);
    });
});

describe('words', () => {
    it('prefixes lines by kind', () => {
        expect(linePrefix('heard')).toBe('>');
        expect(linePrefix('say')).toBe('cdu>');
        expect(linePrefix('anything')).toBe('  ');
    });

    it('shows small money with more digits', () => {
        expect(money(0.0123)).toBe('$0.0123');
        expect(money(3)).toBe('$3.00');
    });

    it('reads the allowance as what is left', () => {
        expect(allowanceLine({ usd: 1.25, capUsd: 5 })).toBe(
            '$3.75 of $5.00 left today',
        );
        expect(allowanceLine(null)).toBe('allowance unreadable');
    });

    it('puts a PR in a few words', () => {
        expect(
            prLine({
                number: 3,
                url: 'u',
                title: 't',
                state: 'OPEN',
                checks: 'failing',
            }),
        ).toBe('#3 open, checks fail');
        expect(
            prLine({
                number: 4,
                url: 'u',
                title: 't',
                state: 'MERGED',
                checks: 'none',
            }),
        ).toBe('#4 merged, no checks yet');
    });

    it('says where an exchange stands', () => {
        expect(exchangeWord(exchange({ status: 'queued' }))).toBe(
            'waiting for it',
        );
        expect(exchangeWord(exchange({ status: 'failed' }))).toBe(
            'it fell silent',
        );
        expect(exchangeWord(exchange({ asked: true, commits: 2 }))).toBe(
            'it asked back, 2 commits',
        );
        expect(
            exchangeWord(exchange({ asked: true, answered: true, commits: 1 })),
        ).toBe('answered, 1 commit');
    });

    it('shortens a sha', () => {
        expect(shortSha('9f92336abcdef')).toBe('9f92336');
    });
});
