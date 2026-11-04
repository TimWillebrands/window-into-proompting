import type { Look } from '../iso/appearance';
import type {
    BlobDoc,
    BottomDoc,
    ExtraDoc,
    FaceDoc,
    FigureDoc,
    HairDoc,
    HairZone,
    Hex,
    LegwearDoc,
    ShoeDoc,
    TopDoc,
} from './doc';

const zone = (at: HairZone['at'], o: Partial<HairZone>): HairZone => ({
    at,
    count: 10,
    len: 0.5,
    width: 0.2,
    jag: 0.3,
    lift: 0.05,
    fall: 1,
    sweep: 0,
    curl: 0,
    ...o,
});

type Style = (c: Hex) => Omit<HairDoc, 'color'>;

/** Hairstyles as zone recipes; the same shape the LLM writes, and its few-shot examples. */
export const HAIR: Record<string, Style> = {
    bald: () => ({ cap: 0, zones: [] }),
    buzz: () => ({ cap: 1, zones: [] }),
    short: () => ({
        cap: 1,
        zones: [
            zone('crown', {
                count: 12,
                len: 0.34,
                width: 0.22,
                lift: 0.1,
                fall: 0.5,
            }),
            zone('fringe', {
                count: 8,
                len: 0.3,
                width: 0.2,
                sweep: 0.5,
                fall: 0.6,
            }),
            zone('sideL', { count: 4, len: 0.26, width: 0.18 }),
            zone('sideR', { count: 4, len: 0.26, width: 0.18 }),
            zone('back', { count: 8, len: 0.34, width: 0.22 }),
        ],
    }),
    long: () => ({
        cap: 1,
        zones: [
            zone('crown', { count: 10, len: 0.55, width: 0.24, fall: 0.9 }),
            zone('fringe', { count: 8, len: 0.55, width: 0.22, sweep: 0.45 }),
            zone('sideL', { count: 8, len: 1.15, width: 0.24, jag: 0.15 }),
            zone('sideR', { count: 8, len: 1.15, width: 0.24, jag: 0.15 }),
            zone('back', { count: 16, len: 1.35, width: 0.26, jag: 0.15 }),
        ],
    }),
    bob: () => ({
        cap: 1,
        zones: [
            zone('crown', { count: 10, len: 0.5, width: 0.24, fall: 0.9 }),
            zone('fringe', { count: 10, len: 0.34, width: 0.18, jag: 0.05 }),
            zone('sideL', { count: 8, len: 0.68, width: 0.24, jag: 0.05 }),
            zone('sideR', { count: 8, len: 0.68, width: 0.24, jag: 0.05 }),
            zone('back', { count: 14, len: 0.72, width: 0.26, jag: 0.05 }),
        ],
    }),
    curly: () => ({
        cap: 1,
        zones: [
            zone('top', {
                count: 26,
                len: 0.42,
                width: 0.2,
                lift: 0.5,
                fall: 0.5,
                curl: 0.5,
            }),
            zone('sideL', {
                count: 8,
                len: 0.5,
                width: 0.2,
                lift: 0.3,
                curl: 0.5,
            }),
            zone('sideR', {
                count: 8,
                len: 0.5,
                width: 0.2,
                lift: 0.3,
                curl: 0.5,
            }),
            zone('back', {
                count: 12,
                len: 0.55,
                width: 0.22,
                lift: 0.3,
                curl: 0.5,
            }),
        ],
    }),
    afro: () => ({
        cap: 1,
        zones: [
            zone('top', {
                count: 40,
                len: 0.4,
                width: 0.26,
                lift: 1,
                fall: 0,
                curl: 0.25,
            }),
            zone('sideL', {
                count: 12,
                len: 0.38,
                width: 0.26,
                lift: 1,
                fall: 0,
                curl: 0.25,
            }),
            zone('sideR', {
                count: 12,
                len: 0.38,
                width: 0.26,
                lift: 1,
                fall: 0,
                curl: 0.25,
            }),
            zone('back', {
                count: 18,
                len: 0.4,
                width: 0.26,
                lift: 1,
                fall: 0,
                curl: 0.25,
            }),
        ],
    }),
    mohawk: () => ({
        cap: 0.4,
        zones: [
            zone([-180, 180, 74, 88], {
                count: 18,
                len: 0.5,
                width: 0.16,
                lift: 1,
                fall: 0,
                jag: 0.15,
            }),
        ],
    }),
    spiky: () => ({
        cap: 1,
        zones: [
            zone('crown', {
                count: 16,
                len: 0.42,
                width: 0.17,
                lift: 0.85,
                fall: 0,
                jag: 0.4,
            }),
            zone('fringe', {
                count: 8,
                len: 0.34,
                width: 0.16,
                lift: 0.5,
                fall: 0.1,
                jag: 0.4,
            }),
            zone('back', {
                count: 8,
                len: 0.3,
                width: 0.18,
                lift: 0.5,
                fall: 0.4,
                jag: 0.3,
            }),
        ],
    }),
    ponytail: () => ({
        cap: 1,
        zones: [
            zone('fringe', {
                count: 8,
                len: 0.3,
                width: 0.2,
                sweep: 0.4,
                fall: 0.5,
            }),
            zone('top', { count: 10, len: 0.34, width: 0.22, fall: 0.4 }),
            zone([170, 190, 28, 42], {
                count: 8,
                len: 1.15,
                width: 0.24,
                lift: 0.7,
                jag: 0.1,
            }),
        ],
    }),
    bun: () => ({
        cap: 1,
        zones: [
            zone('fringe', {
                count: 6,
                len: 0.28,
                width: 0.2,
                sweep: 0.3,
                fall: 0.5,
            }),
            zone([-180, 180, 78, 90], {
                count: 14,
                len: 0.3,
                width: 0.26,
                lift: 0.9,
                fall: 0.1,
                curl: 0.5,
            }),
        ],
    }),
    braids: () => ({
        cap: 1,
        zones: [
            zone('fringe', {
                count: 8,
                len: 0.3,
                width: 0.2,
                sweep: 0.2,
                fall: 0.5,
            }),
            zone([84, 100, 0, 16], {
                count: 5,
                len: 1.25,
                width: 0.16,
                jag: 0.05,
                curl: 0.12,
            }),
            zone([-100, -84, 0, 16], {
                count: 5,
                len: 1.25,
                width: 0.16,
                jag: 0.05,
                curl: 0.12,
            }),
        ],
    }),
    fringe: () => ({
        cap: 1,
        zones: [
            zone('crown', {
                count: 14,
                len: 0.5,
                width: 0.22,
                jag: 0.45,
                lift: 0.25,
                fall: 0.6,
                sweep: -0.2,
            }),
            zone('fringe', {
                count: 10,
                len: 0.92,
                width: 0.24,
                jag: 0.35,
                sweep: -0.9,
                fall: 0.5,
            }),
            zone('sideL', { count: 5, len: 0.7, width: 0.2, jag: 0.5 }),
            zone('sideR', { count: 5, len: 0.7, width: 0.2, jag: 0.5 }),
            zone('back', { count: 12, len: 0.72, width: 0.22, jag: 0.55 }),
        ],
    }),
    scene: (c) => ({
        cap: 1,
        zones: [
            zone('crown', {
                count: 20,
                len: 0.5,
                width: 0.2,
                jag: 0.7,
                lift: 0.7,
                fall: 0.15,
            }),
            zone('fringe', {
                count: 9,
                len: 0.8,
                width: 0.22,
                jag: 0.4,
                sweep: 0.85,
                fall: 0.5,
            }),
            zone('sideL', { count: 7, len: 1.05, width: 0.2, jag: 0.6 }),
            zone('sideR', { count: 7, len: 1.05, width: 0.2, jag: 0.6 }),
            zone('back', {
                count: 14,
                len: 1.25,
                width: 0.22,
                jag: 0.7,
                bands: [c, '#f4f1ee', c, '#f4f1ee', c],
            }),
        ],
    }),
    pigtails: () => ({
        cap: 1,
        zones: [
            zone('fringe', {
                count: 9,
                len: 0.36,
                width: 0.18,
                jag: 0.15,
                fall: 0.7,
            }),
            zone([82, 100, 30, 46], {
                count: 8,
                len: 1.05,
                width: 0.22,
                lift: 0.75,
                jag: 0.15,
                curl: 0.1,
            }),
            zone([-100, -82, 30, 46], {
                count: 8,
                len: 1.05,
                width: 0.22,
                lift: 0.75,
                jag: 0.15,
                curl: 0.1,
            }),
        ],
    }),
    undercut: () => ({
        cap: 0.5,
        zones: [
            zone('crown', {
                count: 14,
                len: 0.5,
                width: 0.2,
                lift: 0.3,
                sweep: 0.7,
                fall: 0.3,
            }),
            zone('top', {
                count: 12,
                len: 0.5,
                width: 0.2,
                lift: 0.3,
                sweep: 0.7,
                fall: 0.3,
            }),
        ],
    }),
    dreads: () => ({
        cap: 1,
        zones: [
            zone('top', {
                count: 22,
                len: 1.1,
                width: 0.13,
                jag: 0.15,
                lift: 0.15,
            }),
            zone('sideL', { count: 8, len: 1.1, width: 0.13, jag: 0.15 }),
            zone('sideR', { count: 8, len: 1.1, width: 0.13, jag: 0.15 }),
            zone('back', { count: 16, len: 1.2, width: 0.13, jag: 0.15 }),
        ],
    }),
};

const top = (color: Hex, o: Partial<TopDoc>): TopDoc => ({
    color,
    sleeve: 0.28,
    hem: 0.42,
    fit: 0.25,
    neck: 'crew',
    ...o,
});

/** Palette garments as cuts. */
const TOPS: Record<string, (c: Hex) => TopDoc> = {
    tshirt: (c) => top(c, {}),
    tank: (c) => top(c, { sleeve: 0, neck: 'scoop', fit: 0.15 }),
    shirt: (c) => top(c, { sleeve: 0.92, neck: 'collar', hem: 0.48, fit: 0.3 }),
    sweater: (c) =>
        top(c, { sleeve: 0.98, neck: 'crew', hem: 0.48, fit: 0.5, trim: c }),
    hoodie: (c) => top(c, { sleeve: 1, hem: 0.5, fit: 0.65, hood: true }),
    jacket: (c) =>
        top(c, { sleeve: 1, hem: 0.46, fit: 0.5, neck: 'collar', open: true }),
    suit: (c) => top(c, { sleeve: 1, hem: 0.56, fit: 0.3, neck: 'v', trim: c }),
    dress: (c) => top(c, { sleeve: 0.1, hem: 0.95, fit: 0.2, neck: 'scoop' }),
    'baby-tee': (c) =>
        top(c, { sleeve: 0.2, hem: 0.2, fit: 0.05, neck: 'scoop' }),
    'band-tee': (c) => top(c, { sleeve: 0.3, hem: 0.46, fit: 0.35 }),
    'long-sleeve': (c) =>
        top(c, {
            sleeve: 1,
            hem: 0.46,
            fit: 0.1,
            pattern: { kind: 'stripes', colors: [c, '#f1eef0'], scale: 0.25 },
        }),
    mesh: (c) =>
        top(c, {
            sleeve: 1,
            hem: 0.42,
            fit: 0,
            pattern: { kind: 'fishnet', colors: [c], scale: 0.15 },
            sheer: 1,
        }),
    corset: (c) => top(c, { sleeve: 0, hem: 0.34, fit: 0, neck: 'scoop' }),
    flannel: (c) =>
        top(c, {
            sleeve: 0.94,
            neck: 'collar',
            hem: 0.55,
            fit: 0.55,
            pattern: {
                kind: 'plaid',
                colors: [c, '#1b1a22', '#eae6dd'],
                scale: 0.5,
            },
        }),
    tracksuit: (c) =>
        top(c, {
            sleeve: 1,
            hem: 0.46,
            fit: 0.5,
            neck: 'turtle',
            sleeveColor: c,
            trim: '#f2f2f2',
        }),
};

const bottom = (color: Hex, o: Partial<BottomDoc>): BottomDoc => ({
    color,
    kind: 'pants',
    length: 1,
    fit: 0.3,
    flare: 0,
    rise: 0.35,
    ...o,
});

const BOTTOMS: Record<string, (c: Hex) => BottomDoc> = {
    trousers: (c) => bottom(c, { fit: 0.3, rise: 0.5 }),
    jeans: (c) => bottom(c, { fit: 0.25 }),
    shorts: (c) => bottom(c, { length: 0.42, fit: 0.45 }),
    skirt: (c) =>
        bottom(c, { kind: 'skirt', length: 0.4, flare: 0.25, rise: 0.5 }),
    'skinny-jeans': (c) => bottom(c, { fit: 0, rise: 0.22 }),
    'baggy-jeans': (c) => bottom(c, { fit: 1, flare: 0.3, rise: 0.15 }),
    bootcut: (c) => bottom(c, { fit: 0.18, flare: 0.75, rise: 0.15 }),
    cargo: (c) => bottom(c, { fit: 0.7, rise: 0.25 }),
    'plaid-skirt': (c) =>
        bottom(c, {
            kind: 'skirt',
            length: 0.16,
            flare: 0.5,
            rise: 0.55,
            pleats: 0.8,
            pattern: {
                kind: 'plaid',
                colors: [c, '#17161c', '#e8e4dc'],
                scale: 0.45,
            },
        }),
    'mini-skirt': (c) =>
        bottom(c, { kind: 'skirt', length: 0.1, flare: 0.1, rise: 0.4 }),
};

const shoe = (
    color: Hex,
    kind: ShoeDoc['kind'],
    sole = '#ece8df',
    trim = color,
    platform = 0,
): ShoeDoc => ({ kind, color, sole, trim, platform });

const SHOES: Record<string, (c: Hex) => ShoeDoc> = {
    sneakers: (c) => shoe(c, 'sneaker', '#f1ede4', '#f1ede4'),
    'skate-shoes': (c) => shoe(c, 'skate', '#f4f1ea', '#f4f1ea', 0.12),
    'hi-tops': (c) => shoe(c, 'hi-top', '#f1ede4', '#f1ede4'),
    boots: (c) => shoe(c, 'boot', '#1b1a20', c),
    platforms: (c) => shoe(c, 'platform', '#1b1a20', '#c9c9d4', 0.65),
    heels: (c) => shoe(c, 'heel', '#1b1a20', c, 0.25),
    sandals: (c) => shoe(c, 'sandal', '#8a6a48', c),
    loafers: (c) => shoe(c, 'loafer', '#1b1a20', c),
};

const LEGWEAR: Record<string, () => LegwearDoc | undefined> = {
    none: () => undefined,
    'knee-socks': () => ({ color: '#f1eef0', length: 0.5 }),
    'striped-socks': () => ({
        color: '#141318',
        length: 0.55,
        pattern: {
            kind: 'stripes',
            colors: ['#141318', '#f1eef0'],
            scale: 0.2,
        },
    }),
    tights: () => ({ color: '#16151b', length: 1 }),
    fishnets: () => ({
        color: '#141318',
        length: 1,
        sheer: 1,
        pattern: { kind: 'fishnet', colors: ['#141318'], scale: 0.12 },
    }),
};

const face = (look: Look): FaceDoc => {
    const dark = look.skin < '#9';
    return {
        shape: 0.5,
        jaw: look.build === 'broad' ? 0.6 : 0.4,
        nose: 0.4,
        ears: 0.45,
        // A near-black iris reads as a hole when small; brown does the same job.
        eyes: {
            shape: 'almond',
            size: 0.65,
            iris: look.eyes === '#1d1d1d' ? '#4a3020' : look.eyes,
            liner: 0.12,
            wing: 0,
            linerColor: '#1c1418',
        },
        brows: { color: look.hair, weight: 0.45, arch: 0.45 },
        mouth: {
            width: 0.5,
            lips: 0.5,
            color: dark ? '#7b4640' : '#c07a72',
            set: 0.15,
        },
        facialHair: (
            {
                stubble: 'stubble',
                beard: 'beard',
                moustache: 'moustache',
            } as Record<string, FaceDoc['facialHair']>
        )[look.facialHair],
        facialHairColor: look.hair,
    };
};

const SLIDER: Record<string, number> = {
    slim: 0.25,
    average: 0.45,
    broad: 0.8,
    short: 0.2,
    tall: 0.85,
};

/** A palette Look as a Figure doc, the LLM's Design laid over it slot by slot. */
export function figureDoc(look: Look): FigureDoc {
    const hair = (HAIR[look.hairStyle] ?? HAIR.short)(look.hair);
    const cut = TOPS[look.topStyle] ?? TOPS.tshirt;
    const leg = BOTTOMS[look.bottomStyle] ?? BOTTOMS.trousers;
    const base: FigureDoc = {
        body: {
            skin: look.skin,
            height: SLIDER[look.height] ?? 0.5,
            build: SLIDER[look.build] ?? 0.45,
            curve: 0.4,
            shoulders: 0.5,
            hips: 0.5,
            legs: 0.55,
            head: 0.5,
        },
        face: face(look),
        hair: { color: look.hair, ...hair },
        top: cut(look.top),
        bottom: leg(look.bottom),
        legwear: (LEGWEAR[look.legwear] ?? LEGWEAR.none)(),
        shoes: (SHOES[look.shoeStyle] ?? SHOES.sneakers)(look.shoes),
        extras: (EXTRAS[look.accessory] ?? EXTRAS.none)(),
    };
    if (look.topStyle === 'dress')
        base.bottom = bottom(look.top, {
            kind: 'skirt',
            length: 0.02,
            rise: 0.3,
        });
    const d = look.design;
    return {
        ...base,
        ...d,
        face: { ...base.face, ...d?.face },
        // A dye over a designed cut: the cut stays, the palette colour shows.
        hair: { ...base.hair, ...d?.hair, color: d?.hair?.color ?? look.hair },
        // The palette accessory is worn on top of whatever was made.
        extras: [...base.extras, ...(d?.extras ?? [])],
    };
}

const ball = (
    x: number,
    y: number,
    z: number,
    r: number,
    color: Hex,
    metal = false,
): BlobDoc => ({ shape: 'ball', at: [x, y, z], r, color, metal });
const rod = (
    from: [number, number, number],
    to: [number, number, number],
    r: number,
    color: Hex,
    metal = false,
): BlobDoc => ({ shape: 'capsule', at: from, to, r, color, metal });
const hoop = (
    x: number,
    y: number,
    z: number,
    r: number,
    tube: number,
    axis: 'x' | 'y' | 'z',
    color: Hex,
    metal = false,
): BlobDoc => ({ shape: 'ring', at: [x, y, z], r, tube, axis, color, metal });
const strap = (
    socket: ExtraDoc['socket'],
    name: string,
    width: number,
    color: Hex,
): ExtraDoc => ({
    name,
    socket,
    blobs: [{ shape: 'band', at: [0, 0, 0], r: width, tube: 0.012, color }],
});
const STUD = '#d9dde4';
const frames = (lens: Hex, frame = '#17151c'): ExtraDoc[] => [
    {
        name: 'glasses',
        socket: 'face',
        blobs: [
            hoop(0.175, -0.05, 0.035, 0.115, 0.016, 'z', frame),
            hoop(-0.175, -0.05, 0.035, 0.115, 0.016, 'z', frame),
            rod([-0.06, -0.03, 0.04], [0.06, -0.03, 0.04], 0.014, frame),
            rod([0.29, -0.03, 0.02], [0.4, -0.02, -0.34], 0.013, frame),
            rod([-0.29, -0.03, 0.02], [-0.4, -0.02, -0.34], 0.013, frame),
            ...(lens === 'none'
                ? []
                : [
                      ball(0.175, -0.05, 0.03, 0.1, lens),
                      ball(-0.175, -0.05, 0.03, 0.1, lens),
                  ]),
        ],
    },
];

/** Palette extras as blob recipes; the LLM's few-shot examples too. */
export const EXTRAS: Record<string, () => ExtraDoc[]> = {
    none: () => [],
    glasses: () => frames('none'),
    sunglasses: () => frames('#101016'),
    earrings: () => [
        {
            name: 'earring-l',
            socket: 'ear-l',
            blobs: [hoop(0.02, -0.14, 0, 0.05, 0.012, 'x', '#e0b84c', true)],
        },
        {
            name: 'earring-r',
            socket: 'ear-r',
            blobs: [hoop(-0.02, -0.14, 0, 0.05, 0.012, 'x', '#e0b84c', true)],
        },
    ],
    necklace: () => [
        {
            name: 'necklace',
            socket: 'neck',
            blobs: [
                {
                    shape: 'band',
                    at: [0, -0.1, 0],
                    r: 0.012,
                    tube: 0.01,
                    color: '#e0b84c',
                    metal: true,
                },
            ],
        },
    ],
    choker: () => [
        {
            ...strap('neck', 'choker', 0.04, '#121116'),
            blobs: [
                {
                    shape: 'band',
                    at: [0, 0.02, 0],
                    r: 0.04,
                    tube: 0.012,
                    color: '#121116',
                },
                ball(0, -0.02, 0.17, 0.03, STUD, true),
            ],
        },
    ],
    scarf: () => [
        {
            name: 'scarf',
            socket: 'neck',
            blobs: [
                {
                    shape: 'band',
                    at: [0, -0.02, 0],
                    r: 0.14,
                    tube: 0.07,
                    color: '#e8a33c',
                },
                rod([0.1, -0.12, 0.2], [0.13, -0.62, 0.26], 0.07, '#e8a33c'),
            ],
        },
    ],
    'studded-belt': () => [
        {
            name: 'studded-belt',
            socket: 'waist',
            blobs: [
                {
                    shape: 'band',
                    at: [0, 0, 0],
                    r: 0.055,
                    tube: 0.012,
                    color: '#14131a',
                },
                ...[-0.4, -0.28, -0.16, -0.04, 0.08, 0.2, 0.32, 0.44].map((x) =>
                    ball(x, 0, 0.3 - Math.abs(x) * 0.28, 0.03, STUD, true),
                ),
            ],
        },
    ],
    wristbands: () => [
        strap('wrist-l', 'wristband-l', 0.07, '#f04f9c'),
        strap('wrist-r', 'wristband-r', 0.07, '#111014'),
    ],
    'arm-warmers': () => [
        {
            name: 'arm-warmer-l',
            socket: 'forearm-l',
            blobs: [
                {
                    shape: 'band',
                    at: [0, 0, 0],
                    r: 0.45,
                    tube: 0.012,
                    color: '#141318',
                },
            ],
        },
        {
            name: 'arm-warmer-r',
            socket: 'forearm-r',
            blobs: [
                {
                    shape: 'band',
                    at: [0, 0, 0],
                    r: 0.45,
                    tube: 0.012,
                    color: '#141318',
                },
            ],
        },
    ],
    'wallet-chain': () => [
        {
            name: 'wallet-chain',
            socket: 'hip-r',
            blobs: [0, 1, 2, 3, 4, 5, 6].map((i) =>
                hoop(
                    -0.02 - i * 0.012,
                    -0.05 - i * 0.075 + Math.sin(i / 2) * 0.01,
                    0.1 - Math.abs(3 - i) * 0.03,
                    0.034,
                    0.01,
                    i % 2 ? 'x' : 'z',
                    STUD,
                    true,
                ),
            ),
        },
    ],
    headphones: () => [
        {
            name: 'headphones',
            socket: 'head',
            blobs: [
                hoop(0, 0.06, -0.02, 0.45, 0.022, 'z', '#26262c'),
                ball(0.42, -0.04, 0, 0.11, '#26262c'),
                ball(-0.42, -0.04, 0, 0.11, '#26262c'),
            ],
        },
    ],
    beanie: () => [
        {
            name: 'beanie',
            socket: 'head',
            blobs: [
                {
                    shape: 'shell',
                    at: [0, 0.16, 0],
                    r: 0.055,
                    color: '#b43a2e',
                    hat: true,
                },
            ],
        },
    ],
    hat: () => [
        {
            name: 'hat',
            socket: 'head',
            blobs: [
                {
                    shape: 'shell',
                    at: [0, 0.18, 0],
                    r: 0.05,
                    color: '#4a3b2a',
                    hat: true,
                },
                hoop(0, 0.18, -0.02, 0.5, 0.05, 'y', '#4a3b2a'),
                hoop(0, 0.2, -0.02, 0.42, 0.03, 'y', '#c0392b'),
            ],
        },
    ],
    cap: () => [
        {
            name: 'cap',
            socket: 'head',
            blobs: [
                {
                    shape: 'shell',
                    at: [0, 0.2, 0],
                    r: 0.05,
                    color: '#2c3e66',
                    hat: true,
                },
                rod([0, 0.21, 0.42], [0, 0.17, 0.76], 0.05, '#20304f'),
                rod([-0.18, 0.2, 0.42], [-0.16, 0.17, 0.7], 0.04, '#20304f'),
                rod([0.18, 0.2, 0.42], [0.16, 0.17, 0.7], 0.04, '#20304f'),
            ],
        },
    ],
    bandana: () => [
        {
            name: 'bandana',
            socket: 'head',
            blobs: [
                {
                    shape: 'shell',
                    at: [0, 0.22, 0],
                    r: 0.03,
                    color: '#c0392b',
                    hat: true,
                },
                rod([0, 0.2, -0.42], [0.05, -0.05, -0.52], 0.05, '#c0392b'),
            ],
        },
    ],
    flower: () => [
        {
            name: 'flower',
            socket: 'head',
            blobs: [0, 1, 2, 3, 4]
                .map((i) =>
                    ball(
                        0.34 + Math.cos(i * 1.26) * 0.06,
                        0.24 + Math.sin(i * 1.26) * 0.06,
                        0.2,
                        0.05,
                        '#ff8fb8',
                    ),
                )
                .concat(ball(0.34, 0.24, 0.24, 0.035, '#ffd166')),
        },
    ],
    bow: () => [
        {
            name: 'bow',
            socket: 'head',
            blobs: [
                rod([0.14, 0.42, 0.14], [0.34, 0.46, 0.1], 0.06, '#e63946'),
                rod([0.14, 0.42, 0.14], [0.02, 0.5, 0.12], 0.06, '#e63946'),
                ball(0.14, 0.43, 0.15, 0.05, '#b5202c'),
            ],
        },
    ],
};

/** What this file can draw, per backend palette list; a test keeps them in step. */
export const paletteCovers = (): Record<string, string[]> => ({
    Styles: Object.keys(HAIR),
    TopStyles: Object.keys(TOPS),
    BottomStyles: Object.keys(BOTTOMS),
    ShoeStyles: Object.keys(SHOES),
    Legwears: Object.keys(LEGWEAR),
    Accessories: Object.keys(EXTRAS),
});
