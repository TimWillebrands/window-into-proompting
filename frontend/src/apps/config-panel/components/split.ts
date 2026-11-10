import type { LlmProviderEntry } from '../../../api/model/llmProviderEntry';

export const DEFAULT_SHARE = 100;

export const shareOf = (p: LlmProviderEntry) =>
    Math.max(1, Number(p.share ?? DEFAULT_SHARE) || DEFAULT_SHARE);

export const inPool = (p: LlmProviderEntry, tier: number) =>
    p.isEnabled !== false &&
    !p.reserved &&
    ((p.supportedComplexities ?? 0) & tier) !== 0;

export interface SplitStep {
    priority: number;
    parts: { provider: LlmProviderEntry; fraction: number }[];
}

/** A tier's pool as the router walks it: one step per Priority, each split by Share (ADR 0048). */
export function splitOf(
    providers: LlmProviderEntry[],
    tier: number,
): SplitStep[] {
    const steps = new Map<number, LlmProviderEntry[]>();
    for (const p of providers.filter((p) => inPool(p, tier))) {
        const priority = Number(p.priority ?? 0);
        steps.set(priority, [...(steps.get(priority) ?? []), p]);
    }
    return [...steps.entries()]
        .sort(([a], [b]) => a - b)
        .map(([priority, members]) => {
            const total = members.reduce((sum, p) => sum + shareOf(p), 0);
            return {
                priority,
                parts: members.map((provider) => ({
                    provider,
                    fraction: shareOf(provider) / total,
                })),
            };
        });
}

export const percent = (fraction: number) => `${Math.round(fraction * 100)}%`;
