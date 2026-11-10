/** Prefabs: furniture and buildings as a few coloured primitives (ADR 0023). Units are tiles; y is up. */
export type Vec3 = [number, number, number];

/** Animation a part can be tagged with; the rest of the tags are state hooks (leaf, screen, window). */
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

/** Copies of a part: a row (`step` apart) and/or a ring around the origin's y axis. */
export interface Repeat {
    n: number;
    step?: Vec3;
}

interface PartBase {
    /** Bottom centre for solids, centre for balls; relative to the footprint centre on the floor. */
    at?: Vec3;
    /** Hex colour, a palette name (`wood`, `brass`), or `$name` for a tint the placement can override. */
    c?: string;
    /** Turn around y, radians. */
    ry?: number;
    /** Turn around x, radians (wheels, arches). */
    rx?: number;
    /** Turn around z, radians. */
    rz?: number;
    /** Stretch along x, y, z before turning. */
    s?: Vec3;
    /** A row of copies. */
    array?: Repeat;
    /** Copies turned evenly around the y axis through the origin (legs, spokes, petals). */
    spin?: number;
    /** A mirrored twin across the plane through the origin: 'x' flips left-right, 'z' front-back, 'xz' gives four. */
    mirror?: 'x' | 'z' | 'xz';
    /** Self-lit: lamps, screens, windows at night. */
    glow?: boolean | 'night';
    /** See-through (glass, water in a vase). */
    opacity?: number;
    /** Named so state or motion can find it: leaf, screen, light, smoke, spin, bob, sway, pulse, flame, twinkle. */
    tag?: string;
    /** Skip the shadow (tiny or flat parts). */
    flat?: boolean;
}

export type Part = PartBase &
    (
        | { box: Vec3 }
        /** [radius, height] or [top radius, bottom radius, height]. */
        | { cyl: [number, number] | [number, number, number]; sides?: number }
        | { cone: [number, number]; sides?: number }
        | { ball: number; squash?: number; detail?: number }
        /** Half a ball, flat side down: radius. */
        | { dome: number; squash?: number }
        /** Pill standing up: [radius, height]. */
        | { capsule: [number, number] }
        /** Triangular prism, ridge along x: [width, height, depth]. */
        | { gable: Vec3 }
        /** Ramp, high at -z: [width, height, depth]. */
        | { wedge: Vec3 }
        /** Four-sided pyramid: [width, height, depth]; `top` 0-1 cuts it off flat (a frustum). */
        | { pyramid: Vec3; top?: number }
        /** Ring in the xy plane: [radius, tube]. */
        | { ring: [number, number]; arc?: number }
        /** A profile of [radius, y] points turned around the y axis: vases, lampshades, bottles, columns. */
        | { lathe: [number, number][]; sides?: number }
        /** A flat outline of [x, y] points, given depth along z: stars, arches, letters, leaves. */
        | { extrude: [number, number][]; depth?: number }
        /** A pipe through [x, y, z] points: cables, handles, vines, neon. */
        | { tube: Vec3[]; r?: number; closed?: boolean; sides?: number }
        /** Flat sign facing +z: [width, height]; text drawn on it. */
        | { sign: [number, number]; text: string; fg?: string }
        /** Parts moved, turned and repeated as one. */
        | { group: Part[] }
        /** Another prefab's parts, by name. */
        | { use: string; tint?: Record<string, string> }
        /** Catalogue model (ADR 0027), scaled to fit [w, h, d] tiles (h 0 = footprint only); `c` paints its stand-in. */
        | { glb: string; fit: Vec3; mats?: Record<string, MaterialRule> }
    );

/** How a catalogue model's named material is drawn: a palette colour or `$tint`, and lighting. */
export interface MaterialRule {
    c?: string;
    glow?: boolean | 'night';
}

export interface Seat {
    /** Tile offset from the footprint's first tile, before rotation. */
    at: [number, number];
    /** Seat top height. */
    h: number;
    /** Quarter turns the sitter faces (0 = +z). */
    face: number;
}

/** Backend furni state, as the rules read it now. */
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
    /** Footprint in tiles (x, z) before rotation; default 1×1. */
    size?: [number, number];
    /** Blocks walking on its footprint; default true. */
    solid?: boolean;
    /** Hangs on the wall behind its tile instead of standing on it. */
    wall?: boolean;
    /** A wall thing whose parts are already built against z = -0.5 (models as data). */
    flush?: boolean;
    seats?: Seat[];
    /** Tints a placement can override. */
    tint?: Record<string, string>;
    /** Warm light it gives off at night or indoors. */
    light?: { at: Vec3; colour: string; power: number };
    /** One line on what it is, for whoever designs with it. */
    about?: string;
    parts: Part[] | ((v: PrefabVariant) => Part[]);
}

/** Small deterministic PRNG so variants are stable per placement. */
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

/** Footprint after quarter turns. */
export const turned = (
    size: [number, number],
    rot: number,
): [number, number] => (rot % 2 === 0 ? size : [size[1], size[0]]);

/** A local offset (from the footprint centre) after quarter turns. */
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

/** Named colours anyone can use instead of hex; shared with the backend through library.json. */
export const PALETTE: Record<string, string> = {};

/** Prefabs by name, for `use` parts; filled by the library. */
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

/** A model as data (backend ThingModel, library.json): what a prefab is when nobody wrote code for it. */
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
