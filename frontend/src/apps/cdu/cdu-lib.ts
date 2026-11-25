import type {
    CduAllowanceSummary,
    CduExchangeSummary,
    CduLineSummary,
    CduModelSummary,
    CduPrSummary,
    CduVerdictSummary,
} from '../../api/model';

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

export const SAID: Record<string, string> = {
    ask: 'asked',
    talk: 'handed it work',
    answer: 'answered',
};

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

export const prLine = (pr: CduPrSummary) =>
    `#${pr.number} ${pr.state.toLowerCase()}, ${CHECKS[pr.checks] ?? 'no checks yet'}${pr.conflicts ? ', conflicts with master' : ''}`;

export function exchangeWord(e: CduExchangeSummary): string {
    if (e.status === 'queued') return 'waiting for it';
    if (e.status === 'failed') return 'it fell silent';
    if (e.status === 'parked') return 'parked: allowance spent';
    const commits = Number(e.commits);
    const moved =
        commits > 0 ? `, ${commits} commit${commits === 1 ? '' : 's'}` : '';
    return e.asked && !e.answered
        ? `it asked back${moved}`
        : `answered${moved}`;
}

export const shortSha = (sha: string) => sha.slice(0, 7);

export const mindLine = (m: CduModelSummary) =>
    Number(m.in) === 0 && Number(m.out) === 0
        ? `${m.id} · ${m.tier ?? '?'} · free`
        : `${m.id} · ${m.tier ?? '?'} · ${money(m.in)}/${money(m.out)}`;

export const resetLine = (at: number | string) =>
    `${new Date(Number(at)).toISOString().slice(0, 16).replace('T', ' ')} UTC`;

export const verdictLine = (v: CduVerdictSummary) =>
    `Last verdict: #${v.pr} ${v.state === 'MERGED' ? 'merged' : 'closed without merging'}, ${resetLine(v.at)}`;
