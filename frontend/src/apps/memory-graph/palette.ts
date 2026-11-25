export const PERSON_PALETTE = [
    'var(--ui-series-1)',
    'var(--ui-series-2)',
    'var(--ui-series-5)',
    'var(--ui-series-6)',
    'var(--ui-series-7)',
    'var(--ui-series-8)',
] as const;

export const PERSON_OTHER = 'var(--ui-series-other)';

export const personColour = (i: number): string =>
    PERSON_PALETTE[i] ?? PERSON_OTHER;

export const KIND_COLOUR = {
    Event: 'var(--ui-series-4)',
    Concept: 'var(--ui-series-3)',
} as const;

export const STANCE_COLOUR = {
    warm: 'var(--ui-tone-go)',
    cold: 'var(--ui-tone-danger)',
} as const;
