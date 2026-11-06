import { describe, expect, it } from 'vitest';
import type { Relation } from '../../api/model';
import { RelationKind } from '../../api/model';
import { inOrder, isCurrent, kindLabel, summary } from './relations-lib';

const married: Relation = { id: 'm', kind: RelationKind.Married };
const old: Relation = {
    id: 'o',
    kind: RelationKind.Together,
    story: 'you dated in high school',
    endedAt: 5,
};
const older: Relation = { id: 'x', kind: RelationKind.Friends, endedAt: 2 };

describe('relations-lib', () => {
    it('counts a relation with no end as current', () => {
        expect(isCurrent(married)).toBe(true);
        expect(isCurrent({ ...married, endedAt: null })).toBe(true);
        expect(isCurrent(old)).toBe(false);
    });

    it('lists current ones first, then newest ended', () => {
        expect(inOrder([older, old, married]).map((r) => r.id)).toEqual([
            'm',
            'o',
            'x',
        ]);
    });

    it('prefers the story over the kind and marks the past', () => {
        expect(summary(married)).toBe('Married');
        expect(summary(old)).toBe('you dated in high school (past)');
    });

    it('falls back when the kind is missing', () => {
        expect(kindLabel(undefined)).toBe('Other');
    });
});
