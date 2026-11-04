import type { Hand, PoseDoc } from './pose';

/** A looping gesture: keyed poses, the unit an LLM can author. t is 0..1 of the loop. */
export interface MoveDoc {
    /** Loop length in seconds. */
    loop: number;
    keys: ({ t: number } & PoseDoc)[];
}

const still = (pose: PoseDoc): MoveDoc => ({
    loop: 1,
    keys: [{ t: 0, ...pose }],
});

const hands = (h: Hand): Pick<PoseDoc, 'handL' | 'handR'> => ({
    handL: h,
    handR: h,
});

const STAND: PoseDoc = {
    spine: [3, 0, 0],
    // The camera sits high: a lifted chin keeps faces in view.
    head: [-7, 0, 0],
    armL: [3, 7, 0],
    armR: [3, 7, 0],
    elbowL: 14,
    elbowR: 14,
    legL: [1, 2.5],
    legR: [-1, 2.5],
    ...hands('relaxed'),
};

const sway = (p: PoseDoc, over: PoseDoc = {}): PoseDoc => ({ ...p, ...over });

/** Standing about: weight shifts, a glance round. A stance doc replaces the base pose. */
export const standMove = (stance?: PoseDoc): MoveDoc => {
    const base = { ...STAND, ...stance };
    const [lean = 3, turn = 0, tilt = 0] = base.spine ?? [];
    const [nod = 0, look = 0, cock = 0] = base.head ?? [];
    const breathe = (k: number) => ({
        spine: [lean + k, turn, tilt] as [number, number, number],
    });
    return {
        loop: 9,
        keys: [
            { t: 0, ...base, ...breathe(0), head: [nod, look, cock] },
            {
                t: 0.18,
                ...base,
                ...breathe(1.4),
                head: [nod + 1, look + 9, cock + 1],
            },
            { t: 0.36, ...base, ...breathe(0), head: [nod, look + 11, cock] },
            {
                t: 0.55,
                ...base,
                ...breathe(1.4),
                head: [nod - 1, look - 7, cock - 1],
            },
            {
                t: 0.75,
                ...base,
                ...breathe(0),
                head: [nod + 1, look - 5, cock],
            },
        ],
    };
};

export const talkMove = (stance?: PoseDoc): MoveDoc => {
    const base = { ...STAND, ...stance };
    const [nod = 0, look = 0, cock = 0] = base.head ?? [];
    return {
        loop: 3.2,
        keys: [
            {
                t: 0,
                ...base,
                head: [nod, look, cock],
                armR: [14, 12, -20],
                elbowR: 62,
                handR: 'open',
            },
            {
                t: 0.22,
                ...base,
                head: [nod + 5, look + 4, cock + 2],
                armR: [26, 18, -30],
                elbowR: 84,
                handR: 'open',
            },
            {
                t: 0.48,
                ...base,
                head: [nod - 1, look - 5, cock - 2],
                armL: [18, 14, -20],
                elbowL: 72,
                armR: [10, 10, -10],
                elbowR: 40,
                handL: 'open',
            },
            {
                t: 0.74,
                ...base,
                head: [nod + 4, look + 1, cock + 1],
                armL: [10, 10, -10],
                elbowL: 38,
                armR: [22, 14, -24],
                elbowR: 76,
                handR: 'open',
            },
        ],
    };
};

const PHONE: PoseDoc = sway(STAND, {
    armR: [46, 2, 22],
    elbowR: 104,
    head: [20, 0, 0],
    handR: 'grip',
    spine: [5, 0, 0],
});

/** The IMVU-style hang-out poses (ADR 0025), now data. */
export const MOVES: Record<string, MoveDoc> = {
    phone: still(PHONE),
    'sit-phone': still({
        spine: [10, 0, 0],
        armR: [50, 2, 22],
        elbowR: 108,
        armL: [34, 6, 0],
        elbowL: 56,
        head: [20, 0, 0],
        handR: 'grip',
        legL: [84, 0],
        legR: [84, 0],
        kneeL: 84,
        kneeR: 84,
    }),
    lean: still({
        spine: [14, 0, 0],
        armL: [58, 8, 0],
        armR: [58, 8, 0],
        elbowL: 52,
        elbowR: 52,
        head: [-8, 0, 0],
        legL: [2, 3],
        legR: [-8, 3],
        kneeR: 14,
    }),
    lounge: still({
        spine: [-26, 0, 0],
        legL: [70, 6],
        legR: [64, -4],
        kneeL: 14,
        kneeR: 24,
        armR: [152, 12, 0],
        elbowR: 128,
        armL: [18, 24, 0],
        elbowL: 12,
        head: [10, 0, 0],
    }),
    perch: {
        loop: 2.8,
        keys: [
            {
                t: 0,
                spine: [5, 0, 0],
                legL: [84, 2],
                legR: [84, 2],
                kneeL: 74,
                kneeR: 98,
                armL: [8, 18, 0],
                armR: [8, 18, 0],
                elbowL: 4,
                elbowR: 4,
                ...hands('grip'),
            },
            {
                t: 0.5,
                spine: [5, 0, 0],
                legL: [84, 2],
                legR: [84, 2],
                kneeL: 98,
                kneeR: 74,
                armL: [8, 18, 0],
                armR: [8, 18, 0],
                elbowL: 4,
                elbowR: 4,
                ...hands('grip'),
            },
        ],
    },
    'cross-legged': still({
        spine: [6, 0, 0],
        legL: [80, 48],
        legR: [80, 48],
        kneeL: 140,
        kneeR: 140,
        armL: [42, 6, 0],
        armR: [42, 6, 0],
        elbowL: 30,
        elbowR: 30,
        sink: 0.32,
    }),
    think: {
        loop: 5,
        keys: [
            {
                t: 0,
                armR: [46, 2, 18],
                elbowR: 136,
                armL: [30, 12, 34],
                elbowL: 82,
                head: [6, 12, 2],
                spine: [3, 0, 0],
                handR: 'relaxed',
            },
            {
                t: 0.5,
                armR: [46, 2, 18],
                elbowR: 132,
                armL: [30, 12, 34],
                elbowL: 84,
                head: [9, 8, 4],
                spine: [4, 0, 0],
                handR: 'relaxed',
            },
        ],
    },
    wave: {
        loop: 0.8,
        keys: [
            { t: 0, ...STAND, armR: [18, 148, 0], elbowR: 26, handR: 'open' },
            { t: 0.5, ...STAND, armR: [18, 156, 0], elbowR: 58, handR: 'open' },
        ],
    },
    hug: still({
        spine: [7, 0, 0],
        armL: [80, 14, 0],
        armR: [80, 14, 0],
        elbowL: 74,
        elbowR: 74,
        head: [6, 22, 0],
        ...hands('open'),
    }),
    'slow-dance': {
        loop: 4.4,
        keys: [
            {
                t: 0,
                armL: [72, 24, 0],
                armR: [72, 24, 0],
                elbowL: 42,
                elbowR: 42,
                spine: [3, 0, 4],
                head: [3, 18, 0],
                legL: [2, 3],
                legR: [-2, 3],
            },
            {
                t: 0.5,
                armL: [72, 24, 0],
                armR: [72, 24, 0],
                elbowL: 42,
                elbowR: 42,
                spine: [3, 0, -4],
                head: [3, 18, 0],
                legL: [-2, 3],
                legR: [2, 3],
            },
        ],
    },
    'high-five': {
        loop: 1.2,
        keys: [
            { t: 0, ...STAND, armR: [158, 8, 0], elbowR: 12, handR: 'open' },
            {
                t: 0.5,
                ...STAND,
                armR: [150, 8, 0],
                elbowR: 24,
                handR: 'open',
                sink: 0.015,
            },
        ],
    },
    'hold-hands-r': still({
        ...STAND,
        armR: [12, 32, 0],
        elbowR: 8,
        head: [0, -20, 0],
        handR: 'grip',
    }),
    'hold-hands-l': still({
        ...STAND,
        armL: [12, 32, 0],
        elbowL: 8,
        head: [0, 20, 0],
        handL: 'grip',
    }),
    lie: still({
        armL: [0, 16, 0],
        armR: [0, 16, 0],
        elbowL: 16,
        elbowR: 16,
        legL: [2, 4],
        legR: [2, 4],
        kneeL: 4,
        kneeR: 4,
        head: [6, 0, 0],
    }),
    sleep: still({
        armL: [0, 22, 0],
        armR: [150, 22, 0],
        elbowL: 18,
        elbowR: 110,
        legL: [14, 5],
        legR: [2, 3],
        kneeL: 22,
        head: [4, 16, 0],
    }),
};

/** Hang-outs that take the weight off the feet. */
export const SEATED = new Set(['lounge', 'perch', 'cross-legged']);
export const PAIRED = new Set(['hug', 'slow-dance', 'high-five', 'hold-hands']);
