/** Prefabs: furniture and buildings as a few coloured primitives (ADR 0023). Units are tiles; y is up. */
export type Vec3 = [number, number, number];

interface PartBase {
    /** Bottom centre for solids, centre for balls; relative to the footprint centre on the floor. */
    at?: Vec3;
    /** Hex colour, or `$name` for a tint the placement can override. */
    c: string;
    /** Turn around y, radians. */
    ry?: number;
    /** Turn around x, radians (wheels, arches). */
    rx?: number;
    /** Turn around z, radians. */
    rz?: number;
    /** Self-lit: lamps, screens, windows at night. */
    glow?: boolean | 'night';
    /** See-through (glass, water in a vase). */
    opacity?: number;
    /** Named so state can restyle it: leaf, screen, light, smoke. */
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
        /** Triangular prism, ridge along x: [width, height, depth]. */
        | { gable: Vec3 }
        /** Ring in the xy plane: [radius, tube]. */
        | { ring: [number, number]; arc?: number }
        /** Flat sign facing +z: [width, height]; text drawn on it. */
        | { sign: [number, number]; text: string; fg?: string }
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
    seats?: Seat[];
    /** Tints a placement can override. */
    tint?: Record<string, string>;
    /** Warm light it gives off at night or indoors. */
    light?: { at: Vec3; colour: string; power: number };
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

export function colourOf(c: string, prefab: Prefab, v: PrefabVariant): string {
    if (!c.startsWith('$')) return c;
    const key = c.slice(1);
    return v.tint[key] ?? prefab.tint?.[key] ?? '#ff00ff';
}
