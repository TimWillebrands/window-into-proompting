import { describe, expect, it } from 'vitest';
import type {
    CompanySummary,
    JobSummary,
    Place,
    PositionSummary,
    PracticeSummary,
    SessionSummary,
    TradeSummary,
    WatchSummary,
} from '../../api/model';
import {
    bookReturn,
    brierLabel,
    credits,
    dayMove,
    desireLabel,
    doorOf,
    employmentOf,
    homeworkLabel,
    jobLine,
    placeFrom,
    positionReturn,
    priceLabel,
    refusal,
    sessionLabel,
    sessionsInOrder,
    shiftLabel,
    signedPercent,
    toneOf,
    tradeLabel,
    workplacesOf,
} from './jobs-lib';

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
    it('reads a promise as who it was made to and whether it was kept', () => {
        expect(
            desireLabel(
                {
                    kind: 'Being',
                    text: '"tot morgen"',
                    source: 'Promise',
                    targetId: 'b',
                    status: 'Faded',
                },
                names,
            ),
        ).toBe('promised Bo: "tot morgen" (lapsed)');
    });
});

const trade = (over: Partial<TradeSummary>): TradeSummary => ({
    id: 't',
    at: 0,
    traderId: null,
    traderName: null,
    instrumentId: 'i',
    title: 'Will it rain?',
    side: 'Buy',
    units: 2000,
    price: 0.5,
    fee: 1,
    realized: null,
    thesis: null,
    forecast: null,
    brier: null,
    ...over,
});

const session = (over: Partial<SessionSummary>): SessionSummary => ({
    id: 's',
    practitionerId: 'p',
    practitionerName: 'Bo',
    clientId: 'c',
    clientName: 'Ana',
    workplace: 'the office',
    reason: 'stuck',
    startsAt: 0,
    startedAt: null,
    endedAt: null,
    status: 'Done',
    fee: 40,
    paid: true,
    homework: null,
    homeworkKept: null,
    ...over,
});

const book = (capital: number, value: number) =>
    ({ capital, value }) as CompanySummary;

describe('credits', () => {
    it('rounds to whole credits, numbers and strings alike', () => {
        expect(credits(12345.6)).toBe('12,346');
        expect(credits('1000')).toBe('1,000');
    });
});

describe('bookReturn', () => {
    it('is the change against the capital', () => {
        expect(bookReturn(book(10000, 10310))).toBeCloseTo(0.031);
        expect(bookReturn(book(10000, 9500))).toBeCloseTo(-0.05);
    });

    it('has nothing to say without capital', () => {
        expect(bookReturn(book(0, 0))).toBe(0);
    });
});

describe('signedPercent', () => {
    it('signs one decimal', () => {
        expect(signedPercent(0.031)).toBe('+3.1%');
        expect(signedPercent(-0.05)).toBe('−5.0%');
    });
});

describe('toneOf', () => {
    it('follows what the rounded number shows', () => {
        expect(toneOf(0.02)).toBe('up');
        expect(toneOf(-0.02)).toBe('down');
        expect(toneOf(0.0001)).toBe('flat');
    });
});

describe('positionReturn', () => {
    it('is what it made on what it cost', () => {
        const p = { value: 1500, cost: 1000 } as PositionSummary;
        expect(positionReturn(p)).toBeCloseTo(0.5);
        expect(positionReturn({ value: 0, cost: 0 } as PositionSummary)).toBe(
            0,
        );
    });
});

describe('priceLabel', () => {
    it('shows cents on questions and thousands on coins', () => {
        expect(priceLabel(0.413)).toBe('0.41');
        expect(priceLabel(71519.3)).toBe('71,519');
    });
});

describe('dayMove', () => {
    const watch = (previous: number | null) =>
        ({ bid: 0.5, ask: 0.6, previous }) as WatchSummary;

    it('compares the middle of the quote with where it stood', () => {
        expect(dayMove(watch(0.5))).toBeCloseTo(0.1);
    });

    it('is unknown when the Market gave no earlier price', () => {
        expect(dayMove(watch(null))).toBeNull();
        expect(dayMove(watch(0))).toBeNull();
    });
});

describe('brierLabel', () => {
    it('stays empty until something settled', () => {
        expect(brierLabel(null)).toBe('—');
        expect(brierLabel(0)).toBe('0.000');
        expect(brierLabel('0.0912')).toBe('0.091');
    });
});

describe('tradeLabel', () => {
    it('says what was done at what price', () => {
        expect(tradeLabel(trade({}))).toBe('Bought 1,000 at 0.50');
    });

    it('adds what a sale made or lost', () => {
        expect(tradeLabel(trade({ side: 'Sell', realized: 120 }))).toBe(
            'Sold 1,000 at 0.50, +120',
        );
        expect(tradeLabel(trade({ side: 'Sell', realized: -120 }))).toBe(
            'Sold 1,000 at 0.50, −120',
        );
    });

    it('tells a Settlement apart from a choice', () => {
        expect(
            tradeLabel(trade({ side: 'Settled', price: 1, realized: 580 })),
        ).toBe('Settled at 1.00, +580');
    });

    it('drops pennies on big prices', () => {
        expect(tradeLabel(trade({ price: 71500, units: 0.1 }))).toBe(
            'Bought 7,150 at 71500',
        );
    });
});

describe('sessionLabel', () => {
    it('follows the status', () => {
        expect(sessionLabel(session({ status: 'Booked' }))).toBe('booked');
        expect(sessionLabel(session({ status: 'Running' }))).toBe('in session');
        expect(sessionLabel(session({ status: 'Missed' }))).toBe('missed');
    });

    it('says whether a Session that happened got paid', () => {
        expect(sessionLabel(session({}))).toBe('done, 40 credits');
        expect(sessionLabel(session({ paid: false }))).toBe('done, unpaid');
    });
});

describe('refusal', () => {
    it('prefers the reason the backend gave', () => {
        expect(refusal({ title: 'Bad Request', detail: 'Too poor.' })).toBe(
            'Too poor.',
        );
    });

    it('takes off the quotes a JSON string body keeps', () => {
        expect(refusal({ detail: '"Ana can\'t afford 40 credits."' })).toBe(
            "Ana can't afford 40 credits.",
        );
    });

    it('falls back when nothing was said', () => {
        expect(refusal({ title: 'Conflict' })).toBe('Conflict');
        expect(refusal({})).toBe("That didn't work.");
    });
});

describe('homeworkLabel', () => {
    it('is empty when nothing was agreed', () => {
        expect(homeworkLabel(session({}))).toBe('');
    });

    it('shows the Homework and then how it went', () => {
        const homework = 'call the recruiter';
        expect(homeworkLabel(session({ homework }))).toBe(homework);
        expect(homeworkLabel(session({ homework, homeworkKept: true }))).toBe(
            'call the recruiter (done)',
        );
        expect(homeworkLabel(session({ homework, homeworkKept: false }))).toBe(
            'call the recruiter (not done)',
        );
    });
});

describe('placeFrom', () => {
    it('keeps a real place', () => {
        expect(placeFrom('person', 'a')).toEqual({ page: 'person', id: 'a' });
        expect(placeFrom('company', 'a')).toEqual({ page: 'company', id: 'a' });
        expect(placeFrom('practices')).toEqual({ page: 'practices' });
        expect(placeFrom('doors')).toEqual({ page: 'doors' });
    });

    it('falls back to the front page', () => {
        expect(placeFrom()).toEqual({ page: 'people' });
        expect(placeFrom('person')).toEqual({ page: 'people' });
        expect(placeFrom('nonsense', 'a')).toEqual({ page: 'people' });
    });
});

describe('employmentOf', () => {
    const polder = {
        id: 'c',
        name: 'Polder Capital',
        staff: [{ participantId: 'a', name: 'Ana', brier: null }],
    } as CompanySummary;
    const coach = { participantId: 'b', field: 'logistics' } as PracticeSummary;

    it('finds a Company through its staff', () => {
        expect(employmentOf(job({}), [polder], [coach]).kind).toBe('company');
    });

    it('finds a Practice by person', () => {
        const bo = job({ participantId: 'b' });
        expect(employmentOf(bo, [polder], [coach]).kind).toBe('practice');
    });

    it('tells a plain job from none', () => {
        const other = job({ participantId: 'x' });
        expect(employmentOf(other, [polder], [coach]).kind).toBe('job');
        expect(employmentOf({ ...other, title: null }, [], []).kind).toBe(
            'none',
        );
    });

    it('reads as one line', () => {
        const trader = job({ title: 'trader' });
        expect(jobLine(trader, { kind: 'company', company: polder })).toBe(
            'trader at Polder Capital',
        );
        expect(jobLine(job({}), { kind: 'job' })).toBe('barista at the Café');
        expect(
            jobLine(job({ title: 'coach' }), {
                kind: 'practice',
                practice: coach,
            }),
        ).toBe('coach, logistics');
        expect(jobLine({ ...job({}), title: null }, { kind: 'none' })).toBe(
            'No job',
        );
    });
});

describe('sessionsInOrder', () => {
    it('puts the next one first, then the latest of what is over', () => {
        const at = (id: string, startsAt: number, status: string) =>
            session({ id, startsAt, status });
        const ordered = sessionsInOrder([
            at('old', 1, 'Done'),
            at('later', 30, 'Booked'),
            at('recent', 2, 'Missed'),
            at('next', 20, 'Booked'),
        ]);
        expect(ordered.map((s) => s.id)).toEqual([
            'next',
            'later',
            'recent',
            'old',
        ]);
    });
});

describe('doors', () => {
    const place = (over: Partial<Place>): Place => ({
        id: 'p',
        name: 'p',
        kind: 'public',
        spots: [],
        ...over,
    });

    it('lists drawn workplaces by name, not empty rooms, homes or locales', () => {
        const works = place({ id: 'w', name: 'The Works', staff: ['j'] });
        const clinic = place({ id: 'c', name: 'a clinic', staff: ['l'] });
        expect(
            workplacesOf([
                works,
                place({ id: 'e', name: 'empty' }),
                place({ id: 'h', kind: 'home', staff: ['x'] }),
                place({ id: 'l', kind: 'locale', staff: ['x'] }),
                clinic,
            ]).map((p) => p.id),
        ).toEqual(['c', 'w']);
    });

    it('reads an unsettled door as open', () => {
        expect(doorOf(place({}))).toBe('open');
        expect(doorOf(place({ access: 'staff' }))).toBe('staff');
    });
});
