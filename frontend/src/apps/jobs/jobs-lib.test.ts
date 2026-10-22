import { describe, expect, it } from 'vitest';
import type { JobSummary } from '../../api/model';
import { desireLabel, shiftLabel } from './jobs-lib';

const now = new Date(2026, 9, 22, 12, 0);
const job = (over: Partial<JobSummary>): JobSummary => ({
    participantId: 'a',
    name: 'Ana',
    seeded: true,
    title: 'barista',
    workplace: 'the Café',
    onscreen: true,
    days: 'Mon–Fri',
    hours: '09:00-17:00',
    shiftDay: '2026-10-22',
    shiftStatus: 'On',
    lateMinutes: 0,
    paid: 0,
    ...over,
});

describe('shiftLabel', () => {
    it('says how today went', () => {
        expect(shiftLabel(job({}), now)).toBe('at work');
        expect(shiftLabel(job({ lateMinutes: 12 }), now)).toBe(
            'at work (12 min late)',
        );
        expect(shiftLabel(job({ shiftStatus: 'Done', paid: 70 }), now)).toBe(
            'done, +70',
        );
        expect(shiftLabel(job({ shiftStatus: 'Missed' }), now)).toBe('missed');
    });

    it('is quiet about other days and the jobless', () => {
        expect(shiftLabel(job({ shiftDay: '2026-10-21' }), now)).toBe('');
        expect(shiftLabel(job({ title: null }), now)).toBe('');
    });
});

describe('desireLabel', () => {
    const names = (id: string) => (id === 'b' ? 'Bo' : 'someone');
    it('names the kind, the source and how it went', () => {
        expect(
            desireLabel(
                {
                    kind: 'Being',
                    text: 'learn to skate',
                    source: 'Mimesis',
                    fromId: 'b',
                    status: 'Active',
                },
                names,
            ),
        ).toBe('to become: learn to skate (from Bo)');
        expect(
            desireLabel(
                {
                    kind: 'Having',
                    text: 'a navy hoodie',
                    source: 'Sheet',
                    status: 'Fulfilled',
                },
                names,
            ),
        ).toBe('to have: a navy hoodie (who they are, got it)');
    });
});
