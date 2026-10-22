import type { FurniView } from '../../../api/model/furniView';
import type { Place } from '../../../api/model/place';
import type { Pt } from '../iso/iso';
import { type Part, type Prefab, pick, rng, turned } from './prefab';
import { FURNI_PREFAB, PREFABS, prefabForSpot } from './prefabs';
import {
    key,
    type PlacedProp,
    parseScene,
    type Scene,
    type SceneDef,
    WALKABLE,
} from './scene';

/** Authored scenes (ADR 0023): draw the map, drop props by name, pin the backend's spots and furni. */

const cafe: SceneDef = {
    id: 'cafe',
    floor: '#b98a5a',
    wall: '#e6d7b8',
    map: `
        ##ww#######
        #.........#
        #.........#
        #.........#
        w.........#
        #.........#
        D.........#
        #.........#
        ###########
    `,
    props: [
        { p: 'booth', at: [1, 1], spot: 'booth' },
        { p: 'counter', at: [6, 1] },
        { p: 'stool', at: [6, 2], rot: 2, spot: 'counter' },
        { p: 'stool', at: [8, 2], rot: 2, spot: 'counter' },
        { p: 'chalkboard', at: [4, 1] },
        { p: 'table', at: [2, 4] },
        { p: 'chair', at: [1, 4], rot: 1, spot: 'window-table' },
        { p: 'chair', at: [3, 4], rot: 3, spot: 'window-table' },
        { p: 'table', at: [6, 5] },
        { p: 'chair', at: [6, 6], rot: 2 },
        { p: 'chair', at: [7, 5], rot: 3 },
        { p: 'table', at: [8, 7] },
        { p: 'chair', at: [7, 7], rot: 1 },
        { p: 'big-plant', at: [1, 7] },
        { p: 'lamp', at: [9, 4] },
    ],
    spots: {
        counter: { at: [7, 2] },
        booth: { at: [2, 2] },
        'window-table': { at: [2, 5] },
        jukebox: { at: [9, 2] },
    },
    furni: {
        espresso: { at: [8, 1], y: 1.04 },
        cups: { at: [6, 1], y: 1.04 },
        jukebox: { at: [9, 1] },
        plant: { at: [2, 4], y: 0.76 },
    },
};

const lobby: SceneDef = {
    id: 'lobby',
    floor: '#7a2f3d',
    wall: '#cdbd9c',
    map: `
        #####ww#######
        #............#
        w............#
        w............#
        #............#
        #............#
        #............#
        #............#
        #............#
        ######DD######
    `,
    props: [
        { p: 'reception', at: [8, 1] },
        { p: 'noticeboard', at: [3, 1] },
        { p: 'painting', at: [12, 1] },
        { p: 'window-bench', at: [1, 2], rot: 1, spot: 'window' },
        { p: 'rug', at: [3, 5], tint: { rug: '#c9a227' } },
        { p: 'sofa', at: [3, 4], spot: 'sofas', tint: { fabric: '#2f5d50' } },
        {
            p: 'sofa',
            at: [3, 7],
            rot: 2,
            spot: 'sofas',
            tint: { fabric: '#2f5d50' },
        },
        { p: 'coffee-table', at: [4, 5] },
        { p: 'rug', at: [6, 7], tint: { rug: '#8b1e2d' } },
        { p: 'armchair', at: [11, 4], rot: 3, tint: { fabric: '#6d4c41' } },
        { p: 'armchair', at: [11, 6], rot: 3, tint: { fabric: '#6d4c41' } },
        { p: 'table', at: [11, 5] },
        { p: 'big-plant', at: [12, 1] },
        { p: 'big-plant', at: [12, 8] },
        { p: 'lamp', at: [1, 8] },
    ],
    spots: {
        entrance: { at: [6, 8] },
        sofas: { at: [3, 6] },
        desk: { at: [9, 2] },
        window: { at: [2, 3] },
    },
    furni: {
        plant: { p: 'big-plant', at: [1, 4] },
        lamp: { p: 'desk-lamp', at: [10, 1], y: 1.06 },
    },
};

const home: SceneDef = {
    id: 'home',
    floor: '#c8a173',
    wall: '#e9dcc4',
    map: `
        ##w###w##
        #.......#
        #.......#
        w.......#
        #.......#
        D.......#
        #.......#
        #########
    `,
    props: [
        { p: 'kitchen', at: [5, 1] },
        { p: 'fridge', at: [7, 1] },
        { p: 'chair', at: [3, 2], rot: 2, spot: 'desk' },
        { p: 'rug', at: [2, 5] },
        { p: 'sofa', at: [2, 4], spot: 'sofa' },
        { p: 'coffee-table', at: [3, 5] },
    ],
    spots: {
        sofa: { at: [3, 5] },
        desk: { at: [3, 2], r: 1 },
        kitchen: { at: [6, 2] },
        bed: { at: [1, 3], r: 1 },
    },
    furni: {
        bed: { at: [1, 1], spot: 'bed' },
        laptop: { at: [3, 1] },
        coffee: { at: [5, 1], y: 0.93 },
        cups: { at: [6, 1], y: 0.93 },
        plant: { at: [1, 3] },
    },
};

const centre: SceneDef = {
    id: 'centre',
    outdoor: true,
    map: `
        HHHHHHHPOPHHHHH==HHCHHHHHHHH
        H-----------B--==------B----
        H-==========================
        H-=--T-i-T-i-T-==-B-T-i-T-i-
        H-=-~~~~~~~~~~~bb~~~~~~~~~~~
        H-=-~~~~~~~~~~~bb~~~~~~~~~~~
        H-=-~~~~~~~~~~~bb~~~~~~~~~~~
        H-=-~~~---L-T-i==-T----T-L-T
        H-=-~~~-====================
        H-=T~~~-=------==-----------
        H-=i~~~-=------==-----------
        H-=-~~~T=------==-----------
        H-=-bbb-=------==-----------
        H-=-bbb-=------==---gggtgg--
        H-=B~~~-=------==---gtgggg--
        H-=-~~~T=------==---gggggg--
        H-=T~~~-=------==---ggggtg--
        H-=-~~~L=------==---gggggg--
        H-=-~~~T=------==-----------
        H-=-~~~-=------==-----------
    `,
    legend: {
        C: {
            ground: 'house',
            house: {
                sign: 'CAFÉ',
                awning: '#2e6b4f',
                brick: '#5e2c22',
                storeys: 3,
                gable: 'neck',
            },
        },
        O: {
            ground: 'house',
            house: {
                sign: 'LOBBY',
                awning: '#7a2f3d',
                brick: '#e7e0d0',
                storeys: 4,
                gable: 'cornice',
            },
        },
        P: {
            ground: 'house',
            house: { brick: '#e7e0d0', storeys: 4, gable: 'cornice' },
        },
    },
    props: [
        { p: 'bench', at: [20, 7], rot: 2, spot: 'bench' },
        { p: 'houseboat', at: [20, 4], rot: 2 },
        { p: 'houseboat', at: [4, 15], rot: 1 },
        { p: 'steps', at: [6, 9], rot: 3, spot: 'steps' },
        { p: 'steps', at: [6, 10], rot: 3, spot: 'steps' },
        { p: 'herring-cart', at: [10, 11] },
        { p: 'bike-rack', at: [10, 9] },
        { p: 'bike-rack', at: [17, 10], rot: 1 },
        { p: 'terrace-table', at: [18, 1] },
        { p: 'terrace-table', at: [21, 1] },
        { p: 'chair', at: [20, 1], rot: 3 },
        { p: 'chair', at: [22, 1], rot: 3 },
        { p: 'bench', at: [22, 12] },
        { p: 'duck', at: [10, 5] },
        { p: 'duck', at: [11, 4] },
        { p: 'duck', at: [5, 17] },
        { p: 'elm', at: [10, 14] },
        { p: 'elm', at: [13, 17] },
        { p: 'elm', at: [18, 15] },
        { p: 'bike-rack', at: [12, 15] },
        { p: 'bench', at: [10, 17], rot: 1 },
        { p: 'planter', at: [19, 9] },
        { p: 'street-lamp', at: [14, 13] },
    ],
    spots: {
        bridge: { at: [15, 5], r: 1 },
        bench: { at: [21, 8], r: 1 },
        steps: { at: [7, 10], r: 1 },
        cart: { at: [11, 12] },
        bikes: { at: [10, 10], r: 1 },
    },
    furni: {
        lamp: { p: 'street-lamp', at: [16, 4] },
        planters: { p: 'planter', at: [22, 7], rot: 2 },
        coffee: { p: 'coffee-cart', at: [12, 11] },
    },
    entrances: { cafe: [19, 1], lobby: [8, 1], '*': [16, 19] },
    focus: [14, 8],
};

export const SCENES: Record<string, SceneDef> = { cafe, lobby, home, centre };

const hash = (s: string) => {
    let h = 2166136261;
    for (const ch of s) h = Math.imul(h ^ ch.charCodeAt(0), 16777619);
    return h >>> 0;
};

const WALLS = [
    '#e9dcc4',
    '#d8e2dc',
    '#f1d6c4',
    '#cfd8e6',
    '#e8e1f0',
    '#f3e6b8',
    '#d7ccc8',
];
const FLOORS = ['#c8a173', '#a67b5b', '#d9c3a0', '#8d6e63', '#b0a090'];
const FABRICS = [
    '#b8574a',
    '#4a6b8a',
    '#6b8e5a',
    '#8e5cc4',
    '#c9a227',
    '#3d405b',
    '#e07a5f',
];

/** Each Home looks a bit different, stable per Place. */
function homeFor(place: Place): SceneDef {
    const r = rng(hash(place.id ?? ''));
    const fabric = pick(r, FABRICS);
    return {
        ...home,
        wall: pick(r, WALLS),
        floor: pick(r, FLOORS),
        props: home.props?.map((p) =>
            p.p === 'sofa'
                ? { ...p, tint: { fabric } }
                : p.p === 'rug'
                  ? { ...p, tint: { rug: pick(r, FABRICS) } }
                  : p,
        ),
        furni: {
            ...home.furni,
            bed: {
                ...home.furni?.bed,
                at: [1, 1],
                tint: { blanket: pick(r, FABRICS) },
            },
        },
    };
}

/** A plain room for a Place nobody drew: one prop per spot, guessed from its name. */
export function generateScene(place: Place): SceneDef {
    const spots = place.spots ?? [];
    const cols = Math.max(2, Math.ceil(spots.length / 2));
    const w = cols * 4 + 2;
    const h = 9;
    const lines = Array.from({ length: h }, (_, z) =>
        Array.from({ length: w }, (_, x) => {
            if (x === 0 && z === 4) return 'D';
            if (x === 0 || z === 0 || x === w - 1 || z === h - 1) return '#';
            return '.';
        }).join(''),
    );
    const def: SceneDef = {
        id: `gen:${place.id}`,
        map: lines.join('\n'),
        props: [],
        spots: {},
    };
    spots.forEach((s, i) => {
        const at: [number, number] = [2 + Math.floor(i / 2) * 4, i % 2 ? 5 : 1];
        const p = prefabForSpot(s.name ?? s.id ?? '');
        const front = i % 2 === 0;
        if (p)
            def.props?.push({
                p,
                at,
                rot: front ? 0 : 2,
                spot: s.id ?? undefined,
            });
        def.spots[s.id ?? ''] = { at: [at[0], front ? at[1] + 2 : at[1] - 1] };
    });
    return def;
}

export function sceneDefFor(place: Place): SceneDef {
    const base = baseDef(place);
    return {
        ...base,
        wall: place.wallColour ?? base.wall,
        floor: place.floorColour ?? base.floor,
    };
}

function baseDef(place: Place): SceneDef {
    if (place.customScene) {
        try {
            const custom = JSON.parse(place.customScene) as Omit<
                SceneDef,
                'id'
            >;
            return { ...custom, id: `custom:${place.id}` };
        } catch {
            return generateScene(place);
        }
    }
    if (place.scene === 'home' || place.kind === 'home') return homeFor(place);
    const def = place.scene ? SCENES[place.scene] : undefined;
    return def ?? generateScene(place);
}

const parsed = new Map<string, Scene>();

/** Parsed scene for a Place, cached per Place and its spots. */
export function sceneFor(place: Place): Scene {
    const k = `${place.id}:${place.scene}:${place.wallColour}:${place.floorColour}:${place.customScene?.length}:${(place.spots ?? []).map((s) => s.id).join(',')}`;
    const hit = parsed.get(k);
    if (hit) return hit;
    try {
        const s = parse(sceneDefFor(place));
        parsed.set(k, s);
        return s;
    } catch {
        // A made scene that names something this client can't draw still gets a room.
        const s = parse(generateScene(place));
        parsed.set(k, s);
        return s;
    }
}

function parse(def: SceneDef): Scene {
    const furniProps = Object.fromEntries(
        Object.entries(def.furni ?? {}).map(([id, f]) => [
            id,
            { ...f, p: f.p ?? FURNI_PREFAB[id] ?? id },
        ]),
    );
    const s = parseScene(
        {
            ...def,
            props: [
                ...(def.props ?? []),
                ...Object.entries(furniProps).map(([, f]) => f),
            ],
        },
        PREFABS,
    );
    // Tag the furni placements so state can find them.
    const ids = Object.keys(furniProps);
    const first = s.props.length - ids.length;
    ids.forEach((id, i) => {
        s.props[first + i].furniId = id;
    });
    return s;
}

const models = new Map<string, Prefab>();

/** A generated model (backend ThingModel parts) as a prefab. */
export function modelPrefab(json: string): Prefab | null {
    const hit = models.get(json);
    if (hit) return hit;
    try {
        const m = JSON.parse(json) as { wall?: boolean; parts?: Part[] };
        if (!m.parts?.length) return null;
        const prefab: Prefab = {
            wall: !!m.wall,
            solid: !m.wall,
            parts: m.parts,
        };
        models.set(json, prefab);
        return prefab;
    } catch {
        return null;
    }
}

/** Every furni of a Place where it stands: the owner's spot if they moved it, else pinned by the scene, else loose; drawn from its model when it has one. */
export function placeFurni(scene: Scene, furni: FurniView[]): PlacedProp[] {
    const byId = new Map(furni.map((f) => [f.furniId ?? '', f]));
    const moved = (f?: FurniView) => f?.tileX != null && f.tileZ != null;
    const pinned = scene.props.filter(
        (p) => p.furniId && byId.has(p.furniId) && !moved(byId.get(p.furniId)),
    );
    const pinnedIds = new Set(pinned.map((p) => p.furniId));
    const loose = placeLoose(
        scene,
        furni.filter((f) => !moved(f) && !pinnedIds.has(f.furniId ?? '')),
    );
    const placed: PlacedProp[] = [...pinned, ...loose];
    for (const f of furni.filter(moved)) {
        const from = scene.props.find((p) => p.furniId === f.furniId);
        const name =
            (f.prefab && PREFABS[f.prefab] ? f.prefab : undefined) ??
            from?.p ??
            (f.kind === 'decor' && f.style
                ? (FURNI_PREFAB[f.style] ?? 'thing')
                : (FURNI_PREFAB[f.kind ?? ''] ?? 'thing'));
        const prefab = PREFABS[name] ?? PREFABS.thing;
        const rot = Number(f.rot ?? 0);
        placed.push({
            ...(from ?? {}),
            p: name,
            at: [Number(f.tileX ?? 0), Number(f.tileZ ?? 0)],
            rot,
            prefab,
            size: turned(prefab.size ?? [1, 1], rot),
            seed: from?.seed ?? hash(f.furniId ?? ''),
            furniId: f.furniId ?? undefined,
        });
    }
    return placed.map((p) => {
        // A catalogue model picked for this thing (ADR 0027) wins over its kind and a generated model.
        const cat = byId.get(p.furniId ?? '')?.prefab;
        if (cat && PREFABS[cat])
            return {
                ...p,
                p: cat,
                prefab: PREFABS[cat],
                size: turned(PREFABS[cat].size ?? [1, 1], p.rot),
            };
        const model = byId.get(p.furniId ?? '')?.model;
        const prefab = model ? modelPrefab(model) : null;
        return prefab
            ? {
                  ...p,
                  p: 'model',
                  prefab,
                  size: turned([1, 1], p.rot),
                  y: prefab.wall ? 0 : p.y,
              }
            : p;
    });
}

/** Where furni the scene didn't pin go: wall decor on back walls, the rest on free floor near the walls. */
export function placeLoose(scene: Scene, furni: FurniView[]): PlacedProp[] {
    const pinned = new Set(scene.props.map((p) => p.furniId).filter(Boolean));
    const loose = furni.filter((f) => !pinned.has(f.furniId ?? ''));
    if (loose.length === 0) return [];
    const reserved = new Set<string>();
    for (const s of Object.values(scene.spots))
        for (const t of s.stands) reserved.add(key(t.tile));
    const ground = (x: number, z: number) =>
        scene.cells[z]?.[x]?.ground ?? 'void';
    const isWall = (x: number, z: number) =>
        ['wall', 'window', 'house'].includes(ground(x, z));
    const walls: { tile: Pt; rot: number }[] = [];
    const floors: Pt[] = [];
    for (let z = 0; z < scene.h; z++)
        for (let x = 0; x < scene.w; x++) {
            if (!WALKABLE.has(ground(x, z)) || ground(x, z) === 'door')
                continue;
            if (isWall(x, z - 1) && ground(x, z - 1) !== 'window')
                walls.push({ tile: { x, y: z }, rot: 0 });
            else if (isWall(x - 1, z) && ground(x - 1, z) !== 'window')
                walls.push({ tile: { x, y: z }, rot: 1 });
            const k = key({ x, y: z });
            if (
                !scene.blocked.has(k) &&
                !reserved.has(k) &&
                (isWall(x, z - 1) ||
                    isWall(x - 1, z) ||
                    isWall(x + 1, z) ||
                    isWall(x, z + 1))
            )
                floors.push({ x, y: z });
        }
    const usedWalls = new Set(
        scene.props
            .filter((p) => p.prefab.wall)
            .map((p) => key({ x: p.at[0], y: p.at[1] })),
    );
    const freeWalls = walls.filter((w) => !usedWalls.has(key(w.tile)));
    const out: PlacedProp[] = [];
    let wi = 0;
    let fi = 0;
    for (const f of loose) {
        const name = f.style && f.kind === 'decor' ? f.style : (f.kind ?? '');
        const p = FURNI_PREFAB[name] ?? (PREFABS[name] ? name : 'thing');
        const prefab = PREFABS[p];
        let at: Pt | undefined;
        let rot = 0;
        if (prefab.wall && wi < freeWalls.length) {
            at = freeWalls[wi].tile;
            rot = freeWalls[wi].rot;
            wi += 2;
        } else if (fi < floors.length) {
            at = floors[fi];
            fi += 2;
        }
        if (!at) continue;
        out.push({
            p,
            at: [at.x, at.y],
            rot,
            prefab,
            size: turned(prefab.size ?? [1, 1], rot),
            seed: hash(f.furniId ?? ''),
            furniId: f.furniId ?? undefined,
        });
    }
    return out;
}
