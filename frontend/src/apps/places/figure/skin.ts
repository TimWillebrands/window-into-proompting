import type { Anatomy } from './anatomy';
import { clamp, type Loft, smooth, type V3 } from './sdf';
import { boneIndex, joint, type Posed } from './skeleton';

const idx = (name: string) => boneIndex.get(name) as number;

type Weights = Map<number, number>;

const add = (w: Weights, bone: number, v: number) => {
    if (v > 1e-4) w.set(bone, (w.get(bone) ?? 0) + v);
};

const AFFINITY = 0.016;

export interface Skinner {
    /** Fills four bones and weights for a point in bind pose. */
    weigh(
        x: number,
        y: number,
        z: number,
        into: Uint16Array,
        weights: Float32Array,
        at: number,
    ): void;
}

/** Pins everything to one bone: heads, hair, hard accessories. */
export const rigid = (bone: string): Skinner => {
    const i = idx(bone);
    return {
        weigh(_x, _y, _z, into, weights, at) {
            into[at] = i;
            weights[at] = 1;
        },
    };
};

/** Smooth weights from the body's own parts, so clothes bend exactly as what's under them bends. */
export function bodySkinner(a: Anatomy, posed: Posed): Skinner {
    const H = a.H;
    const tau = AFFINITY * H;
    const spine = ['pelvis', 'spine_01', 'spine_02', 'spine_03', 'neck_01'].map(
        (n) => ({ bone: idx(n), y: joint(posed, n)[1] }),
    );
    const shoulderY = joint(posed, 'upperarm_l')[1];
    const shoulderX = joint(posed, 'upperarm_l')[0];

    const spineWeights = (y: number, x: number, w: Weights, k: number) => {
        // A vertex follows the bone it's on, easing into the next at mid-bone.
        for (let i = 0; i < spine.length - 1; i++) {
            const lo = spine[i];
            const hi = spine[i + 1];
            if (y <= hi.y || i === spine.length - 2) {
                const f = clamp((y - lo.y) / (hi.y - lo.y), 0, 1);
                const hiShare = smooth(0.25, 0.95, f);
                add(w, lo.bone, k * (1 - hiShare));
                add(w, hi.bone, k * hiShare);
                break;
            }
        }
        // The top corners of the chest ride the clavicles.
        const c =
            smooth(shoulderX * 0.35, shoulderX * 0.9, Math.abs(x)) *
            smooth(shoulderY - 0.09 * H, shoulderY - 0.01 * H, y);
        if (c > 0) {
            const clav = idx(x > 0 ? 'clavicle_l' : 'clavicle_r');
            for (const [b, v] of w) w.set(b, v * (1 - c * 0.8));
            add(w, clav, k * c * 0.8);
        }
    };

    const armWeights = (
        arm: Loft,
        t: number,
        side: 'l' | 'r',
        w: Weights,
        k: number,
    ) => {
        const wrist = smooth(0.97, 1.03, t);
        const elbow = smooth(a.elbow - 0.07, a.elbow + 0.07, t);
        add(w, idx(`upperarm_${side}`), k * (1 - elbow));
        add(w, idx(`lowerarm_${side}`), k * elbow * (1 - wrist));
        add(w, idx(`hand_${side}`), k * wrist);
        void arm;
    };

    const legWeights = (t: number, side: 'l' | 'r', w: Weights, k: number) => {
        const ankle = smooth(0.96, 1.02, t);
        const knee = smooth(a.knee - 0.06, a.knee + 0.06, t);
        add(w, idx(`thigh_${side}`), k * (1 - knee));
        add(w, idx(`calf_${side}`), k * knee * (1 - ankle));
        add(w, idx(`foot_${side}`), k * ankle);
    };

    const scratch: Weights = new Map();
    return {
        weigh(x, y, z, into, weights, at) {
            const w = scratch;
            w.clear();
            const side = x > 0 ? 'l' : 'r';
            const arm = x > 0 ? a.armL : a.armR;
            const leg = x > 0 ? a.legL : a.legR;
            const foot = x > 0 ? a.footL : a.footR;
            const dT = a.torso.d(x, y, z);
            const dN = a.neck.d(x, y, z);
            const dA = arm.d(x, y, z);
            const tA = arm.t;
            const dL = leg.d(x, y, z);
            const tL = leg.t;
            const dF = foot.d(x, y, z);
            const least = Math.min(dT, dN, dA, dL, dF);
            const aff = (d: number, tight = 1) =>
                Math.exp(-(d - least) / (tau * tight));
            const kT = aff(dT);
            const kN = aff(dN, 0.6);
            const kA = aff(dA, 0.8);
            const kL = aff(dL, 1.2);
            const kF = aff(dF, 0.6);
            spineWeights(y, x, w, kT);
            const headBone = joint(posed, 'Head');
            const up = smooth(
                headBone[1] - 0.03 * H,
                headBone[1] + 0.01 * H,
                y,
            );
            add(w, idx('neck_01'), kN * (1 - up));
            add(w, idx('Head'), kN * up);
            armWeights(arm, tA, side, w, kA);
            legWeights(tL, side, w, kL);
            add(w, idx(`foot_${side}`), kF);

            const sorted = [...w].sort((p, q) => q[1] - p[1]).slice(0, 4);
            const sum = sorted.reduce((s, p) => s + p[1], 0) || 1;
            for (let i = 0; i < 4; i++) {
                into[at + i] = sorted[i]?.[0] ?? 0;
                weights[at + i] = sorted[i] ? sorted[i][1] / sum : 0;
            }
        },
    };
}

/** Weights for a hand: palm to the hand bone, each finger to its nearest joint. */
export function handSkinner(posed: Posed, side: 'l' | 'r'): Skinner {
    const fingers = ['index', 'middle', 'ring', 'pinky', 'thumb'].flatMap((f) =>
        ['01', '02', '03'].map((n) => ({
            bone: idx(`${f}_${n}_${side}`),
            at: joint(posed, `${f}_${n}_${side}`),
            to: joint(
                posed,
                n === '03'
                    ? `${f}_04_leaf_${side}`
                    : `${f}_0${Number(n) + 1}_${side}`,
            ),
        })),
    );
    const palm = idx(`hand_${side}`);
    const wristAt = joint(posed, `hand_${side}`);
    const knuckle = joint(posed, `middle_01_${side}`);
    const dist = (p: V3, a: V3, b: V3) => {
        const bx = b[0] - a[0];
        const by = b[1] - a[1];
        const bz = b[2] - a[2];
        const t = clamp(
            ((p[0] - a[0]) * bx + (p[1] - a[1]) * by + (p[2] - a[2]) * bz) /
                (bx * bx + by * by + bz * bz || 1),
            0,
            1,
        );
        return Math.hypot(
            p[0] - a[0] - bx * t,
            p[1] - a[1] - by * t,
            p[2] - a[2] - bz * t,
        );
    };
    return {
        weigh(x, y, z, into, weights, at) {
            const p: V3 = [x, y, z];
            let best = palm;
            let least = dist(p, wristAt, knuckle) - 0.016;
            for (const f of fingers) {
                const d = dist(p, f.at, f.to);
                if (d < least) {
                    least = d;
                    best = f.bone;
                }
            }
            into[at] = best;
            weights[at] = 1;
        },
    };
}
