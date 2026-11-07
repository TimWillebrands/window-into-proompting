import { describe, expect, it } from 'vitest';
import type { CommissionSummary, WorksLineSummary } from '../../api/model';
import {
    factsLine,
    isOpen,
    linePrefix,
    mergeCommand,
    mergeLines,
    money,
    pickDefault,
    statusWord,
} from './works-lib';

const line = (n: number, text = `line ${n}`): WorksLineSummary => ({
    n,
    at: n,
    kind: 'say',
    text,
});

const commission = (over: Partial<CommissionSummary> = {}): CommissionSummary =>
    ({
        id: 'c1',
        kind: 'Change',
        status: 'Waiting',
        model: 'free-one',
        budgetUsd: 2,
        spentUsd: 0.5,
        files: 3,
        added: 10,
        removed: 2,
        ...over,
    }) as CommissionSummary;

describe('mergeLines', () => {
    it('keeps order and drops what is already held', () => {
        const merged = mergeLines([line(1), line(3)], [line(3), line(2)]);
        expect(merged.map((l) => l.n)).toEqual([1, 2, 3]);
    });

    it('returns the same array when nothing is new, so React skips a render', () => {
        const held = [line(1)];
        expect(mergeLines(held, [line(1)])).toBe(held);
    });
});

describe('words', () => {
    it('gives a prompt per line kind, and a blank for unknown ones', () => {
        expect(linePrefix('tool')).toBe('$');
        expect(linePrefix('new-kind')).toBe('  ');
    });

    it('falls back to the raw status', () => {
        expect(statusWord('Proposed')).toBe('with the world-spirits');
        expect(statusWord('Odd')).toBe('Odd');
    });

    it('shows small spends in full', () => {
        expect(money(0.0123)).toBe('$0.0123');
        expect(money(12)).toBe('$12.00');
        expect(money(0)).toBe('$0.00');
    });
});

describe('factsLine', () => {
    it('reports spend, builder and what changed', () => {
        expect(factsLine(commission())).toBe(
            '$0.5000 of $2.00 · free-one · 3 files, +10 -2',
        );
    });

    it('leaves code out of a Survey', () => {
        expect(factsLine(commission({ kind: 'Survey' }))).not.toContain(
            'files',
        );
    });
});

describe('pickDefault', () => {
    it('prefers what is open, then the newest', () => {
        const done = commission({ id: 'a', status: 'Dropped' });
        const open = commission({ id: 'b', status: 'Working' });
        expect(pickDefault([done, open])).toBe('b');
        expect(pickDefault([done])).toBe('a');
        expect(pickDefault([])).toBeNull();
    });

    it('treats a Proposition as finished work', () => {
        expect(isOpen(commission({ status: 'Proposed' }))).toBe(false);
    });
});

describe('mergeCommand', () => {
    it('pipes the patch into git am', () => {
        expect(mergeCommand('https://x', 'p', 'c')).toBe(
            'curl -s https://x/api/worlds/p/works/commissions/c/patch | git am',
        );
    });
});
