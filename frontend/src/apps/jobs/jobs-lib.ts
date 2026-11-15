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

export type WorkPlace =
    | { page: 'people' }
    | { page: 'person'; id: string }
    | { page: 'companies' }
    | { page: 'company'; id: string }
    | { page: 'practices' }
    | { page: 'doors' };

export function placeFrom(page?: string, id?: string): WorkPlace {
    if ((page === 'person' || page === 'company') && id) return { page, id };
    if (page === 'companies' || page === 'practices' || page === 'doors')
        return { page };
    return { page: 'people' };
}

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

export function jobLine(person: JobSummary, work: Employment): string {
    if (!person.title) return person.seeded ? 'No job' : 'Working it out…';
    if (work.kind === 'company')
        return `${person.title} at ${work.company.name}`;
    if (work.kind === 'practice')
        return `${person.title}, ${work.practice.field}`;
    return `${person.title} at ${person.workplace}`;
}

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

function promiseLabel(d: Desire, nameOf: (id: string) => string): string {
    const to = d.targetId ? nameOf(d.targetId) : 'someone';
    const how = d.status ? PROMISE_OUTCOMES[d.status] : undefined;
    return `promised ${to}: ${d.text ?? ''}${how ? ` (${how})` : ''}`;
}

export function refusal(error: ProblemDetails): string {
    const said = error.detail ?? error.title;
    return said ? said.replace(/^"|"$/g, '') : "That didn't work.";
}

export const credits = (value: number | string) =>
    Math.round(Number(value)).toLocaleString('en-GB');

export const signedPercent = (fraction: number) =>
    `${fraction >= 0 ? '+' : '−'}${Math.abs(fraction * 100).toFixed(1)}%`;

export type Tone = 'up' | 'down' | 'flat';

export function toneOf(fraction: number): Tone {
    if (Math.abs(fraction) < 0.0005) return 'flat';
    return fraction > 0 ? 'up' : 'down';
}

export function bookReturn(company: CompanySummary): number {
    const capital = Number(company.capital);
    return capital > 0 ? Number(company.value) / capital - 1 : 0;
}

export function positionReturn(p: PositionSummary): number {
    const cost = Number(p.cost);
    return cost > 0 ? Number(p.value) / cost - 1 : 0;
}

export function priceLabel(price: number): string {
    return price < 10 ? price.toFixed(2) : credits(price);
}

export const midOf = (w: WatchSummary) => (Number(w.bid) + Number(w.ask)) / 2;

export function dayMove(w: WatchSummary): number | null {
    const before = w.previous === null ? 0 : Number(w.previous);
    return before > 0 ? midOf(w) / before - 1 : null;
}

export const brierLabel = (brier: number | string | null) =>
    brier === null ? '—' : Number(brier).toFixed(3);

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

export function homeworkLabel(s: SessionSummary): string {
    if (!s.homework) return '';
    if (s.homeworkKept === null) return s.homework;
    return `${s.homework} (${s.homeworkKept ? 'done' : 'not done'})`;
}

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

export const DOORS = [
    { value: 'open', label: 'Open to anyone' },
    { value: 'appointment', label: 'By appointment' },
    { value: 'staff', label: 'Staff only' },
] as const;

export const doorOf = (place: Place) => place.access ?? 'open';

export const workplacesOf = (places: Place[]) =>
    places
        .filter((p) => p.kind === 'public' && (p.staff ?? []).length > 0)
        .sort((a, b) => (a.name ?? '').localeCompare(b.name ?? ''));
