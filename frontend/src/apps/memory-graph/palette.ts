/// <summary>Per-person hues; picked to stay apart from the Event amber and Concept teal.</summary>
export const PERSON_PALETTE = [
    '#7B5CC4',
    '#D2477A',
    '#2F7FC1',
    '#E0662E',
    '#1F9AA6',
    '#B34FB0',
    '#6B7A8F',
    '#A0522D',
] as const;

export const KIND_COLOUR = {
    Event: '#F3C487',
    Concept: '#7CC4A4',
} as const;

export const STANCE_COLOUR = { warm: '#2E9E4F', cold: '#D23C3C' } as const;
