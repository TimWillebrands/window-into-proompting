import type { CheckResult } from './patch.ts';

export type PropositionStatus = 'open' | 'merged' | 'rejected' | 'fallen';

export interface Proposition {
    n: number;
    commission: string;
    title: string;
    why: string;
    author: string;
    branch: string;
    /** The Proposition this stacks on; null when it sits on main. */
    parent: number | null;
    baseSha: string;
    sha: string;
    status: PropositionStatus;
    note: string | null;
    checks: CheckResult[];
    createdAt: number;
}

/** New work stacks on the newest thing still waiting. */
export const latestOpen = (all: Proposition[]) => all.filter((p) => p.status === 'open').at(-1) ?? null;

/** Rejection takes everything stacked on it down too. */
export function reject(all: Proposition[], n: number, note: string | null) {
    const target = all.find((p) => p.n === n);
    if (!target || target.status !== 'open') return false;
    target.status = 'rejected';
    target.note = note;
    const gone = new Set([n]);
    for (const p of all) {
        if (p.status !== 'open' || p.parent === null || !gone.has(p.parent)) continue;
        p.status = 'fallen';
        gone.add(p.n);
    }
    return true;
}
