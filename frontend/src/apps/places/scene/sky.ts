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
}

/** Light and colour for an hour of the local day (the World runs on local time). */
export function skyAt(hour: number, outdoor: boolean): Sky {
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
    return {
        background: outdoor ? outside : '#141922',
        sun: sunColour,
        sunPower: outdoor ? 0.35 + 1.75 * day : 0.25 + 0.9 * day,
        sunAt: [-12 + 24 * swing, 16, -9],
        ambient: outdoor ? 0.45 + 0.65 * day : 0.75 + 0.35 * day,
        night: day < 0.4,
        pane: mix('#22304f', '#cfe8ff', day),
        water: [mix('#0b1d2b', '#1f5466', day), mix('#1a3550', '#5fa3b3', day)],
    };
}
