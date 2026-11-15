export type Vec3 = [number, number, number];

export const MOTION_TAGS = [
    'flicker',
    'disco',
    'smoke',
    'paddle',
    'spin',
    'bob',
    'sway',
    'pulse',
    'flame',
    'twinkle',
    'light',
    'notes',
] as const;

export interface Repeat {
    n: number;
    step?: Vec3;
}

interface PartBase {
    at?: Vec3;
    c?: string;
    ry?: number;
    rx?: number;
    rz?: number;
    s?: Vec3;
    array?: Repeat;
    spin?: number;
    mirror?: 'x' | 'z' | 'xz';
    glow?: boolean | 'night';
    opacity?: number;
    tag?: string;
    flat?: boolean;
}

export type Part = PartBase &
    (
        | { box: Vec3 }
        | { cyl: [number, number] | [number, number, number]; sides?: number }
        | { cone: [number, number]; sides?: number }
        | { ball: number; squash?: number; detail?: number }
        | { dome: number; squash?: number }
        | { capsule: [number, number] }
        | { gable: Vec3 }
        | { wedge: Vec3 }
        | { pyramid: Vec3; top?: number }
        | { ring: [number, number]; arc?: number }
        | { lathe: [number, number][]; sides?: number }
        | { extrude: [number, number][]; depth?: number }
        | { tube: Vec3[]; r?: number; closed?: boolean; sides?: number }
        | { sign: [number, number]; text: string; fg?: string }
        | { group: Part[] }
        | { use: string; tint?: Record<string, string> }
        | { glb: string; fit: Vec3; mats?: Record<string, MaterialRule> }
    );

export interface MaterialRule {
    c?: string;
    glow?: boolean | 'night';
}

export interface Seat {
    at: [number, number];
    h: number;
    face: number;
}

export interface FurniState {
    condition: number;
    on: boolean;
    notable: boolean;
}

export interface PrefabVariant {
    seed: number;
    tint: Record<string, string>;
    text?: string;
    state?: FurniState;
}

export interface Prefab {
    size?: [number, number];
    solid?: boolean;
    wall?: boolean;
    flush?: boolean;
    seats?: Seat[];
    tint?: Record<string, string>;
    light?: { at: Vec3; colour: string; power: number };
    about?: string;
    parts: Part[] | ((v: PrefabVariant) => Part[]);
}

export function rng(seed: number) {
    let s = seed >>> 0 || 1;
    return () => {
        s ^= s << 13;
        s ^= s >>> 17;
        s ^= s << 5;
        return ((s >>> 0) % 10_000) / 10_000;
    };
}

export const pick = <T>(r: () => number, xs: readonly T[]): T =>
    xs[Math.floor(r() * xs.length) % xs.length];

export const turned = (
    size: [number, number],
    rot: number,
): [number, number] => (rot % 2 === 0 ? size : [size[1], size[0]]);

export function turn(x: number, z: number, rot: number): [number, number] {
    switch (((rot % 4) + 4) % 4) {
        case 1:
            return [z, -x];
        case 2:
            return [-x, -z];
        case 3:
            return [-z, x];
        default:
            return [x, z];
    }
}

export function partsOf(prefab: Prefab, v: PrefabVariant): Part[] {
    return typeof prefab.parts === 'function' ? prefab.parts(v) : prefab.parts;
}

export const PALETTE: Record<string, string> = {};

export const REGISTRY: Record<string, Prefab> = {};

export function colourOf(
    c: string | undefined,
    prefab: Prefab,
    v: PrefabVariant,
): string {
    if (!c) return '#999999';
    if (c.startsWith('#')) return c;
    if (!c.startsWith('$')) return PALETTE[c] ?? '#999999';
    const key = c.slice(1);
    const hit = v.tint[key] ?? prefab.tint?.[key] ?? '#ff00ff';
    return hit.startsWith('#') ? hit : (PALETTE[hit] ?? '#999999');
}

export interface Model {
    about?: string;
    size?: [number, number];
    wall?: boolean;
    solid?: boolean;
    seats?: Seat[];
    tint?: Record<string, string>;
    light?: { at: Vec3; colour: string; power: number };
    parts?: Part[];
}

export function prefabOf(m: Model): Prefab | null {
    if (!m.parts?.length) return null;
    return {
        size: m.size,
        wall: !!m.wall,
        flush: !!m.wall,
        solid: m.solid ?? !m.wall,
        seats: m.seats,
        tint: m.tint,
        light: m.light,
        about: m.about,
        parts: m.parts,
    };
}
