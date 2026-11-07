import type {
    CduAllowanceSummary,
    CduExchangeSummary,
    CduLineSummary,
    CduModelSummary,
    CduPrSummary,
} from '../../api/model';

/** Realtime and the first fetch overlap; the CDU's line number says what's already here. */
export function mergeLines(
    held: CduLineSummary[],
    incoming: CduLineSummary[],
): CduLineSummary[] {
    const seen = new Set(held.map((l) => l.n));
    const fresh = incoming.filter((l) => !seen.has(l.n));
    return fresh.length === 0
        ? held
        : [...held, ...fresh].sort((a, b) => Number(a.n) - Number(b.n));
}

const PROMPTS: Record<string, string> = {
    heard: '>',
    say: 'cdu>',
    thought: '...',
    tool: '$',
    failed: '!!',
    status: '--',
};

export const linePrefix = (kind: string) => PROMPTS[kind] ?? '  ';

export const money = (usd: number | string) => {
    const amount = Number(usd);
    return `$${amount.toFixed(amount > 0 && amount < 1 ? 4 : 2)}`;
};

export const allowanceLine = (a: CduAllowanceSummary | null | undefined) =>
    a
        ? `${money(Math.max(0, Number(a.capUsd) - Number(a.usd)))} of ${money(a.capUsd)} left today`
        : 'allowance unreadable';

const CHECKS: Record<string, string> = {
    passing: 'checks pass',
    failing: 'checks fail',
    pending: 'checks running',
};

/** The PR in a few words: GitHub's state and its checks, nothing the CDU said. */
export const prLine = (pr: CduPrSummary) =>
    `#${pr.number} ${pr.state.toLowerCase()}, ${CHECKS[pr.checks] ?? 'no checks yet'}`;

/** Where an exchange stands: waiting, silent, or answered, and whether it asked back. */
export function exchangeWord(e: CduExchangeSummary): string {
    if (e.status === 'queued') return 'waiting for it';
    if (e.status === 'failed') return 'it fell silent';
    const commits = Number(e.commits);
    const moved =
        commits > 0 ? `, ${commits} commit${commits === 1 ? '' : 's'}` : '';
    return e.asked && !e.answered
        ? `it asked back${moved}`
        : `answered${moved}`;
}

export const shortSha = (sha: string) => sha.slice(0, 7);

/** A mind on the ladder: its tier and what it costs per million tokens. */
export const mindLine = (m: CduModelSummary) =>
    Number(m.in) === 0 && Number(m.out) === 0
        ? `${m.id} · ${m.tier ?? '?'} · free`
        : `${m.id} · ${m.tier ?? '?'} · ${money(m.in)}/${money(m.out)}`;
