import { describe, expect, it } from 'vitest';
import type { LlmProviderEntry } from '../../../api/model/llmProviderEntry';
import { percent, shareOf, splitOf } from './split';

const provider = (id: string, patch: Partial<LlmProviderEntry> = {}) =>
    ({
        id,
        type: 'openrouter',
        baseUrl: 'https://x',
        supportedComplexities: 1,
        isEnabled: true,
        ...patch,
    }) as LlmProviderEntry;

describe('splitOf', () => {
    it('splits one priority by share', () => {
        const [step, ...rest] = splitOf(
            [
                provider('haiku', { share: 50 }),
                provider('nemotron', { share: 50 }),
            ],
            1,
        );
        expect(rest).toEqual([]);
        expect(
            step.parts.map((p) => [p.provider.id, percent(p.fraction)]),
        ).toEqual([
            ['haiku', '50%'],
            ['nemotron', '50%'],
        ]);
    });

    it('puts a higher priority number in a later step', () => {
        const steps = splitOf(
            [
                provider('cloud', { priority: 10 }),
                provider('local', { priority: 0 }),
            ],
            1,
        );
        expect(steps.map((s) => s.parts.map((p) => p.provider.id))).toEqual([
            ['local'],
            ['cloud'],
        ]);
    });

    it('leaves out reserved, disabled and other-tier providers', () => {
        const steps = splitOf(
            [
                provider('pool'),
                provider('reserved', { reserved: true }),
                provider('off', { isEnabled: false }),
                provider('voice', { supportedComplexities: 2 }),
            ],
            1,
        );
        expect(steps.flatMap((s) => s.parts.map((p) => p.provider.id))).toEqual(
            ['pool'],
        );
    });

    it('treats a missing or zero share as a whole one', () => {
        expect(shareOf(provider('a'))).toBe(100);
        expect(shareOf(provider('a', { share: 0 }))).toBe(100);
        expect(shareOf(provider('a', { share: '25' }))).toBe(25);
    });
});
