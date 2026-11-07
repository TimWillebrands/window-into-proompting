import type { CommissionSummary, WorksLineSummary } from '../../api/model';

export const OPEN_STATUSES = ['Filed', 'Working', 'Waiting', 'Checking'];

/** Realtime and the first fetch overlap; the Runner's line number says what's already here. */
export function mergeLines(
    held: WorksLineSummary[],
    incoming: WorksLineSummary[],
): WorksLineSummary[] {
    const seen = new Set(held.map((l) => l.n));
    const fresh = incoming.filter((l) => !seen.has(l.n));
    return fresh.length === 0
        ? held
        : [...held, ...fresh].sort((a, b) => Number(a.n) - Number(b.n));
}

const PROMPTS: Record<string, string> = {
    say: 'builder>',
    thought: '...',
    tool: '$',
    failed: '!!',
    status: '--',
    check: '[check]',
};

export const linePrefix = (kind: string) => PROMPTS[kind] ?? '  ';

const WORDS: Record<string, string> = {
    Filed: 'filed',
    Working: 'building',
    Waiting: 'waiting on an Architect',
    Checking: 'being checked',
    Proposed: 'with the world-spirits',
    Accepted: 'taken by the spirits',
    Rejected: 'turned down',
    Fallen: 'fell with its base',
    Dropped: 'dropped',
    Answered: 'answered',
    Failed: 'failed',
};

export const statusWord = (status: string) => WORDS[status] ?? status;

export const money = (usd: number | string) => {
    const amount = Number(usd);
    return `$${amount.toFixed(amount > 0 && amount < 1 ? 4 : 2)}`;
};

/** Budget, spend and what changed in one line; only facts the Runner reported. */
export function factsLine(c: CommissionSummary): string {
    const parts = [`${money(c.spentUsd)} of ${money(c.budgetUsd)}`, c.model];
    if (c.kind === 'Change')
        parts.push(`${c.files} files, +${c.added} -${c.removed}`);
    return parts.join(' · ');
}

export const isOpen = (c: CommissionSummary) =>
    OPEN_STATUSES.includes(c.status);

/** The newest open one if any, otherwise the newest of all. */
export const pickDefault = (all: CommissionSummary[]) =>
    all.find(isOpen)?.id ?? all[0]?.id ?? null;

/** One line for whoever merges: the patch carries the Architect's name as author. */
export const mergeCommand = (origin: string, partyId: string, id: string) =>
    `curl -s ${origin}/api/worlds/${partyId}/works/commissions/${id}/patch | git am`;
