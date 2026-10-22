import type { Desire, JobSummary } from '../../api/model';

const localDay = (d: Date) =>
    `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;

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
