import { CATALOGUE } from './catalogue.gen';
import {
    type Part,
    type Prefab,
    type PrefabVariant,
    pick,
    rng,
    type Vec3,
} from './prefab';
import type { HouseStyle } from './scene';

/** The prefab library (ADR 0023). Add one here and use it by name in any scene. */

const C = {
    wood: '#a0703f',
    darkWood: '#5b3a22',
    lightWood: '#d6ae74',
    metal: '#a7b0ba',
    chrome: '#d9dee5',
    dark: '#2f3136',
    white: '#f3efe6',
    cream: '#efe3c8',
    leaf: '#4f9a43',
    dryLeaf: '#a58f3e',
    pot: '#b5562f',
    glass: '#9fc7e6',
    roof: '#3d3431',
    iron: '#24302a',
    bollard: '#6b1d1d',
};

const box = (
    w: number,
    h: number,
    d: number,
    x: number,
    y: number,
    z: number,
    c: string,
    o: Partial<Part> = {},
): Part => ({ box: [w, h, d], at: [x, y, z], c, ...o }) as Part;

const cyl = (
    r: number,
    h: number,
    x: number,
    y: number,
    z: number,
    c: string,
    o: Partial<Part> = {},
): Part => ({ cyl: [r, h], at: [x, y, z], c, ...o }) as Part;

const ball = (
    r: number,
    x: number,
    y: number,
    z: number,
    c: string,
    o: Partial<Part> = {},
): Part => ({ ball: r, at: [x, y, z], c, ...o }) as Part;

const legs = (w: number, d: number, h: number, c: string, r = 0.03): Part[] =>
    [
        [-1, -1],
        [1, -1],
        [-1, 1],
        [1, 1],
    ].map(([sx, sz]) =>
        cyl(r, h, (sx * w) / 2, 0, (sz * d) / 2, c, { flat: true }),
    );

/** Lerp two hex colours. */
export function mix(a: string, b: string, t: number): string {
    const pa = Number.parseInt(a.slice(1), 16);
    const pb = Number.parseInt(b.slice(1), 16);
    const ch = (s: number) =>
        Math.round(((pa >> s) & 255) * (1 - t) + ((pb >> s) & 255) * t);
    return `#${((ch(16) << 16) | (ch(8) << 8) | ch(0)).toString(16).padStart(6, '0')}`;
}

const BIKE_COLOURS = [
    '#1d1d1d',
    '#2c4f7c',
    '#7a1f1f',
    '#2f5d3a',
    '#d8d2c4',
    '#5a3f6b',
    '#c46d2a',
];

function bikeParts(r: () => number, x: number, z: number, ry = 0): Part[] {
    const c = pick(r, BIKE_COLOURS);
    const [dx, dz] = [Math.cos(ry) * 0.28, -Math.sin(ry) * 0.28];
    return [
        {
            ring: [0.17, 0.025],
            at: [x - dx, 0.19, z - dz],
            c: C.dark,
            ry,
            flat: true,
        },
        {
            ring: [0.17, 0.025],
            at: [x + dx, 0.19, z + dz],
            c: C.dark,
            ry,
            flat: true,
        },
        box(0.56, 0.04, 0.04, x, 0.3, z, c, { ry }),
        box(0.04, 0.24, 0.04, x - dx * 0.4, 0.22, z - dz * 0.4, c, { ry }),
        box(0.16, 0.04, 0.08, x - dx * 0.45, 0.47, z - dz * 0.45, C.dark, {
            ry,
        }),
        box(0.04, 0.3, 0.04, x + dx * 0.85, 0.2, z + dz * 0.85, c, { ry }),
        box(0.04, 0.04, 0.3, x + dx * 0.85, 0.5, z + dz * 0.85, C.dark, { ry }),
        box(0.18, 0.05, 0.16, x + dx * 1.15, 0.36, z + dz * 1.15, C.darkWood, {
            ry,
            flat: true,
        }),
    ];
}

/** A plant's leaves, browning and drooping as it dries out. */
function leaves(v: PrefabVariant, y: number, r: number): Part[] {
    const c = v.state?.condition ?? 1;
    const colour = mix(C.dryLeaf, C.leaf, Math.min(1, c * 1.4));
    const droop = c < 0.35 ? 0.6 : 1;
    return [
        ball(r, 0, y + r * 0.6 * droop, 0, colour, {
            squash: droop,
            tag: 'leaf',
        }),
        ball(
            r * 0.75,
            -r * 0.6,
            y + r * 0.2,
            r * 0.3,
            mix(colour, '#2d5a28', 0.2),
            { squash: droop },
        ),
        ball(r * 0.7, r * 0.55, y + r * 0.3 * droop, -r * 0.2, colour, {
            squash: droop,
        }),
    ];
}

/** Lamps light up unless broken, which flickers. */
const lampGlow = (v: PrefabVariant) =>
    v.state && v.state.condition < 0.5 ? 'flicker' : 'light';

const BRICKS = [
    '#7d3326',
    '#8e4a32',
    '#5e2c22',
    '#6d4a3a',
    '#a2583b',
    '#3f3a38',
    '#e7e0d0',
    '#2f3d47',
];

/** One narrow canal house, facade toward +z. */
function canalHouse(v: PrefabVariant, style: HouseStyle = {}): Part[] {
    const r = rng(v.seed);
    const storeys = style.storeys ?? 2 + Math.floor(r() * 3);
    const brick = style.brick ?? pick(r, BRICKS);
    const trim = brick === '#e7e0d0' ? '#38424a' : C.white;
    const gable =
        style.gable ?? pick(r, ['step', 'neck', 'bell', 'cornice'] as const);
    const ground = 1.0;
    const storey = 0.72;
    const top = ground + storeys * storey;
    const f = 0.5;
    const parts: Part[] = [box(1, top, 1, 0, 0, 0, brick)];
    // Ground floor: door with a stoop, and a shop window or a house window.
    const doorLeft = r() < 0.5;
    const dx = doorLeft ? -0.26 : 0.26;
    parts.push(
        box(
            0.3,
            0.72,
            0.03,
            dx,
            0.12,
            f + 0.01,
            pick(r, ['#21402d', '#2a2a2a', '#5b3a22', '#6b1d1d', '#23395b']),
        ),
        box(0.36, 0.06, 0.03, dx, 0.84, f + 0.012, trim),
        box(0.42, 0.12, 0.22, dx, 0, f + 0.1, '#77706a'),
        box(
            0.42,
            0.55,
            0.03,
            -dx * 0.9,
            0.3,
            f + 0.01,
            style.sign || r() < 0.6 ? '#ffe2a8' : '#33465a',
            {
                glow: 'night',
                tag: 'window',
            },
        ),
        box(0.48, 0.61, 0.02, -dx * 0.9, 0.27, f + 0.005, trim),
    );
    for (let s = 0; s < storeys; s++) {
        const y = ground + s * storey + 0.16;
        const lit = r() < 0.45;
        for (const wx of [-0.22, 0.22]) {
            parts.push(
                box(0.3, 0.5, 0.02, wx, y - 0.03, f + 0.005, trim),
                box(
                    0.24,
                    0.44,
                    0.03,
                    wx,
                    y,
                    f + 0.01,
                    lit ? '#ffd98a' : '#33465a',
                    {
                        glow: lit ? 'night' : undefined,
                        tag: 'window',
                    },
                ),
            );
        }
    }
    const roofZ = -0.1;
    switch (gable) {
        case 'step':
            parts.push(
                box(0.8, 0.28, 0.14, 0, top, f - 0.07, brick),
                box(0.54, 0.28, 0.14, 0, top + 0.28, f - 0.07, brick),
                box(0.28, 0.24, 0.14, 0, top + 0.56, f - 0.07, brick),
                {
                    gable: [0.84, 0.75, 0.86],
                    at: [0, top, roofZ - 0.05],
                    c: C.roof,
                    ry: Math.PI / 2,
                },
            );
            break;
        case 'neck':
            parts.push(
                box(0.46, 0.62, 0.14, 0, top, f - 0.07, brick),
                {
                    gable: [0.56, 0.2, 0.15],
                    at: [0, top + 0.62, f - 0.07],
                    c: trim,
                },
                ball(0.1, -0.3, top + 0.1, f - 0.04, trim, { squash: 1.4 }),
                ball(0.1, 0.3, top + 0.1, f - 0.04, trim, { squash: 1.4 }),
                {
                    gable: [0.84, 0.7, 0.86],
                    at: [0, top, roofZ - 0.05],
                    c: C.roof,
                    ry: Math.PI / 2,
                },
            );
            break;
        case 'bell':
            parts.push(
                {
                    cyl: [0.42, 0.14],
                    at: [0, top - 0.05, f - 0.14],
                    c: brick,
                    rx: Math.PI / 2,
                    sides: 16,
                },
                box(0.3, 0.2, 0.14, 0, top + 0.37, f - 0.07, brick),
                box(0.36, 0.05, 0.16, 0, top + 0.57, f - 0.07, trim),
                {
                    gable: [0.84, 0.66, 0.86],
                    at: [0, top, roofZ - 0.05],
                    c: C.roof,
                    ry: Math.PI / 2,
                },
            );
            break;
        default:
            parts.push(
                box(1.06, 0.12, 0.08, 0, top - 0.02, f + 0.02, trim),
                box(0.98, 0.32, 0.96, 0, top + 0.1, 0, C.roof),
            );
    }
    parts.push(box(0.06, 0.06, 0.32, 0, top - 0.1, f + 0.12, C.darkWood));
    if (style.awning)
        parts.push(
            box(0.96, 0.05, 0.42, 0, 1.0, f + 0.18, style.awning, { rx: 0.35 }),
            box(0.96, 0.12, 0.03, 0, 0.88, f + 0.38, style.awning),
        );
    if (style.sign)
        parts.push({
            sign: [0.92, 0.24],
            text: style.sign,
            at: [0, style.awning ? 1.12 : 0.92, f + 0.03],
            c: '#1f2a30',
            fg: '#f6e7b8',
        });
    return parts;
}

export const HOUSE = (style?: HouseStyle): Prefab => ({
    parts: (v) => canalHouse(v, style),
});

/** Front-row houses are cut away to their ground floor so the camera sees over them. */
export const HOUSE_STUB: Prefab = {
    parts: (v) => {
        const brick = pick(rng(v.seed), BRICKS);
        return [
            box(1, 0.32, 1, 0, 0, 0, brick),
            box(1, 0.04, 1, 0, 0.32, 0, mix(brick, '#000000', 0.35)),
        ];
    },
};

const poster = (v: PrefabVariant): Part[] => {
    const r = rng(v.seed);
    const bg = pick(r, [
        '#e94f37',
        '#393e41',
        '#f6f7eb',
        '#3f88c5',
        '#f2bb05',
        '#7b2d8b',
    ]);
    const fg = pick(r, ['#f6f7eb', '#1b1b1e', '#ffd166', '#06d6a0', '#ef476f']);
    return [
        box(0.56, 0.78, 0.02, 0, 1.1, 0, bg),
        ball(0.15, 0, 1.6, 0.02, fg, { squash: 0.15 }),
        box(0.4, 0.06, 0.02, 0, 1.3, 0.015, fg),
        box(0.3, 0.04, 0.02, 0, 1.22, 0.015, fg),
    ];
};

const painting = (v: PrefabVariant): Part[] => {
    const r = rng(v.seed);
    const sky = pick(r, ['#8ecae6', '#ffb4a2', '#f4d35e', '#cdb4db']);
    const land = pick(r, ['#588157', '#bc6c25', '#264653', '#6d597a']);
    return [
        box(0.86, 0.62, 0.04, 0, 1.1, 0, '#b08d57'),
        box(0.74, 0.5, 0.02, 0, 1.16, 0.02, sky),
        box(0.74, 0.2, 0.02, 0, 1.16, 0.03, land),
        ball(0.07, 0.2, 1.52, 0.03, '#fff3b0', { squash: 0.2 }),
    ];
};

const books = (v: PrefabVariant): Part[] => {
    const r = rng(v.seed);
    const parts: Part[] = [
        box(0.8, 1.6, 0.04, 0, 0, -0.18, C.darkWood),
        box(0.04, 1.6, 0.4, -0.38, 0, 0, C.darkWood),
        box(0.04, 1.6, 0.4, 0.38, 0, 0, C.darkWood),
    ];
    for (const y of [0, 0.4, 0.8, 1.2, 1.56])
        parts.push(box(0.8, 0.04, 0.4, 0, y, 0, C.darkWood));
    for (const y of [0.04, 0.44, 0.84, 1.24]) {
        let x = -0.33;
        while (x < 0.3) {
            const w = 0.04 + r() * 0.05;
            const h = 0.22 + r() * 0.1;
            parts.push(
                box(
                    w,
                    h,
                    0.28,
                    x + w / 2,
                    y,
                    0.02,
                    pick(r, [
                        '#c0392b',
                        '#2c3e50',
                        '#27ae60',
                        '#f39c12',
                        '#8e44ad',
                        '#ecf0f1',
                        '#16a085',
                        '#d35400',
                    ]),
                    { flat: true },
                ),
            );
            x += w + 0.01;
        }
    }
    return parts;
};

export const PREFABS: Record<string, Prefab> = {
    // Seating
    sofa: {
        size: [2, 1],
        tint: { fabric: '#b8574a' },
        seats: [
            { at: [0, 0], h: 0.43, face: 0 },
            { at: [1, 0], h: 0.43, face: 0 },
        ],
        parts: (v) => {
            const f = v.tint.fabric ?? '#b8574a';
            const light = mix(f, '#ffffff', 0.18);
            return [
                ...legs(1.7, 0.6, 0.08, C.darkWood),
                box(1.9, 0.25, 0.8, 0, 0.08, 0.02, f),
                box(0.88, 0.12, 0.6, -0.47, 0.32, 0.1, light),
                box(0.88, 0.12, 0.6, 0.47, 0.32, 0.1, light),
                box(1.9, 0.48, 0.2, 0, 0.3, -0.3, f),
                box(0.16, 0.3, 0.8, -0.95, 0.3, 0.02, f),
                box(0.16, 0.3, 0.8, 0.95, 0.3, 0.02, f),
            ];
        },
    },
    armchair: {
        tint: { fabric: '#4a6b8a' },
        seats: [{ at: [0, 0], h: 0.43, face: 0 }],
        parts: (v) => {
            const f = v.tint.fabric ?? '#4a6b8a';
            return [
                ...legs(0.6, 0.6, 0.08, C.darkWood),
                box(0.8, 0.25, 0.8, 0, 0.08, 0.02, f),
                box(0.6, 0.12, 0.6, 0, 0.32, 0.1, mix(f, '#ffffff', 0.15)),
                box(0.8, 0.5, 0.2, 0, 0.3, -0.3, f),
                box(0.14, 0.28, 0.8, -0.38, 0.3, 0.02, f),
                box(0.14, 0.28, 0.8, 0.38, 0.3, 0.02, f),
            ];
        },
    },
    chair: {
        tint: { wood: C.wood },
        seats: [{ at: [0, 0], h: 0.46, face: 0 }],
        parts: (v) => [
            ...legs(0.36, 0.36, 0.44, v.tint.wood ?? C.wood, 0.025),
            box(0.44, 0.05, 0.44, 0, 0.42, 0, v.tint.wood ?? C.wood),
            box(0.44, 0.48, 0.05, 0, 0.47, -0.2, v.tint.wood ?? C.wood),
        ],
    },
    stool: {
        tint: { fabric: '#b33a3a' },
        seats: [{ at: [0, 0], h: 0.7, face: 0 }],
        parts: (v) => [
            cyl(0.18, 0.02, 0, 0, 0, C.chrome, { flat: true }),
            cyl(0.03, 0.64, 0, 0, 0, C.chrome),
            cyl(0.19, 0.07, 0, 0.64, 0, v.tint.fabric ?? '#b33a3a'),
        ],
    },
    bench: {
        size: [2, 1],
        seats: [
            { at: [0, 0], h: 0.45, face: 0 },
            { at: [1, 0], h: 0.45, face: 0 },
        ],
        parts: [
            box(0.08, 0.42, 0.42, -0.85, 0, 0, C.iron),
            box(0.08, 0.42, 0.42, 0.85, 0, 0, C.iron),
            box(1.9, 0.05, 0.14, 0, 0.42, 0.13, '#8a5a33'),
            box(1.9, 0.05, 0.14, 0, 0.42, -0.03, '#8a5a33'),
            box(1.9, 0.12, 0.04, 0, 0.62, -0.2, '#8a5a33'),
            box(1.9, 0.12, 0.04, 0, 0.8, -0.2, '#8a5a33'),
            box(0.06, 0.5, 0.06, -0.85, 0.42, -0.2, C.iron),
            box(0.06, 0.5, 0.06, 0.85, 0.42, -0.2, C.iron),
        ],
    },
    'window-bench': {
        size: [2, 1],
        tint: { fabric: '#7c9c6b' },
        seats: [
            { at: [0, 0], h: 0.45, face: 0 },
            { at: [1, 0], h: 0.45, face: 0 },
        ],
        parts: (v) => [
            box(1.9, 0.36, 0.6, 0, 0, -0.15, C.lightWood),
            box(1.86, 0.1, 0.56, 0, 0.36, -0.13, v.tint.fabric ?? '#7c9c6b'),
            box(
                0.4,
                0.3,
                0.12,
                -0.5,
                0.46,
                -0.36,
                mix(v.tint.fabric ?? '#7c9c6b', '#fff', 0.3),
                { rx: -0.2 },
            ),
            box(0.4, 0.3, 0.12, 0.55, 0.46, -0.36, '#e9c46a', { rx: -0.2 }),
        ],
    },
    booth: {
        size: [2, 2],
        tint: { fabric: '#8b2f3c' },
        seats: [
            { at: [0, 0], h: 0.45, face: 0 },
            { at: [1, 0], h: 0.45, face: 0 },
            { at: [0, 1], h: 0.45, face: 1 },
        ],
        parts: (v) => {
            const f = v.tint.fabric ?? '#8b2f3c';
            return [
                box(1.95, 0.42, 0.6, 0, 0, -0.68, f),
                box(1.95, 0.65, 0.2, 0, 0.42, -0.88, f),
                box(0.6, 0.42, 1.35, -0.68, 0, 0.3, f),
                box(0.2, 0.65, 1.35, -0.88, 0.42, 0.3, f),
                cyl(0.05, 0.72, 0.35, 0, 0.35, C.dark),
                box(1.0, 0.06, 0.9, 0.35, 0.72, 0.35, C.lightWood),
                cyl(0.06, 0.12, 0.2, 0.78, 0.3, '#f6f1e7'),
            ];
        },
    },
    // Tables
    table: {
        parts: [
            cyl(0.2, 0.03, 0, 0, 0, C.dark, { flat: true }),
            cyl(0.04, 0.72, 0, 0, 0, C.dark),
            cyl(0.38, 0.04, 0, 0.72, 0, '#ebe6dc'),
        ],
    },
    'long-table': {
        size: [2, 1],
        parts: [
            ...legs(1.7, 0.6, 0.72, C.darkWood, 0.04),
            box(1.9, 0.06, 0.82, 0, 0.72, 0, C.wood),
        ],
    },
    'coffee-table': {
        parts: [
            ...legs(0.6, 0.4, 0.3, C.darkWood),
            box(0.75, 0.05, 0.55, 0, 0.3, 0, C.lightWood),
            box(0.2, 0.03, 0.15, 0.1, 0.35, 0, '#e76f51', { flat: true }),
        ],
    },
    desk: {
        size: [2, 1],
        parts: [
            ...legs(1.75, 0.6, 0.74, C.dark, 0.025),
            box(1.9, 0.05, 0.72, 0, 0.74, -0.05, C.lightWood),
            box(0.45, 0.35, 0.6, 0.65, 0.39, -0.05, C.lightWood),
            box(0.1, 0.02, 0.02, 0.65, 0.6, 0.26, C.dark, { flat: true }),
        ],
    },
    counter: {
        size: [3, 1],
        parts: [
            box(2.95, 0.98, 0.66, 0, 0, -0.05, C.darkWood),
            box(2.95, 0.6, 0.02, 0, 0.18, 0.29, '#7a4e2d'),
            box(3.0, 0.06, 0.8, 0, 0.98, -0.02, '#d8d2c4'),
            box(2.95, 0.05, 0.05, 0, 0.1, 0.3, C.chrome, { flat: true }),
            ...[-1, 0, 1].map((x) =>
                box(0.6, 0.02, 0.4, x, 1.04, -0.15, '#bfa27a', { flat: true }),
            ),
        ],
    },
    reception: {
        size: [3, 1],
        parts: [
            box(2.9, 1.0, 0.6, 0, 0, 0, '#6d3b47'),
            box(2.9, 0.12, 0.02, 0, 0.7, 0.31, '#d4af37'),
            box(3.0, 0.06, 0.72, 0, 1.0, 0, '#2d2d2d'),
            cyl(0.06, 0.05, -0.9, 1.06, 0.1, '#d4af37'),
            ball(0.04, -0.9, 1.13, 0.1, '#d4af37'),
            box(0.3, 0.02, 0.22, 0.6, 1.06, 0, '#f3efe6', { flat: true }),
        ],
    },
    kitchen: {
        size: [2, 1],
        parts: [
            box(1.95, 0.88, 0.6, 0, 0, -0.18, C.white),
            box(2.0, 0.05, 0.66, 0, 0.88, -0.16, '#3a3a3a'),
            box(0.5, 0.04, 0.36, -0.45, 0.9, -0.18, C.chrome, { flat: true }),
            cyl(0.02, 0.22, -0.45, 0.93, -0.36, C.chrome, { flat: true }),
            box(0.6, 0.02, 0.45, 0.5, 0.93, -0.18, C.dark, { flat: true }),
            {
                ring: [0.1, 0.015],
                at: [0.38, 0.95, -0.18],
                c: '#666',
                rx: Math.PI / 2,
                flat: true,
            },
            {
                ring: [0.1, 0.015],
                at: [0.62, 0.95, -0.18],
                c: '#666',
                rx: Math.PI / 2,
                flat: true,
            },
            box(1.95, 0.55, 0.34, 0, 1.5, -0.33, C.white),
            ...[-0.48, 0.48].map((x) =>
                box(0.9, 0.5, 0.02, x, 1.52, -0.15, '#ece7dc'),
            ),
            ...[-0.5, 0, 0.5].map((x) =>
                box(0.1, 0.02, 0.02, x, 0.78, 0.13, C.dark, { flat: true }),
            ),
        ],
    },
    fridge: {
        parts: [
            box(0.7, 1.75, 0.65, 0, 0, -0.1, '#e8eef0'),
            box(0.02, 0.4, 0.04, 0.27, 1.0, 0.24, C.chrome),
            box(0.7, 0.02, 0.66, 0, 1.15, -0.1, '#c8cdd0', { flat: true }),
        ],
    },
    bed: {
        size: [1, 2],
        tint: { blanket: '#5470a8' },
        seats: [
            { at: [0, 0], h: 0.58, face: 0 },
            { at: [0, 1], h: 0.5, face: 1 },
        ],
        parts: (v) => [
            box(0.96, 0.3, 1.96, 0, 0, 0, C.wood),
            box(0.9, 0.18, 1.86, 0, 0.3, 0.02, C.white),
            box(0.6, 0.12, 0.3, 0, 0.48, -0.72, '#fbfaf5'),
            box(0.94, 0.08, 1.2, 0, 0.47, 0.35, v.tint.blanket ?? '#5470a8'),
            box(0.96, 0.8, 0.08, 0, 0, -0.98, C.darkWood),
        ],
    },
    // Furni (backend state drives them)
    plant: {
        parts: (v) => [
            cyl(0.17, 0.32, 0, 0, 0, C.pot),
            cyl(0.15, 0.02, 0, 0.31, 0, '#4a3020', { flat: true }),
            ...leaves(v, 0.35, 0.22),
        ],
    },
    'big-plant': {
        parts: (v) => [
            cyl(0.25, 0.45, 0, 0, 0, '#e9e3d6'),
            ...leaves(v, 0.5, 0.32),
            ...leaves({ ...v, seed: v.seed + 1 }, 0.95, 0.25),
        ],
    },
    planter: {
        parts: (v) => {
            const c = v.state?.condition ?? 1;
            const petal = mix('#6b4a3a', '#d62828', Math.min(1, c * 1.5));
            return [
                box(0.85, 0.32, 0.38, 0, 0, 0, '#7a5233'),
                ...leaves(v, 0.32, 0.14).map((p) => ({
                    ...p,
                    at: [
                        (p.at?.[0] ?? 0) * 2.5,
                        p.at?.[1] ?? 0,
                        p.at?.[2] ?? 0,
                    ] as Vec3,
                })),
                ...[-0.3, -0.1, 0.12, 0.3].map((x, i) =>
                    ball(0.07, x, 0.5 + (i % 2) * 0.06, 0.05, petal),
                ),
            ];
        },
    },
    cups: {
        solid: false,
        parts: (v) => {
            const n = Math.round((1 - (v.state?.condition ?? 1)) * 5) + 1;
            return Array.from({ length: n }, (_, i) =>
                cyl(
                    0.06,
                    0.1,
                    -0.2 + (i % 3) * 0.16,
                    Math.floor(i / 3) * 0.1,
                    (i % 2) * 0.12 - 0.12,
                    i > 1 ? '#e8e0cc' : '#ffffff',
                    { flat: true },
                ),
            );
        },
    },
    coffee: {
        solid: false,
        parts: (v) => [
            box(0.36, 0.42, 0.3, 0, 0.0, -0.15, '#2b2b2b'),
            box(0.3, 0.06, 0.22, 0, 0.0, 0.0, '#555', { flat: true }),
            cyl(0.06, 0.1, 0, 0.06, -0.02, '#f3efe6', { flat: true }),
            ball(
                0.03,
                0.12,
                0.32,
                0.01,
                v.state && v.state.condition < 0.5 ? '#e63946' : '#52b788',
                { glow: true },
            ),
            ...(v.state && v.state.condition < 0.5
                ? [ball(0.08, 0, 0.56, -0.15, '#bbbbbb', { tag: 'smoke' })]
                : []),
        ],
    },
    espresso: {
        solid: false,
        parts: (v) => [
            box(0.62, 0.42, 0.4, 0, 0.0, -0.15, C.chrome),
            box(0.64, 0.05, 0.42, 0, 0.42, -0.15, '#8f979f'),
            box(0.5, 0.12, 0.05, 0, 0.16, 0.07, '#30343a'),
            cyl(0.03, 0.08, -0.15, 0.12, 0.08, C.dark, { flat: true }),
            cyl(0.03, 0.08, 0.15, 0.12, 0.08, C.dark, { flat: true }),
            ball(
                0.03,
                0.24,
                0.34,
                0.06,
                v.state && v.state.condition < 0.5 ? '#e63946' : '#ffb703',
                { glow: true },
            ),
            ...[-0.2, 0, 0.2].map((x) =>
                cyl(0.04, 0.06, x, 0.47, -0.15, '#ffffff', { flat: true }),
            ),
            ...(v.state && v.state.condition < 0.5
                ? [ball(0.1, 0, 0.71, -0.15, '#bbbbbb', { tag: 'smoke' })]
                : []),
        ],
    },
    'coffee-cart': {
        parts: (v) => [
            box(0.86, 0.75, 0.55, 0, 0.12, 0, '#2f5d50'),
            box(0.9, 0.05, 0.6, 0, 0.87, 0, '#d8d2c4'),
            {
                ring: [0.12, 0.03],
                at: [-0.32, 0.12, 0.29],
                c: C.dark,
                flat: true,
            },
            {
                ring: [0.12, 0.03],
                at: [0.32, 0.12, 0.29],
                c: C.dark,
                flat: true,
            },
            box(0.34, 0.34, 0.26, -0.15, 0.92, -0.08, C.chrome),
            ball(
                0.03,
                0.0,
                1.15,
                0.06,
                v.state && v.state.condition < 0.5 ? '#e63946' : '#ffb703',
                { glow: true },
            ),
            cyl(0.02, 1.0, 0.36, 0.9, -0.2, C.dark),
            {
                cone: [0.6, 0.3],
                at: [0.36, 1.85, -0.2],
                c: '#e9c46a',
                sides: 8,
            },
            {
                sign: [0.6, 0.2],
                text: 'KOFFIE',
                at: [0, 0.45, 0.28],
                c: '#2f5d50',
                fg: '#f6e7b8',
            },
            ...(v.state && v.state.condition < 0.5
                ? [ball(0.08, -0.15, 1.45, -0.08, '#bbbbbb', { tag: 'smoke' })]
                : []),
        ],
    },
    jukebox: {
        parts: (v) => {
            const on = v.state?.on ?? false;
            const glow = on ? '#ffd166' : '#8a6b3c';
            return [
                box(0.72, 1.1, 0.48, 0, 0, -0.1, '#7a2e2a'),
                {
                    cyl: [0.36, 0.48],
                    at: [0, 1.1, -0.34],
                    c: '#7a2e2a',
                    rx: Math.PI / 2,
                    sides: 20,
                },
                box(0.52, 0.32, 0.02, 0, 0.62, 0.15, glow, {
                    glow: on ? true : undefined,
                    tag: on ? 'disco' : undefined,
                }),
                box(0.56, 0.06, 0.03, 0, 0.98, 0.15, C.chrome),
                box(0.56, 0.3, 0.02, 0, 0.16, 0.15, '#3a1a18'),
                ...[-0.18, 0, 0.18].map((x) =>
                    box(
                        0.04,
                        0.26,
                        0.03,
                        x,
                        0.18,
                        0.16,
                        on ? '#ff6b6b' : '#8a3a35',
                        { glow: on ? true : undefined },
                    ),
                ),
                ...(on
                    ? [ball(0.06, 0.15, 1.6, 0, '#222', { tag: 'notes' })]
                    : []),
            ];
        },
    },
    lamp: {
        parts: (v) => [
            cyl(0.15, 0.03, 0, 0, 0, C.dark, { flat: true }),
            cyl(0.025, 1.4, 0, 0, 0, C.dark),
            {
                cyl: [0.12, 0.24, 0.3],
                at: [0, 1.32, 0],
                c: '#f4e3b5',
                glow: true,
                tag: lampGlow(v),
            },
        ],
        light: { at: [0, 1.4, 0], colour: '#ffcf87', power: 2.2 },
    },
    'desk-lamp': {
        solid: false,
        parts: (v) => [
            cyl(0.08, 0.02, 0, 0.0, -0.15, '#2a5d34', { flat: true }),
            cyl(0.015, 0.3, 0, 0.0, -0.15, '#d4af37'),
            {
                box: [0.32, 0.08, 0.14],
                at: [0, 0.3, -0.1],
                c: '#2a7d45',
                glow: true,
                tag: lampGlow(v),
            },
        ],
        light: { at: [0, 0.36, 0], colour: '#ffe0a0', power: 1.4 },
    },
    'street-lamp': {
        parts: (v) => [
            cyl(0.09, 0.12, 0, 0, 0, '#1f3326'),
            cyl(0.045, 2.2, 0, 0, 0, '#1f3326'),
            box(0.2, 0.06, 0.2, 0, 2.18, 0, '#1f3326'),
            {
                box: [0.18, 0.28, 0.18],
                at: [0, 2.24, 0],
                c: '#ffe8a3',
                glow: 'night',
                tag: v.state ? lampGlow(v) : 'light',
                opacity: 0.95,
            },
            {
                cone: [0.17, 0.14],
                at: [0, 2.52, 0],
                c: '#1f3326',
                sides: 4,
                ry: Math.PI / 4,
            },
            ball(0.03, 0, 2.68, 0, '#1f3326'),
        ],
        light: { at: [0, 2.3, 0], colour: '#ffcf87', power: 3.5 },
    },
    laptop: {
        solid: false,
        parts: [
            box(0.38, 0.02, 0.26, 0, 0.79, 0.02, '#c7ccd1', { flat: true }),
            box(0.38, 0.26, 0.015, 0, 0.8, -0.11, '#c7ccd1', { rx: -0.25 }),
            box(0.34, 0.21, 0.01, 0, 0.83, -0.1, '#7fb3ff', {
                rx: -0.25,
                glow: true,
                flat: true,
            }),
        ],
    },
    // Decor (Homes, in the owner's words)
    poster: { wall: true, solid: false, parts: poster },
    painting: { wall: true, solid: false, parts: painting },
    photo: {
        wall: true,
        solid: false,
        parts: (v) => [
            box(0.3, 0.38, 0.03, 0, 1.25, 0, C.darkWood),
            box(
                0.24,
                0.32,
                0.02,
                0,
                1.28,
                0.015,
                pick(rng(v.seed), ['#d4a373', '#a8dadc', '#e5989b', '#b5838d']),
            ),
        ],
    },
    noticeboard: {
        wall: true,
        solid: false,
        parts: (v) => {
            const r = rng(v.seed);
            return [
                box(1.1, 0.75, 0.04, 0, 1.0, 0, '#c49a6c'),
                ...Array.from({ length: 6 }, (_, i) =>
                    box(
                        0.2,
                        0.24,
                        0.02,
                        -0.38 + (i % 3) * 0.38 + r() * 0.06,
                        1.08 + Math.floor(i / 3) * 0.3,
                        0.025,
                        pick(r, [
                            '#fff',
                            '#ffe066',
                            '#a5d8ff',
                            '#ffc9c9',
                            '#d3f9d8',
                        ]),
                        { rz: (r() - 0.5) * 0.3, flat: true },
                    ),
                ),
            ];
        },
    },
    chalkboard: {
        wall: true,
        solid: false,
        parts: [
            box(0.9, 0.6, 0.04, 0, 1.15, 0, C.darkWood),
            {
                sign: [0.8, 0.5],
                text: 'KOFFIE · THEE · TOSTI',
                at: [0, 1.2, 0.025],
                c: '#24302a',
                fg: '#f1f1f1',
            },
        ],
    },
    rug: {
        size: [2, 2],
        solid: false,
        tint: { rug: '#a23b3b' },
        parts: (v) => [
            box(1.8, 0.015, 1.5, 0, 0.001, 0, v.tint.rug ?? '#a23b3b', {
                flat: true,
            }),
            box(
                1.5,
                0.017,
                1.2,
                0,
                0.001,
                0,
                mix(v.tint.rug ?? '#a23b3b', '#f1e3c8', 0.45),
                { flat: true },
            ),
            box(1.2, 0.019, 0.9, 0, 0.001, 0, v.tint.rug ?? '#a23b3b', {
                flat: true,
            }),
        ],
    },
    books: { parts: books },
    guitar: {
        parts: [
            box(0.3, 0.02, 0.2, 0, 0, 0, C.dark, { flat: true }),
            ball(0.17, 0, 0.3, 0, '#c8742b', { squash: 0.35 }),
            ball(0.13, 0, 0.52, 0, '#c8742b', { squash: 0.35 }),
            box(0.06, 0.55, 0.04, 0, 0.6, 0, C.darkWood),
            box(0.09, 0.12, 0.04, 0, 1.15, 0, C.dark),
            ball(0.05, 0, 0.32, 0.06, '#2b1a0e', { squash: 0.3 }),
        ],
    },
    thing: {
        parts: (v) => {
            const r = rng(v.seed);
            return [
                box(0.3, 0.5, 0.3, 0, 0, 0, C.darkWood),
                ball(
                    0.13,
                    0,
                    0.62,
                    0,
                    pick(r, [
                        '#e63946',
                        '#457b9d',
                        '#2a9d8f',
                        '#e9c46a',
                        '#f4a261',
                    ]),
                    { detail: 0 },
                ),
            ];
        },
    },
    tv: {
        parts: [
            box(1.0, 0.4, 0.4, 0, 0, -0.2, C.darkWood),
            box(0.9, 0.52, 0.05, 0, 0.45, -0.28, '#111'),
            box(0.84, 0.46, 0.01, 0, 0.48, -0.25, '#1b2a3a', { flat: true }),
        ],
    },
    // Outdoors
    elm: {
        parts: (v) => {
            const r = rng(v.seed);
            const g = pick(r, ['#4e8a3e', '#5c9a45', '#477c3a', '#6aa84f']);
            const s = 0.85 + r() * 0.35;
            return [
                cyl(0.09, 1.3 * s, 0, 0, 0, '#5b4636'),
                ball(0.6 * s, 0, 1.65 * s, 0, g, { detail: 1 }),
                ball(0.45 * s, 0.35, 1.95 * s, 0.15, mix(g, '#ffffff', 0.1), {
                    detail: 1,
                }),
                ball(0.42 * s, -0.3, 1.9 * s, -0.2, mix(g, '#000000', 0.12), {
                    detail: 1,
                }),
            ];
        },
    },
    bollard: {
        solid: false,
        parts: [
            cyl(0.05, 0.55, 0, 0, 0.3, C.bollard),
            ball(0.065, 0, 0.6, 0.3, C.bollard),
        ],
    },
    bike: { solid: false, parts: (v) => bikeParts(rng(v.seed), 0, 0.2) },
    'bike-rack': {
        size: [2, 1],
        parts: (v) => {
            const r = rng(v.seed);
            return [
                box(1.9, 0.04, 0.04, 0, 0.45, -0.2, C.metal),
                box(0.04, 0.45, 0.04, -0.92, 0, -0.2, C.metal),
                box(0.04, 0.45, 0.04, 0.92, 0, -0.2, C.metal),
                ...[-0.65, -0.2, 0.25, 0.7].flatMap((x) =>
                    r() < 0.8 ? bikeParts(r, x, 0.05, Math.PI / 2) : [],
                ),
            ];
        },
    },
    'herring-cart': {
        size: [2, 1],
        parts: [
            box(1.8, 0.85, 0.75, 0, 0.15, 0, '#f3efe6'),
            ...[-0.6, -0.2, 0.2, 0.6].map((x) =>
                box(0.2, 0.85, 0.02, x, 0.15, 0.38, '#2a5c99', { flat: true }),
            ),
            box(1.86, 0.06, 0.82, 0, 1.0, 0, '#cfd6dc'),
            {
                ring: [0.15, 0.04],
                at: [-0.6, 0.15, 0.4],
                c: C.dark,
                flat: true,
            },
            { ring: [0.15, 0.04], at: [0.6, 0.15, 0.4], c: C.dark, flat: true },
            ...[-0.85, 0.85].map((x) => cyl(0.025, 0.8, x, 1.0, 0.35, C.metal)),
            { gable: [1.95, 0.35, 0.95], at: [0, 1.8, 0.05], c: '#c0392b' },
            {
                sign: [1.0, 0.24],
                text: 'HOLLANDSE NIEUWE',
                at: [0, 1.82, 0.5],
                c: '#2a5c99',
                fg: '#ffffff',
            },
            ...[-0.5, -0.2, 0.1, 0.4].map((x) =>
                box(0.2, 0.05, 0.1, x, 1.06, 0.15, '#9fb4c7', { flat: true }),
            ),
        ],
    },
    'terrace-table': {
        parts: (v) => {
            const r = rng(v.seed);
            const c = pick(r, ['#c0392b', '#2a9d8f', '#e9c46a', '#264653']);
            return [
                cyl(0.03, 0.7, 0, 0, 0, C.dark),
                cyl(0.3, 0.03, 0, 0.7, 0, '#ebe6dc'),
                cyl(0.02, 1.0, 0, 0.73, 0, C.white),
                { cone: [0.8, 0.3], at: [0, 1.7, 0], c, sides: 8 },
            ];
        },
    },
    steps: {
        seats: [{ at: [0, 0], h: -0.12, face: 0 }],
        parts: [0, 1, 2, 3].map((i) =>
            box(
                0.9,
                0.12,
                0.25,
                0,
                -0.12 * (i + 1),
                -0.38 + i * 0.25,
                '#8d8478',
            ),
        ),
    },
    houseboat: {
        size: [4, 1],
        parts: (v) => {
            const r = rng(v.seed);
            const hull = pick(r, ['#2f5d3a', '#1d3557', '#6b2737', '#3d405b']);
            return [
                box(3.8, 0.55, 0.88, 0, -0.62, 0, hull),
                box(3.84, 0.06, 0.9, 0, -0.1, 0, '#3a2e25'),
                box(2.7, 0.55, 0.74, -0.3, -0.06, 0, '#e8dcc0'),
                ...[-1.2, -0.6, 0, 0.6].map((x) =>
                    box(0.3, 0.22, 0.02, x, 0.12, 0.38, '#33465a', {
                        glow: 'night',
                        tag: 'window',
                    }),
                ),
                box(2.8, 0.06, 0.82, -0.3, 0.49, 0, hull),
                cyl(0.05, 0.3, 0.8, 0.55, -0.2, '#555'),
                ...[-1.3, -0.9, -0.4, 0.1].map((x, i) =>
                    ball(0.13, x, 0.65, 0.1, i % 2 ? '#d62828' : C.leaf),
                ),
                box(0.4, 0.3, 0.3, 1.5, -0.06, 0, '#7a5233'),
            ];
        },
    },
    duck: {
        solid: false,
        parts: (v) => {
            const male = rng(v.seed)() < 0.5;
            return [
                ball(0.12, 0, -0.35, 0, male ? '#8d7b68' : '#a88b6a', {
                    squash: 0.6,
                    tag: 'paddle',
                }),
                ball(0.07, 0, -0.22, 0.1, male ? '#2d6a4f' : '#a88b6a', {
                    tag: 'paddle',
                }),
                {
                    cone: [0.03, 0.08],
                    at: [0, -0.23, 0.18],
                    c: '#f4a261',
                    rx: Math.PI / 2,
                    tag: 'paddle',
                },
            ];
        },
    }, // Bars, shops, parties, cosy corners
    'bar-shelf': {
        size: [2, 1],
        parts: (v) => {
            const r = rng(v.seed);
            const bottles: Part[] = [];
            for (const y of [1.0, 1.45])
                for (let i = 0; i < 9; i++)
                    bottles.push(
                        cyl(
                            0.04,
                            0.22 + r() * 0.1,
                            -0.8 + i * 0.2,
                            y,
                            -0.3,
                            pick(r, [
                                '#2d6a4f',
                                '#9b2226',
                                '#e9c46a',
                                '#8ecae6',
                                '#6d597a',
                                '#f4a261',
                            ]),
                            { opacity: 0.85, flat: true },
                        ),
                    );
            return [
                box(1.95, 0.95, 0.5, 0, 0, -0.22, C.darkWood),
                box(1.95, 1.6, 0.06, 0, 0.95, -0.45, '#3b2a20'),
                box(1.95, 0.04, 0.24, 0, 0.98, -0.3, C.lightWood),
                box(1.95, 0.04, 0.24, 0, 1.43, -0.3, C.lightWood),
                box(1.6, 0.04, 0.02, 0, 2.0, -0.4, '#ffcf87', { glow: true }),
                ...bottles,
            ];
        },
    },
    shelf: {
        size: [2, 1],
        parts: (v) => {
            const r = rng(v.seed);
            const goods: Part[] = [];
            for (const y of [0.05, 0.5, 0.95, 1.4])
                for (let i = 0; i < 6; i++) {
                    const c = pick(r, [
                        '#e63946',
                        '#f1faee',
                        '#a8dadc',
                        '#457b9d',
                        '#ffb703',
                        '#8ac926',
                        '#ff99c8',
                        '#6a4c93',
                    ]);
                    goods.push(
                        r() < 0.5
                            ? box(
                                  0.18,
                                  0.22,
                                  0.2,
                                  -0.75 + i * 0.3,
                                  y + 0.04,
                                  -0.05,
                                  c,
                                  { flat: true },
                              )
                            : cyl(
                                  0.08,
                                  0.26,
                                  -0.75 + i * 0.3,
                                  y + 0.04,
                                  -0.05,
                                  c,
                                  { flat: true },
                              ),
                    );
                }
            return [
                box(1.95, 1.85, 0.05, 0, 0, -0.3, '#d9d4c7'),
                ...[0.05, 0.5, 0.95, 1.4].map((y) =>
                    box(1.95, 0.04, 0.5, 0, y, -0.06, '#c7c0b0'),
                ),
                box(0.05, 1.85, 0.5, -0.97, 0, -0.06, '#c7c0b0'),
                box(0.05, 1.85, 0.5, 0.97, 0, -0.06, '#c7c0b0'),
                ...goods,
            ];
        },
    },
    register: {
        parts: [
            box(0.9, 0.95, 0.6, 0, 0, 0, '#3d5a80'),
            box(0.95, 0.05, 0.65, 0, 0.95, 0, '#e0e1dd'),
            box(0.32, 0.18, 0.26, 0.15, 1.0, -0.05, C.dark),
            box(0.26, 0.14, 0.02, 0.15, 1.2, -0.12, '#8ecae6', {
                glow: true,
                rx: -0.3,
            }),
            box(0.2, 0.02, 0.15, -0.25, 1.0, 0.05, '#f1faee', { flat: true }),
        ],
    },
    'clothes-rack': {
        size: [2, 1],
        parts: (v) => {
            const r = rng(v.seed);
            return [
                box(0.05, 1.5, 0.05, -0.85, 0, 0, C.chrome),
                box(0.05, 1.5, 0.05, 0.85, 0, 0, C.chrome),
                box(1.75, 0.04, 0.04, 0, 1.48, 0, C.chrome),
                box(0.4, 0.05, 0.4, -0.85, 0, 0, C.chrome, { flat: true }),
                box(0.4, 0.05, 0.4, 0.85, 0, 0, C.chrome, { flat: true }),
                ...Array.from({ length: 7 }, (_, i) =>
                    box(
                        0.06,
                        0.65 + r() * 0.2,
                        0.42,
                        -0.7 + i * 0.23,
                        0.68,
                        0,
                        pick(r, [
                            '#e63946',
                            '#1d3557',
                            '#f4a261',
                            '#2a9d8f',
                            '#e9c46a',
                            '#ffffff',
                            '#6d597a',
                            '#111111',
                        ]),
                    ),
                ),
            ];
        },
    },
    mirror: {
        wall: true,
        solid: false,
        parts: [
            box(0.5, 1.2, 0.04, 0, 0.6, 0, '#b08d57'),
            box(0.42, 1.1, 0.02, 0, 0.65, 0.02, '#cfe8f5', { glow: true }),
        ],
    },
    clock: {
        wall: true,
        solid: false,
        parts: [
            {
                cyl: [0.2, 0.05],
                at: [0, 1.8, 0],
                c: '#f3efe6',
                rx: Math.PI / 2,
                sides: 20,
            },
            { ring: [0.2, 0.025], at: [0, 1.8, 0.03], c: C.darkWood },
            box(0.02, 0.13, 0.01, 0, 1.8, 0.06, C.dark, { flat: true }),
            box(0.1, 0.02, 0.01, 0.05, 1.8, 0.06, C.dark, { flat: true }),
        ],
    },
    neon: {
        wall: true,
        solid: false,
        parts: (v) => [
            {
                sign: [0.9, 0.3],
                text: v.text ?? 'OPEN',
                at: [0, 1.5, 0.01],
                c: '#1b1b2f',
                fg: pick(rng(v.seed), [
                    '#ff4fd8',
                    '#4ff0ff',
                    '#ffe14f',
                    '#7dff4f',
                ]),
            },
        ],
    },
    speaker: {
        parts: [
            box(0.45, 1.1, 0.4, 0, 0, 0, '#1d1d1d'),
            {
                cyl: [0.14, 0.03],
                at: [0, 0.75, 0.2],
                c: '#444',
                rx: Math.PI / 2,
            },
            {
                cyl: [0.08, 0.03],
                at: [0, 0.35, 0.2],
                c: '#444',
                rx: Math.PI / 2,
            },
            box(0.06, 0.06, 0.02, 0.15, 1.0, 0.2, '#4ff0ff', { glow: true }),
        ],
    },
    'dj-booth': {
        size: [2, 1],
        parts: [
            box(1.9, 0.95, 0.65, 0, 0, 0, '#22223b'),
            box(1.9, 0.12, 0.02, 0, 0.6, 0.33, '#ff4fd8', {
                glow: true,
                tag: 'disco',
            }),
            box(1.95, 0.05, 0.7, 0, 0.95, 0, '#111'),
            { cyl: [0.2, 0.03], at: [-0.5, 1.0, 0], c: '#333', sides: 16 },
            { cyl: [0.2, 0.03], at: [0.5, 1.0, 0], c: '#333', sides: 16 },
            box(0.4, 0.04, 0.3, 0, 1.0, 0, '#555'),
        ],
    },
    'dance-floor': {
        size: [3, 3],
        solid: false,
        parts: (v) => {
            const r = rng(v.seed);
            const tiles: Part[] = [];
            for (let x = -1; x <= 1; x++)
                for (let z = -1; z <= 1; z++)
                    tiles.push(
                        box(
                            0.94,
                            0.02,
                            0.94,
                            x,
                            0.001,
                            z,
                            pick(r, [
                                '#ff4fd8',
                                '#4ff0ff',
                                '#ffe14f',
                                '#7dff4f',
                                '#9d4edd',
                            ]),
                            {
                                glow: true,
                                flat: true,
                                tag: (x + z) % 2 ? 'disco' : undefined,
                            },
                        ),
                    );
            return tiles;
        },
    },
    'pool-table': {
        size: [2, 1],
        parts: [
            ...legs(1.6, 0.6, 0.65, C.darkWood, 0.06),
            box(1.95, 0.15, 0.95, 0, 0.65, 0, C.darkWood),
            box(1.8, 0.03, 0.8, 0, 0.8, 0, '#1b7340', { flat: true }),
            ...[
                [-0.5, 0],
                [0.3, 0.2],
                [0.35, -0.15],
                [0.45, 0.05],
                [-0.2, -0.2],
            ].map(([x, z], i) =>
                ball(
                    0.04,
                    x,
                    0.87,
                    z,
                    ['#ffffff', '#e63946', '#ffb703', '#1d3557', '#111111'][i],
                    { detail: 1 },
                ),
            ),
        ],
    },
    piano: {
        size: [2, 1],
        seats: [],
        parts: [
            box(1.7, 1.2, 0.5, 0, 0, -0.2, '#111'),
            box(1.6, 0.06, 0.25, 0, 0.72, 0.15, '#f3efe6'),
            ...Array.from({ length: 11 }, (_, i) =>
                box(0.05, 0.04, 0.14, -0.65 + i * 0.13, 0.76, 0.1, '#111', {
                    flat: true,
                }),
            ),
            box(1.7, 0.3, 0.06, 0, 1.2, -0.42, '#111'),
        ],
    },
    arcade: {
        parts: (v) => [
            box(
                0.7,
                1.7,
                0.6,
                0,
                0,
                -0.05,
                pick(rng(v.seed), ['#3a0ca3', '#d00000', '#006466']),
            ),
            box(0.56, 0.42, 0.02, 0, 1.05, 0.26, '#4ff0ff', {
                glow: true,
                rx: -0.15,
            }),
            box(0.6, 0.06, 0.25, 0, 0.85, 0.3, '#222'),
            ball(0.04, -0.12, 0.92, 0.32, '#ff4fd8', { glow: true }),
            ball(0.03, 0.12, 0.92, 0.32, '#ffe14f', { glow: true }),
            {
                sign: [0.6, 0.16],
                text: 'PLAY',
                at: [0, 1.52, 0.255],
                c: '#111',
                fg: '#ffe14f',
            },
        ],
    },
    fireplace: {
        size: [2, 1],
        parts: [
            box(1.6, 1.1, 0.5, 0, 0, -0.25, '#8d6e63'),
            box(1.8, 0.1, 0.6, 0, 1.1, -0.22, '#5d4037'),
            box(0.9, 0.6, 0.1, 0, 0.1, 0.0, '#1b1b1b'),
            {
                cone: [0.18, 0.4],
                at: [-0.12, 0.12, 0.05],
                c: '#ff8c1a',
                glow: true,
                sides: 5,
                tag: 'flicker',
            },
            {
                cone: [0.13, 0.32],
                at: [0.15, 0.12, 0.05],
                c: '#ffd166',
                glow: true,
                sides: 5,
                tag: 'flicker',
            },
            box(0.6, 0.08, 0.15, 0, 0.1, 0.05, '#5d4037'),
        ],
        light: { at: [0, 0.4, 0.4], colour: '#ff9b4a', power: 2.5 },
    },
    beanbag: {
        tint: { fabric: '#e76f51' },
        seats: [{ at: [0, 0], h: 0.3, face: 0 }],
        parts: (v) => [
            ball(0.4, 0, 0.22, 0, v.tint.fabric ?? '#e76f51', { squash: 0.55 }),
            ball(0.3, 0, 0.42, -0.2, v.tint.fabric ?? '#e76f51', {
                squash: 0.7,
            }),
        ],
    },
    'flower-stall': {
        size: [2, 1],
        parts: (v) => {
            const r = rng(v.seed);
            return [
                box(1.9, 0.7, 0.8, 0, 0, 0, '#6b4f36'),
                ...Array.from({ length: 10 }, (_, i) =>
                    ball(
                        0.11,
                        -0.8 + (i % 5) * 0.4,
                        0.82 + Math.floor(i / 5) * 0.1,
                        (Math.floor(i / 5) - 0.5) * 0.35,
                        pick(r, [
                            '#e63946',
                            '#ffb703',
                            '#ff99c8',
                            '#ffffff',
                            '#9d4edd',
                            '#fb8500',
                        ]),
                    ),
                ),
                ...[-0.9, 0.9].map((x) =>
                    cyl(0.03, 1.4, x, 0.7, -0.35, C.darkWood),
                ),
                { gable: [2.0, 0.3, 1.0], at: [0, 2.1, 0], c: '#2a9d8f' },
            ];
        },
    },
    fountain: {
        size: [2, 2],
        parts: [
            cyl(0.95, 0.35, 0, 0, 0, '#a8a29e', { sides: 16 }),
            cyl(0.82, 0.02, 0, 0.3, 0, '#5fa3b3', { sides: 16, glow: 'night' }),
            cyl(0.15, 0.9, 0, 0, 0, '#a8a29e', { sides: 8 }),
            cyl(0.4, 0.08, 0, 0.9, 0, '#a8a29e', { sides: 12 }),
            ball(0.12, 0, 1.15, 0, '#bde0fe', { opacity: 0.7, tag: 'smoke' }),
        ],
    },
    'vending-machine': {
        parts: (v) => {
            const r = rng(v.seed);
            return [
                box(
                    0.75,
                    1.8,
                    0.65,
                    0,
                    0,
                    -0.1,
                    pick(r, ['#d62828', '#1d3557', '#2a9d8f']),
                ),
                box(0.5, 1.2, 0.02, -0.08, 0.45, 0.235, '#bde0fe', {
                    glow: true,
                }),
                box(0.12, 0.5, 0.02, 0.27, 0.9, 0.235, '#222'),
            ];
        },
    },
    hammock: {
        size: [2, 1],
        seats: [{ at: [0, 0], h: 0.55, face: 0 }],
        parts: [
            cyl(0.05, 1.3, -0.9, 0, 0, C.darkWood),
            cyl(0.05, 1.3, 0.9, 0, 0, C.darkWood),
            box(1.5, 0.05, 0.55, 0, 0.5, 0, '#e9c46a', { rz: 0.0 }),
            ...[-0.5, 0, 0.5].map((x) =>
                box(
                    0.4,
                    0.04,
                    0.55,
                    x,
                    0.47 - Math.abs(x) * 0.0,
                    0,
                    ['#e76f51', '#2a9d8f', '#e9c46a'][Math.round(x * 2 + 1)],
                ),
            ),
        ],
    },
    'potted-palm': {
        parts: (v) => {
            const g = mix(
                C.dryLeaf,
                '#3f8f4a',
                Math.min(1, (v.state?.condition ?? 1) * 1.4),
            );
            return [
                cyl(0.2, 0.4, 0, 0, 0, '#d6ccc2', { sides: 8 }),
                cyl(0.04, 0.9, 0, 0.4, 0, '#7f5539'),
                ...[0, 1, 2, 3, 4].map(
                    (i) =>
                        ({
                            cone: [0.08, 0.6],
                            at: [
                                Math.cos(i * 1.26) * 0.25,
                                1.2,
                                Math.sin(i * 1.26) * 0.25,
                            ],
                            c: g,
                            rz: Math.cos(i * 1.26) * -1.8,
                            rx: Math.sin(i * 1.26) * 1.8,
                            sides: 4,
                        }) as Part,
                ),
            ];
        },
    },
    umbrella: {
        parts: (v) => [
            cyl(0.03, 1.9, 0, 0, 0, C.white),
            {
                cone: [0.95, 0.35],
                at: [0, 1.8, 0],
                c: pick(rng(v.seed), [
                    '#e63946',
                    '#2a9d8f',
                    '#f4a261',
                    '#264653',
                ]),
                sides: 8,
            },
        ],
    },
    // Catalogue models (ADR 0027), generated from tools/catalogue/picks.json.
    ...CATALOGUE,
};

/** The prefab a furni kind (or a decor style) draws with by default. */
export const FURNI_PREFAB: Record<string, string> = {
    plant: 'plant',
    cups: 'cups',
    coffee: 'coffee',
    jukebox: 'jukebox',
    lamp: 'lamp',
    laptop: 'laptop',
    bed: 'bed',
    poster: 'poster',
    painting: 'painting',
    photo: 'photo',
    rug: 'rug',
    books: 'books',
    guitar: 'guitar',
    thing: 'thing',
};

/** Prefab for a spot nobody drew a scene for, by what it's called. */
export function prefabForSpot(name: string): string | null {
    const n = name.toLowerCase();
    const rules: [RegExp, string][] = [
        [/sofa|couch/, 'sofa'],
        [/bed/, 'bed'],
        [/booth/, 'booth'],
        [/bench|steps/, 'bench'],
        [/desk/, 'desk'],
        [/kitchen|stove/, 'kitchen'],
        [/counter|bar\b/, 'counter'],
        [/table/, 'long-table'],
        [/jukebox/, 'jukebox'],
        [/armchair|chair|seat/, 'armchair'],
        [/plant|garden/, 'big-plant'],
        [/shelf|book|librar/, 'books'],
        [/tv|television/, 'tv'],
        [/reception/, 'reception'],
    ];
    return rules.find(([re]) => re.test(n))?.[1] ?? null;
}
