import type {
    CompanySummary,
    ProblemDetails,
    SessionSummary,
    TradeSummary,
} from '../../api/model';

/** The backend's own reason; a plain-string 400 can arrive still wearing its JSON quotes. */
export function refusal(error: ProblemDetails): string {
    const said = error.detail ?? error.title;
    return said ? said.replace(/^"|"$/g, '') : "That didn't work.";
}

/** "12,300": credits are whole numbers to look at, whatever the Book holds. */
export const credits = (value: number | string) =>
    Math.round(Number(value)).toLocaleString('en-GB');

/** "+3.1%": the Book against what it started with. */
export function returnLabel(company: CompanySummary): string {
    const capital = Number(company.capital);
    if (capital <= 0) return '—';
    const change = (Number(company.value) / capital - 1) * 100;
    return `${change >= 0 ? '+' : '−'}${Math.abs(change).toFixed(1)}%`;
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
