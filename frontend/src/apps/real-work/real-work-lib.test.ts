import { describe, expect, it } from 'vitest';
import type {
    CompanySummary,
    SessionSummary,
    TradeSummary,
} from '../../api/model';
import {
    brierLabel,
    credits,
    homeworkLabel,
    refusal,
    returnLabel,
    sessionLabel,
    tradeLabel,
} from './real-work-lib';

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

const company = (capital: number, value: number) =>
    ({ capital, value }) as CompanySummary;

describe('credits', () => {
    it('rounds to whole credits, numbers and strings alike', () => {
        expect(credits(12345.6)).toBe('12,346');
        expect(credits('1000')).toBe('1,000');
    });
});

describe('returnLabel', () => {
    it('signs the change against the capital', () => {
        expect(returnLabel(company(10000, 10310))).toBe('+3.1%');
        expect(returnLabel(company(10000, 9500))).toBe('−5.0%');
    });

    it('has nothing to say without capital', () => {
        expect(returnLabel(company(0, 0))).toBe('—');
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
