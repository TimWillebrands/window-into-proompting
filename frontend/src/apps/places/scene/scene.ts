import type { Pt } from '../iso/iso';
import { findPath } from '../iso/path';
import { type Prefab, type Seat, turn, turned } from './prefab';

/** What a map cell is made of. */
export type Ground =
    | 'void'
    | 'floor'
    | 'wall'
    | 'window'
    | 'door'
    | 'water'
    | 'street'
    | 'quay'
    | 'bridge'
    | 'grass'
    | 'house';

/** A canal house's look; anything left out is rolled from its position. */
export interface HouseStyle {
    sign?: string;
    awning?: string;
    brick?: string;
    storeys?: number;
    gable?: 'step' | 'neck' | 'bell' | 'cornice';
}

export interface Cell {
    ground: Ground;
    prop?: string;
    rot?: number;
    house?: HouseStyle;
}

export interface PropDef {
    /** Prefab name. */
    p: string;
    /** First (back) tile of the footprint. */
    at: [number, number];
    rot?: number;
    /** Lift, e.g. a machine on a counter. */
    y?: number;
    tint?: Record<string, string>;
    text?: string;
    /** Its seats belong to this spot. */
    spot?: string;
}

/** A Place's look, authored as an ASCII map plus props (ADR 0023). Spots are the backend's; this only places them. */
export interface SceneDef {
    id: string;
    outdoor?: boolean;
    /** One row per z, one char per x; see LEGEND (scenes can add their own). */
    map: string;
    legend?: Record<string, Cell>;
    props?: PropDef[];
    /** Where each backend spot is; Bodies stand around it, or on the seats of props tagged with it. */
    spots: Record<string, { at: [number, number]; r?: number }>;
    /** Where furni stand, by furni id; unlisted ones go near their spot. */
    furni?: Record<string, Omit<PropDef, 'p'> & { p?: string }>;
    /** Tile you appear on coming from a scene (by scene id); `*` for anywhere else. */
    entrances?: Record<string, [number, number]>;
    floor?: string;
    wall?: string;
    /** Where the camera starts. */
    focus?: [number, number];
}

export const LEGEND: Record<string, Cell> = {
    ' ': { ground: 'void' },
    '.': { ground: 'floor' },
    '#': { ground: 'wall' },
    w: { ground: 'window' },
    D: { ground: 'door' },
    '~': { ground: 'water' },
    '=': { ground: 'street' },
    '-': { ground: 'quay' },
    b: { ground: 'bridge' },
    g: { ground: 'grass' },
    H: { ground: 'house' },
    T: { ground: 'quay', prop: 'elm' },
    t: { ground: 'grass', prop: 'elm' },
    i: { ground: 'quay', prop: 'bollard' },
    L: { ground: 'quay', prop: 'street-lamp' },
    B: { ground: 'quay', prop: 'bike' },
};

export const WALKABLE: ReadonlySet<Ground> = new Set([
    'floor',
    'door',
    'street',
    'quay',
    'bridge',
    'grass',
]);

const BACK: ReadonlySet<Ground> = new Set(['wall', 'window', 'door', 'house']);

/** Ground top height: water sits lower, bridges a bit higher. */
export const TOP: Record<Ground, number> = {
    void: 0,
    floor: 0,
    wall: 0,
    window: 0,
    door: 0,
    water: -0.4,
    street: -0.04,
    quay: 0,
    bridge: 0.14,
    grass: 0.02,
    house: 0,
};

export interface Slot {
    tile: Pt;
    /** Seat height when it's a seat. */
    h?: number;
    /** Quarter turns to face (0 = +z). */
    face?: number;
    seat?: boolean;
}

export interface PlacedProp extends PropDef {
    prefab: Prefab;
    rot: number;
    /** Footprint after rotation. */
    size: [number, number];
    seed: number;
    /** Set for backend furni. */
    furniId?: string;
}

export interface Scene {
    def: SceneDef;
    w: number;
    h: number;
    cells: Cell[][];
    props: PlacedProp[];
    spots: Record<string, { at: Pt; seats: Slot[]; stands: Slot[] }>;
    blocked: Set<string>;
}

export const key = (p: Pt) => `${p.x},${p.y}`;

/** The map's rows, trimmed of the template literal's blank first and last lines. */
export function rows(map: string): string[] {
    const lines = map.split('\n');
    while (lines.length && lines[0].trim() === '') lines.shift();
    while (lines.length && lines[lines.length - 1].trim() === '') lines.pop();
    const indent = Math.min(
        ...lines
            .filter((l) => l.trim())
            .map((l) => l.length - l.trimStart().length),
    );
    return lines.map((l) => l.slice(indent).replace(/\s+$/, ''));
}

export function parseScene(
    def: SceneDef,
    prefabs: Record<string, Prefab>,
): Scene {
    const lines = rows(def.map);
    const h = lines.length;
    const w = Math.max(...lines.map((l) => l.length));
    const legend = { ...LEGEND, ...def.legend };
    const cells: Cell[][] = lines.map((line) =>
        Array.from({ length: w }, (_, x) => {
            const ch = line[x] ?? ' ';
            const cell = legend[ch];
            if (!cell)
                throw new Error(`scene ${def.id}: unknown map char '${ch}'`);
            return cell;
        }),
    );
    const props: PlacedProp[] = [];
    const place = (d: PropDef, furniId?: string) => {
        const prefab = prefabs[d.p];
        if (!prefab) throw new Error(`scene ${def.id}: no prefab '${d.p}'`);
        const rot = d.rot ?? 0;
        props.push({
            ...d,
            prefab,
            rot,
            size: turned(prefab.size ?? [1, 1], rot),
            seed: (d.at[0] + 1) * 7919 + (d.at[1] + 1) * 104_729,
            furniId,
        });
    };
    cells.forEach((row, z) => {
        row.forEach((cell, x) => {
            if (cell.prop)
                place({ p: cell.prop, at: [x, z], rot: cell.rot ?? 0 });
        });
    });
    for (const d of def.props ?? []) place(d);

    const ground = (x: number, z: number): Ground =>
        cells[z]?.[x]?.ground ?? 'void';
    const blocked = new Set<string>();
    for (let z = 0; z < h; z++)
        for (let x = 0; x < w; x++)
            if (!WALKABLE.has(ground(x, z))) blocked.add(key({ x, y: z }));
    for (const p of props) {
        if (p.prefab.solid === false || p.prefab.wall) continue;
        for (const t of footprint(p)) blocked.add(key(t));
    }

    const spots: Scene['spots'] = {};
    const taken = new Set<string>();
    for (const [id, s] of Object.entries(def.spots)) {
        const at = { x: s.at[0], y: s.at[1] };
        const seats = props
            .filter((p) => p.spot === id)
            .flatMap((p) =>
                (p.prefab.seats ?? []).map((seat) => seatSlot(p, seat)),
            );
        const r = s.r ?? 2;
        const stands: Slot[] = [];
        for (let dz = -r; dz <= r; dz++)
            for (let dx = -r; dx <= r; dx++) {
                const t = { x: at.x + dx, y: at.y + dz };
                if (blocked.has(key(t)) || taken.has(key(t))) continue;
                if (t.x < 0 || t.y < 0 || t.x >= w || t.y >= h) continue;
                stands.push({ tile: t, face: faceTowards(t, at) });
            }
        stands.sort(
            (a, b) =>
                dist(a.tile, at) - dist(b.tile, at) ||
                a.tile.y - b.tile.y ||
                a.tile.x - b.tile.x,
        );
        const kept = stands.slice(0, 8);
        for (const s2 of kept) taken.add(key(s2.tile));
        spots[id] = { at, seats, stands: kept };
    }
    return { def, w, h, cells, props, spots, blocked };
}

export function footprint(p: PlacedProp): Pt[] {
    const out: Pt[] = [];
    for (let dz = 0; dz < p.size[1]; dz++)
        for (let dx = 0; dx < p.size[0]; dx++)
            out.push({ x: p.at[0] + dx, y: p.at[1] + dz });
    return out;
}

/** A prefab seat in world tiles, rotated with its prop. */
export function seatSlot(p: PlacedProp, seat: Seat): Slot {
    const [sw, sd] = p.prefab.size ?? [1, 1];
    const [lx, lz] = turn(
        seat.at[0] + 0.5 - sw / 2,
        seat.at[1] + 0.5 - sd / 2,
        p.rot,
    );
    return {
        tile: {
            x: p.at[0] + p.size[0] / 2 + lx - 0.5,
            y: p.at[1] + p.size[1] / 2 + lz - 0.5,
        },
        h: seat.h + (p.y ?? 0),
        face: (seat.face + p.rot) % 4,
        seat: true,
    };
}

const dist = (a: Pt, b: Pt) => Math.hypot(a.x - b.x, a.y - b.y);

/** Quarter turn that best faces from a tile toward a point. */
export function faceTowards(from: Pt, to: Pt): number {
    const dx = to.x - from.x;
    const dz = to.y - from.y;
    if (dx === 0 && dz === 0) return 0;
    if (Math.abs(dz) >= Math.abs(dx)) return dz > 0 ? 0 : 2;
    return dx > 0 ? 1 : 3;
}

export const groundAt = (s: Scene, t: Pt): Ground =>
    s.cells[Math.round(t.y)]?.[Math.round(t.x)]?.ground ?? 'void';

/** Height Bodies stand at on a tile. */
export const standHeight = (s: Scene, t: Pt) => TOP[groundAt(s, t)] ?? 0;

/** A wall or house is cut away when it's in front of something walkable (the camera looks from +x, +z). */
export function isFront(s: Scene, x: number, z: number): boolean {
    const g = (dx: number, dz: number) =>
        s.cells[z + dz]?.[x + dx]?.ground ?? 'void';
    return (
        WALKABLE.has(g(-1, 0)) ||
        WALKABLE.has(g(0, -1)) ||
        g(-1, 0) === 'water' ||
        g(0, -1) === 'water'
    );
}

/** Where a back wall or house faces: toward its walkable neighbour (+z or +x), else null. */
export function facing(s: Scene, x: number, z: number): 'z' | 'x' | null {
    const g = (dx: number, dz: number) =>
        s.cells[z + dz]?.[x + dx]?.ground ?? 'void';
    const open = (gr: Ground) => WALKABLE.has(gr) || gr === 'water';
    if (open(g(0, 1)) && !BACK.has(g(0, 1))) return 'z';
    if (open(g(1, 0)) && !BACK.has(g(1, 0))) return 'x';
    return null;
}

/** The tile Bodies use coming from (or going to) another scene. */
export function entrance(s: Scene, otherScene?: string | null): Pt {
    const e = s.def.entrances ?? {};
    const at = (otherScene && e[otherScene]) || e['*'];
    if (at) return { x: at[0], y: at[1] };
    for (let z = 0; z < s.h; z++)
        for (let x = 0; x < s.w; x++)
            if (s.cells[z][x].ground === 'door') return { x, y: z };
    return { x: 0, y: 0 };
}

const paths = new Map<string, Pt[]>();

const connected = (p: Pt[]) =>
    p.every(
        (t, i) =>
            i === 0 ||
            Math.abs(t.x - p[i - 1].x) + Math.abs(t.y - p[i - 1].y) === 1,
    );

/** A* on the scene grid, memoised: Bodies walking in and out ask every frame. Seats inside furniture are slid into from the nearest free tile. */
export function pathIn(s: Scene, from: Pt, to: Pt): Pt[] {
    const k = `${s.def.id}:${s.blocked.size}:${key(from)}>${key(to)}`;
    const hit = paths.get(k);
    if (hit) return hit;
    let p = findPath(from, to, s.w, s.h, s.blocked);
    if (!connected(p) && s.blocked.has(key(to))) {
        const near: Pt[] = [];
        for (let dz = -2; dz <= 2; dz++)
            for (let dx = -2; dx <= 2; dx++) {
                const t = { x: to.x + dx, y: to.y + dz };
                if (
                    t.x >= 0 &&
                    t.y >= 0 &&
                    t.x < s.w &&
                    t.y < s.h &&
                    !s.blocked.has(key(t))
                )
                    near.push(t);
            }
        near.sort((a, b) => dist(a, to) - dist(b, to));
        for (const n of near) {
            const q = findPath(from, n, s.w, s.h, s.blocked);
            if (connected(q)) {
                p = [...q, to];
                break;
            }
        }
    }
    if (paths.size > 500) paths.clear();
    paths.set(k, p);
    return p;
}

/** The scene with things standing where they are now: their tiles blocked, standing room on them dropped. */
export function withFurni(s: Scene, placed: PlacedProp[]): Scene {
    const extra = placed
        .filter((p) => p.prefab.solid !== false && !p.prefab.wall)
        .flatMap(footprint)
        .map(key);
    if (extra.every((k) => s.blocked.has(k))) return s;
    const blocked = new Set([...s.blocked, ...extra]);
    const spots = Object.fromEntries(
        Object.entries(s.spots).map(([id, sp]) => [
            id,
            {
                ...sp,
                stands: sp.stands.filter((t) => !blocked.has(key(t.tile))),
            },
        ]),
    );
    return { ...s, blocked, spots };
}

/** Nearest spot to a clicked tile, within a few tiles. */
export function spotNear(s: Scene, t: Pt): string | null {
    let best: string | null = null;
    let bestD = 3.5;
    for (const [id, sp] of Object.entries(s.spots)) {
        const tiles = [
            sp.at,
            ...sp.seats.map((x) => x.tile),
            ...sp.stands.map((x) => x.tile),
        ];
        const d = Math.min(...tiles.map((x) => dist(x, t)));
        if (d < bestD) {
            best = id;
            bestD = d;
        }
    }
    return best;
}
