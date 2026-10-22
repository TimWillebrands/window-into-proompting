import { pick, rng } from './prefab';
import { PREFABS } from './prefabs';
import type { PropDef, SceneDef } from './scene';

/** Kit scenes (ADR 0027): how a Locale looks once it opens, by archetype. Spot ids are backend kits.json's (a test checks). */
const room = [
    '##ww####ww####',
    '#............#',
    '#............#',
    '#............#',
    '#............#',
    'D............#',
    '#............#',
    '#............#',
    '##############',
].join('\n');

const kit = (
    props: PropDef[],
    spots: SceneDef['spots'],
    look: Partial<SceneDef> = {},
): Omit<SceneDef, 'id'> => ({ map: room, props, spots, ...look });

export const KIT_SCENES: Record<string, Omit<SceneDef, 'id'>> = {
    office: kit(
        [
            { p: 'k-desk', at: [1, 1], spot: 'desks' },
            { p: 'k-screen', at: [1, 1], y: 0.78 },
            { p: 'k-desk-chair', at: [1, 2], rot: 2, spot: 'desks' },
            { p: 'k-desk', at: [4, 1], spot: 'desks' },
            { p: 'k-screen', at: [5, 1], y: 0.78 },
            {
                p: 'k-desk-chair',
                at: [4, 2],
                rot: 2,
                spot: 'desks',
                tint: { fabric: '#8c3d3d' },
            },
            { p: 'k-potted-plant', at: [6, 1] },
            { p: 'k-kitchen-bar', at: [9, 1] },
            { p: 'k-coffee-machine', at: [10, 1], y: 0.84 },
            { p: 'k-bookcase', at: [12, 1] },
            { p: 'long-table', at: [9, 5] },
            { p: 'k-chair', at: [9, 4], spot: 'meeting' },
            { p: 'k-chair', at: [10, 4], spot: 'meeting' },
            { p: 'k-chair', at: [9, 6], rot: 2, spot: 'meeting' },
            { p: 'k-chair', at: [10, 6], rot: 2, spot: 'meeting' },
        ],
        {
            desks: { at: [3, 3] },
            coffee: { at: [10, 2] },
            meeting: { at: [11, 5] },
            window: { at: [7, 2] },
        },
        { floor: '#9aa3a8', wall: '#e9e6df' },
    ),
    cafe: kit(
        [
            { p: 'counter', at: [8, 1], spot: 'counter' },
            { p: 'k-coffee-machine', at: [9, 1], y: 1.04 },
            { p: 'k-round-table', at: [2, 2] },
            { p: 'k-chair', at: [2, 1], spot: 'window-table' },
            { p: 'k-chair', at: [2, 3], rot: 2, spot: 'window-table' },
            { p: 'k-cloth-table', at: [5, 5] },
            { p: 'k-chair', at: [5, 4], spot: 'big-table' },
            { p: 'k-chair', at: [6, 4], spot: 'big-table' },
            { p: 'k-chair', at: [5, 6], rot: 2, spot: 'big-table' },
            { p: 'k-chair', at: [6, 6], rot: 2, spot: 'big-table' },
            { p: 'k-potted-plant', at: [12, 1] },
        ],
        {
            counter: { at: [9, 2] },
            'window-table': { at: [3, 2] },
            'big-table': { at: [7, 5] },
        },
        { floor: '#b98a5a', wall: '#e6d7b8' },
    ),
    bar: kit(
        [
            { p: 'k-kitchen-bar', at: [7, 1], spot: 'bar' },
            { p: 'k-kitchen-bar', at: [9, 1], spot: 'bar' },
            { p: 'k-bar-stool', at: [7, 2], spot: 'bar' },
            { p: 'k-bar-stool', at: [8, 2], spot: 'bar' },
            { p: 'k-bar-stool', at: [9, 2], spot: 'bar' },
            { p: 'k-barrel', at: [12, 1] },
            { p: 'k-round-table', at: [4, 5] },
            { p: 'k-barrel', at: [6, 5] },
            { p: 'k-lounge-sofa', at: [1, 1], spot: 'sofa' },
            { p: 'coffee-table', at: [1, 2] },
            { p: 'jukebox', at: [12, 7] },
        ],
        {
            bar: { at: [8, 3] },
            'high-tables': { at: [5, 6] },
            sofa: { at: [3, 2] },
        },
        { floor: '#5b3a22', wall: '#7a4e3a' },
    ),
    restaurant: kit(
        [
            { p: 'k-kitchen-bar', at: [8, 1], spot: 'pass' },
            { p: 'k-stove', at: [10, 1] },
            { p: 'k-fridge', at: [12, 1] },
            { p: 'booth', at: [1, 1], spot: 'booth' },
            { p: 'k-cloth-table', at: [5, 5] },
            { p: 'k-chair', at: [5, 4], spot: 'tables' },
            { p: 'k-chair', at: [6, 4], spot: 'tables' },
            { p: 'k-chair', at: [5, 6], rot: 2, spot: 'tables' },
            { p: 'k-chair', at: [6, 6], rot: 2, spot: 'tables' },
            { p: 'k-potted-plant', at: [12, 7] },
        ],
        {
            pass: { at: [9, 2] },
            tables: { at: [7, 5] },
            booth: { at: [3, 2] },
        },
        { floor: '#8a5a3c', wall: '#efe3c8' },
    ),
    shop: kit(
        [
            { p: 'counter', at: [2, 6], spot: 'till' },
            { p: 'register', at: [3, 6], y: 0.98 },
            { p: 'k-shelves', at: [6, 1] },
            { p: 'k-shelves', at: [6, 2] },
            { p: 'k-shelves', at: [6, 3] },
            { p: 'k-shelves', at: [9, 1] },
            { p: 'k-shelves', at: [9, 2] },
            { p: 'k-shelves', at: [9, 3] },
            { p: 'k-carton', at: [12, 1] },
            { p: 'k-carton', at: [12, 2] },
            { p: 'potted-palm', at: [12, 7] },
        ],
        {
            till: { at: [3, 5] },
            aisles: { at: [7, 3] },
            window: { at: [11, 6] },
        },
        { floor: '#d8d2c4', wall: '#f3efe6' },
    ),
    gym: kit(
        [
            { p: 'reception', at: [2, 1], spot: 'front-desk' },
            { p: 'bench', at: [8, 1] },
            { p: 'bench', at: [11, 1] },
            { p: 'speaker', at: [12, 7] },
            { p: 'rug', at: [8, 5] },
            { p: 'mirror', at: [6, 1] },
        ],
        {
            'front-desk': { at: [3, 2] },
            machines: { at: [9, 2] },
            mats: { at: [9, 6] },
        },
        { floor: '#4a5560', wall: '#d9dee5' },
    ),
    station: kit(
        [
            { p: 'bench', at: [2, 1], spot: 'benches' },
            { p: 'coffee-cart', at: [12, 6], spot: 'kiosk' },
            { p: 'vending-machine', at: [12, 1] },
            { p: 'clock', at: [7, 1] },
        ],
        {
            platform: { at: [7, 3] },
            kiosk: { at: [11, 6] },
            benches: { at: [3, 2] },
        },
        { floor: '#b8b1a3', wall: '#d6cfc2' },
    ),
    school: kit(
        [
            { p: 'chalkboard', at: [3, 1] },
            { p: 'k-chair', at: [2, 3], spot: 'classroom' },
            { p: 'k-chair', at: [3, 3], spot: 'classroom' },
            { p: 'k-chair', at: [4, 3], spot: 'classroom' },
            { p: 'k-chair', at: [2, 4], spot: 'classroom' },
            { p: 'k-bookcase', at: [10, 1] },
            { p: 'k-bookcase', at: [11, 1] },
            { p: 'k-bookcase', at: [12, 1] },
            { p: 'long-table', at: [10, 4] },
            { p: 'k-chair', at: [10, 3], spot: 'library' },
            { p: 'k-chair', at: [11, 3], spot: 'library' },
        ],
        {
            classroom: { at: [3, 5] },
            hallway: { at: [7, 6] },
            library: { at: [12, 4] },
        },
        { floor: '#c9b48a', wall: '#e8e2cf' },
    ),
    clinic: kit(
        [
            { p: 'reception', at: [8, 1], spot: 'reception' },
            { p: 'k-hospital-sign', at: [12, 1] },
            { p: 'k-chair', at: [2, 1], spot: 'waiting' },
            { p: 'k-chair', at: [3, 1], spot: 'waiting' },
            { p: 'k-chair', at: [4, 1], spot: 'waiting' },
            { p: 'k-chair', at: [5, 1], spot: 'waiting' },
            { p: 'big-plant', at: [1, 1] },
        ],
        {
            reception: { at: [9, 2] },
            waiting: { at: [3, 3] },
        },
        { floor: '#dfe6ee', wall: '#f3f6f8' },
    ),
    place: kit(
        [
            { p: 'armchair', at: [12, 1], spot: 'corner' },
            { p: 'big-plant', at: [11, 1] },
            { p: 'rug', at: [5, 3] },
        ],
        {
            entrance: { at: [1, 5] },
            main: { at: [6, 4] },
            corner: { at: [11, 2] },
        },
    ),
    park: {
        outdoor: true,
        map: [
            'HHHHHHHHHHHHHHHH',
            'gggggggggggggggg',
            'ggg--------ggggg',
            'gg-ggggggg-ggggg',
            'gg-gg~~~gg-ggggg',
            'gg-gg~~~gg-ggggg',
            'gg-ggggggg-ggggg',
            'gg----------gggg',
            'gggggggggg-ggggg',
            'gggggggggg-ggggg',
        ].join('\n'),
        props: [
            { p: 'k-park-bench', at: [4, 2], spot: 'bench' },
            { p: 'k-tree-large', at: [12, 1] },
            { p: 'k-tree-fall', at: [0, 8] },
            { p: 'k-potted-plant', at: [9, 3] },
        ],
        spots: {
            bench: { at: [5, 3] },
            lawn: { at: [13, 5] },
            pond: { at: [7, 6] },
        },
        entrances: { '*': [10, 9] },
    },
};

const FLOORS = [
    '#9aa3a8',
    '#b98a5a',
    '#c9b48a',
    '#8a5a3c',
    '#d8d2c4',
    '#6f7d6a',
];
const WALLS = [
    '#e9e6df',
    '#e6d7b8',
    '#efe3c8',
    '#d9dee5',
    '#e8d5cf',
    '#dfe8dc',
];
const FABRICS = [
    '#3d6b8c',
    '#8c3d3d',
    '#5a7d6a',
    '#b8862f',
    '#6b4c8a',
    '#2f3136',
];

/** One Place's take on its kit: floor, walls and fabrics picked from its id, so two offices don't look alike. */
export function variedKit(
    def: Omit<SceneDef, 'id'>,
    placeId: string,
): Omit<SceneDef, 'id'> {
    if (def.outdoor) return def;
    let h = 7;
    for (const c of placeId) h = (h * 31 + c.charCodeAt(0)) >>> 0;
    const r = rng(h);
    const fabric = pick(r, FABRICS);
    return {
        ...def,
        floor: pick(r, FLOORS),
        wall: pick(r, WALLS),
        props: (def.props ?? []).map((p) =>
            PREFABS[p.p]?.tint?.fabric
                ? {
                      ...p,
                      tint: { ...p.tint, fabric: p.tint?.fabric ?? fabric },
                  }
                : p,
        ),
    };
}
