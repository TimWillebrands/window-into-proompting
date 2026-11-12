import type {
    CompanySummary,
    Desire,
    JobSummary,
    Place,
    PositionSummary,
    PracticeSummary,
    ProblemDetails,
    SessionSummary,
    SkillView,
    TradeSummary,
    WatchSummary,
} from '../../api/model';

const localDay = (d: Date) =>
    `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;

/** Where the window is; kept in window props so a refresh or a link lands on the same page. */
export type WorkPlace =
    | { page: 'people' }
    | { page: 'person'; id: string }
    | { page: 'companies' }
    | { page: 'company'; id: string }
    | { page: 'practices' }
    | { page: 'doors' };

/** Anything else that turns up in the URL means the front page. */
export function placeFrom(page?: string, id?: string): WorkPlace {
    if ((page === 'person' || page === 'company') && id) return { page, id };
    if (page === 'companies' || page === 'practices' || page === 'doors')
        return { page };
    return { page: 'people' };
}

/** What someone's Occupation amounts to, from the lists the window already holds. */
export type Employment =
    | { kind: 'none' }
    | { kind: 'job' }
    | { kind: 'company'; company: CompanySummary }
    | { kind: 'practice'; practice: PracticeSummary };

export function employmentOf(
    person: JobSummary,
    companies: CompanySummary[],
    practices: PracticeSummary[],
): Employment {
    const id = person.participantId;
    const company = companies.find((c) =>
        c.staff.some((s) => s.participantId === id),
    );
    if (company) return { kind: 'company', company };
    const practice = practices.find((p) => p.participantId === id);
    if (practice) return { kind: 'practice', practice };
    return person.title ? { kind: 'job' } : { kind: 'none' };
}

/** "trader at Polder Capital", in one line. */
export function jobLine(person: JobSummary, work: Employment): string {
    if (!person.title) return person.seeded ? 'No job' : 'Working it out…';
    if (work.kind === 'company')
        return `${person.title} at ${work.company.name}`;
    if (work.kind === 'practice')
        return `${person.title}, ${work.practice.field}`;
    return `${person.title} at ${person.workplace}`;
}

/** How today's shift went, in a word or two; empty when nothing happened today. */
export function shiftLabel(job: JobSummary, now = new Date()): string {
    if (!job.title || job.shiftDay !== localDay(now)) return '';
    const late = Number(job.lateMinutes);
    const paid = Number(job.paid);
    switch (job.shiftStatus) {
        case 'On':
            return late > 0 ? `at work (${late} min late)` : 'at work';
        case 'Done':
            return paid > 0 ? `done, +${paid}` : 'done';
        case 'LeftEarly':
            return paid > 0 ? `left early, +${paid}` : 'left early';
        case 'Missed':
            return 'missed';
        default:
            return '';
    }
}

/** "to become: get your sourdough right (from Bo, got it)": kind, text, where it came from, how it went. */
export function desireLabel(d: Desire, nameOf: (id: string) => string): string {
    if (d.source === 'Promise') return promiseLabel(d, nameOf);
    const kind = d.kind === 'Having' ? 'to have' : 'to become';
    const notes = [
        d.source === 'Mimesis' && d.fromId ? `from ${nameOf(d.fromId)}` : null,
        d.source === 'Sheet' ? 'who they are' : null,
        d.status === 'Fulfilled' ? 'got it' : null,
        d.status === 'Faded' ? 'faded' : null,
        d.status === 'Abandoned' ? 'dropped' : null,
    ].filter(Boolean);
    return `${kind}: ${d.text ?? ''}${notes.length ? ` (${notes.join(', ')})` : ''}`;
}

const SKILL_LEVELS: Record<SkillView['level'], string> = {
    New: 'new to it',
    Learning: 'getting the hang of it',
    Practised: 'practised',
    SecondNature: 'second nature',
};

const SKILL_SOURCES: Partial<Record<SkillView['source'], string>> = {
    Sheet: 'who they are',
    Work: 'from work',
    Feature: 'from doing it',
    Desire: 'wanted to',
};

/** 'fixing bikes (practised, 12 times, from work)': what they know how to do and how well (ADR 0054). */
export function skillLabel(s: SkillView): string {
    const times = Number(s.practice);
    const notes = [
        s.rusty ? 'rusty' : SKILL_LEVELS[s.level],
        !s.intrinsic && times > 0
            ? `${times} ${times === 1 ? 'time' : 'times'}`
            : null,
        SKILL_SOURCES[s.source] ?? null,
    ].filter(Boolean);
    return `${s.name} (${notes.join(', ')})`;
}

const PROMISE_OUTCOMES: Partial<Record<NonNullable<Desire['status']>, string>> =
    {
        Fulfilled: 'kept',
        Faded: 'lapsed',
        Abandoned: 'called off',
    };

/** 'promised Pieter: "tot morgen" (kept)': a promise is a desire to keep their word (ADR 0043). */
function promiseLabel(d: Desire, nameOf: (id: string) => string): string {
    const to = d.targetId ? nameOf(d.targetId) : 'someone';
    const how = d.status ? PROMISE_OUTCOMES[d.status] : undefined;
    return `promised ${to}: ${d.text ?? ''}${how ? ` (${how})` : ''}`;
}

/** The backend's own reason; a plain-string 400 can arrive still wearing its JSON quotes. */
export function refusal(error: ProblemDetails): string {
    const said = error.detail ?? error.title;
    return said ? said.replace(/^"|"$/g, '') : "That didn't work.";
}

/** "12,300": credits are whole numbers to look at, whatever the Book holds. */
export const credits = (value: number | string) =>
    Math.round(Number(value)).toLocaleString('en-GB');

/** "+3.1%" from a fraction. */
export const signedPercent = (fraction: number) =>
    `${fraction >= 0 ? '+' : '−'}${Math.abs(fraction * 100).toFixed(1)}%`;

export type Tone = 'up' | 'down' | 'flat';

/** Colour follows the rounded number, so "+0.0%" never reads as a gain. */
export function toneOf(fraction: number): Tone {
    if (Math.abs(fraction) < 0.0005) return 'flat';
    return fraction > 0 ? 'up' : 'down';
}

/** The whole Book against what it started with, as a fraction. */
export function bookReturn(company: CompanySummary): number {
    const capital = Number(company.capital);
    return capital > 0 ? Number(company.value) / capital - 1 : 0;
}

/** What a Position has made on what it cost, as a fraction. */
export function positionReturn(p: PositionSummary): number {
    const cost = Number(p.cost);
    return cost > 0 ? Number(p.value) / cost - 1 : 0;
}

/** A question trades in cents, a coin in thousands. */
export function priceLabel(price: number): string {
    return price < 10 ? price.toFixed(2) : credits(price);
}

export const midOf = (w: WatchSummary) => (Number(w.bid) + Number(w.ask)) / 2;

/** Today's move as a fraction; null when the Market didn't say where it stood. */
export function dayMove(w: WatchSummary): number | null {
    const before = w.previous === null ? 0 : Number(w.previous);
    return before > 0 ? midOf(w) / before - 1 : null;
}

/** Forecasting score; lower is better, and nothing until a question settled. */
export const brierLabel = (brier: number | string | null) =>
    brier === null ? '—' : Number(brier).toFixed(3);

/** "Bought 1,000 at 0.42" or "Settled at 1.00, +580". */
export function tradeLabel(t: TradeSummary): string {
    const price = Number(t.price);
    const at = price.toFixed(price < 10 ? 2 : 0);
    const size = credits(Number(t.units) * price);
    const result =
        t.realized === null
            ? ''
            : `, ${Number(t.realized) >= 0 ? '+' : '−'}${credits(Math.abs(Number(t.realized)))}`;
    if (t.side === 'Settled') return `Settled at ${at}${result}`;
    return `${t.side === 'Buy' ? 'Bought' : 'Sold'} ${size} at ${at}${result}`;
}

/** How a Session stands, in a word or two. */
export function sessionLabel(s: SessionSummary): string {
    switch (s.status) {
        case 'Booked':
            return 'booked';
        case 'Running':
            return 'in session';
        case 'Missed':
            return 'missed';
        default:
            return s.paid ? `done, ${s.fee} credits` : 'done, unpaid';
    }
}

/** What the Session left the Client owing, and whether they did it. */
export function homeworkLabel(s: SessionSummary): string {
    if (!s.homework) return '';
    if (s.homeworkKept === null) return s.homework;
    return `${s.homework} (${s.homeworkKept ? 'done' : 'not done'})`;
}

/** Soonest first, then the latest of what's over: what you'd want to see next. */
export function sessionsInOrder(sessions: SessionSummary[]): SessionSummary[] {
    const open = (s: SessionSummary) =>
        s.status === 'Booked' || s.status === 'Running';
    const at = (s: SessionSummary) => Number(s.startsAt);
    return [
        ...sessions.filter(open).sort((a, b) => at(a) - at(b)),
        ...sessions.filter((s) => !open(s)).sort((a, b) => at(b) - at(a)),
    ];
}

export const whenLabel = (at: number | string) =>
    new Date(Number(at)).toLocaleString('en-GB', {
        weekday: 'short',
        hour: '2-digit',
        minute: '2-digit',
    });

/** A workplace's door (ADR 0041), in the author's words. */
export const DOORS = [
    { value: 'open', label: 'Open to anyone' },
    { value: 'appointment', label: 'By appointment' },
    { value: 'staff', label: 'Staff only' },
] as const;

export const doorOf = (place: Place) => place.access ?? 'open';

/** Drawn Places someone works at: the ones with a door to keep. */
export const workplacesOf = (places: Place[]) =>
    places
        .filter((p) => p.kind === 'public' && (p.staff ?? []).length > 0)
        .sort((a, b) => (a.name ?? '').localeCompare(b.name ?? ''));
