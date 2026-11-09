import { mix } from './prefabs';

const smooth = (a: number, b: number, x: number) => {
    const t = Math.min(1, Math.max(0, (x - a) / (b - a)));
    return t * t * (3 - 2 * t);
};

export interface Sky {
    background: string;
    sun: string;
    sunPower: number;
    sunAt: [number, number, number];
    ambient: number;
    /** Lamps and lit windows on. */
    night: boolean;
    /** Colour window panes show. */
    pane: string;
    water: [string, string];
    ambientColour: string;
    /** CSS behind the canvas. */
    backdrop: string;
}

const MOODS: Record<
    string,
    { ambient: number; sun: number; colour?: string; night?: boolean }
> = {
    bright: { ambient: 1.1, sun: 1.1 },
    warm: { ambient: 0.9, sun: 0.9, colour: '#ffdcb0' },
    dim: { ambient: 0.66, sun: 0.3, colour: '#c9d2f0', night: true },
    candle: { ambient: 0.58, sun: 0.15, colour: '#ffb870', night: true },
    neon: { ambient: 0.68, sun: 0.12, colour: '#b9a0ff', night: true },
    cold: { ambient: 1, sun: 1, colour: '#cfe2ff' },
};

/** Light and colour for an hour of the local day (the World runs on local time). */
export function skyAt(hour: number, outdoor: boolean, mood?: string): Sky {
    const m = (mood && MOODS[mood]) || { ambient: 1, sun: 1 };
    const day = smooth(5.5, 8, hour) * (1 - smooth(18.5, 21, hour));
    const dusk =
        Math.max(0, 1 - Math.abs(hour - 19.8) / 1.6) +
        Math.max(0, 1 - Math.abs(hour - 6.6) / 1.2);
    const glow = Math.min(1, dusk) * 0.6;
    const sunColour = mix(
        '#7f95cc',
        mix('#fff4df', '#ffb070', Math.min(1, dusk)),
        day,
    );
    const swing = Math.min(1, Math.max(0, (hour - 6) / 13));
    const outside = mix(mix('#0d1530', '#8fc4ef', day), '#f2955c', glow);
    const night = m.night || day < 0.4;
    const room = night ? '#1b1a2b' : '#27304a';
    return {
        background: outdoor ? outside : '#141922',
        backdrop: outdoor
            ? `linear-gradient(${mix(outside, '#0a1c3d', 0.35)}, ${outside} 55%, ${mix(outside, '#ffffff', 0.25)})`
            : `radial-gradient(ellipse at 50% 45%, ${room}, #0b0d14 85%)`,
        sun: sunColour,
        sunPower: (outdoor ? 0.35 + 1.75 * day : 0.45 + 1.0 * day) * m.sun,
        sunAt: [-12 + 24 * swing, 16, -9],
        ambient: (outdoor ? 0.45 + 0.65 * day : 0.62 + 0.26 * day) * m.ambient,
        ambientColour: m.colour ?? (outdoor ? '#dfe8ff' : '#fff1de'),
        night,
        pane: mix('#22304f', '#cfe8ff', day),
        water: [mix('#0b1d2b', '#1f5466', day), mix('#1a3550', '#5fa3b3', day)],
    };
}
