export const tones = ['neutral', 'go', 'caution', 'danger'] as const;

export type Tone = (typeof tones)[number];
