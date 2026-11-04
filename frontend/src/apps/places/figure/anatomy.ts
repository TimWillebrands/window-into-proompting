import type { BodyDoc, FaceDoc } from './doc';
import {
    capsule,
    ellipsoid,
    type Field,
    Loft,
    mix,
    smin,
    sphere,
    ssub,
    type V3,
} from './sdf';
import { joint, type Posed } from './skeleton';

export interface Anatomy {
    H: number;
    headH: number;
    /** Centre of the head. */
    headC: V3;
    torso: Loft;
    neck: Loft;
    armL: Loft;
    armR: Loft;
    legL: Loft;
    legR: Loft;
    footL: Loft;
    footR: Loft;
    /** Torso with bust and seat. */
    chest: Field;
    /** Torso, neck, arms and legs blended. */
    trunk: Field;
    head: Field;
    /** Skull only, for hair to grow from. */
    skull: Field;
    handL: Field;
    handR: Field;
    /** Softness of cut edges: a hem can't be crisper than the grid it's sampled on. */
    soft: number;
    /** Where the elbow sits on the arm, the knee on the leg (0..1). */
    elbow: number;
    knee: number;
    waistT: number;
}

/** Blended solid of a chest, with a bust and a seat. */
function torsoField(torso: Loft, body: BodyDoc, H: number): Field {
    const c = body.curve;
    const bustY = torso.at(0.72)[1];
    const r = (0.03 + c * 0.012) * H;
    const bustL = ellipsoid(
        [0.04 * H, bustY, 0.03 * H + c * 0.012 * H],
        [r, r * 0.9, r * (0.55 + c * 0.35)],
    );
    const bustR = ellipsoid(
        [-0.04 * H, bustY, 0.03 * H + c * 0.012 * H],
        [r, r * 0.9, r * (0.55 + c * 0.35)],
    );
    const seatY = torso.at(0.1)[1];
    const seatR = (0.056 + c * 0.012) * H;
    const seatL = ellipsoid(
        [0.046 * H, seatY, -0.03 * H],
        [seatR, seatR, seatR * 0.95],
    );
    const seatRt = ellipsoid(
        [-0.046 * H, seatY, -0.03 * H],
        [seatR, seatR, seatR * 0.95],
    );
    const k = 0.04 * H;
    return (x, y, z) => {
        let d = torso.d(x, y, z);
        d = smin(d, x > 0 ? bustL(x, y, z) : bustR(x, y, z), k);
        d = smin(d, x > 0 ? seatL(x, y, z) : seatRt(x, y, z), k);
        return d;
    };
}

function handField(
    posed: Posed,
    side: 'l' | 'r',
    H: number,
    fingers: boolean,
): Field {
    const j = (n: string) => joint(posed, `${n}_${side}`);
    const wrist = j('hand');
    const knuckle = j('middle_01');
    const palm = capsule(wrist, knuckle, 0.019 * H, 0.024 * H);
    const palmFlat = (x: number, y: number, z: number) => palm(x, y, z);
    const parts: Field[] = [];
    if (fingers)
        for (const f of ['index', 'middle', 'ring', 'pinky']) {
            parts.push(
                capsule(j(`${f}_01`), j(`${f}_02`), 0.0085 * H, 0.0078 * H),
                capsule(j(`${f}_02`), j(`${f}_03`), 0.0078 * H, 0.007 * H),
                capsule(j(`${f}_03`), j(`${f}_04_leaf`), 0.007 * H, 0.006 * H),
            );
        }
    else
        parts.push(capsule(knuckle, j('middle_04_leaf'), 0.021 * H, 0.015 * H));
    parts.push(
        capsule(j('thumb_01'), j('thumb_02'), 0.011 * H, 0.0095 * H),
        capsule(j('thumb_02'), j('thumb_03'), 0.0095 * H, 0.0085 * H),
        capsule(j('thumb_03'), j('thumb_04_leaf'), 0.0085 * H, 0.007 * H),
    );
    return (x, y, z) => {
        let d = palmFlat(x, y, z);
        for (const p of parts) d = smin(d, p(x, y, z), 0.008 * H);
        return d;
    };
}

function headFields(
    c: V3,
    s: number,
    face: FaceDoc,
): { head: Field; skull: Field } {
    const at = (x: number, y: number, z: number): V3 => [
        c[0] + x * s,
        c[1] + y * s,
        c[2] + z * s,
    ];
    const long = mix(0.94, 1.06, face.shape);
    const wide = mix(1.05, 0.94, face.shape);
    const cranium = ellipsoid(at(0, 0.07, -0.02), [
        0.4 * s * wide,
        0.43 * s,
        0.44 * s,
    ]);
    const jawW = mix(0.27, 0.34, face.jaw) * wide;
    const jaw = ellipsoid(at(0, -0.16 * long, 0.03), [
        jawW * s,
        0.34 * s * long,
        0.35 * s,
    ]);
    const chin = sphere(
        at(0, -0.385 * long, 0.11),
        mix(0.1, 0.13, face.jaw) * s,
    );
    const noseR = mix(0.03, 0.058, face.nose) * s;
    const nose = capsule(
        at(0, -0.02, 0.4),
        at(0, -0.15, 0.462),
        0.018 * s,
        noseR,
    );
    const eyeL = sphere(at(0.17, -0.02, 0.49), 0.11 * s);
    const eyeR = sphere(at(-0.17, -0.02, 0.49), 0.11 * s);
    const earSize = mix(0.8, 1.25, face.ears);
    const earL = ellipsoid(at(0.385 * wide, -0.03, -0.03), [
        0.035 * s,
        0.115 * s * earSize,
        0.08 * s * earSize,
    ]);
    const earR = ellipsoid(at(-0.385 * wide, -0.03, -0.03), [
        0.035 * s,
        0.115 * s * earSize,
        0.08 * s * earSize,
    ]);
    const lips = ellipsoid(at(0, -0.285 * long, 0.385), [
        0.1 * s,
        0.03 * s,
        0.04 * s,
    ]);
    const skull: Field = (x, y, z) => {
        const a = smin(cranium(x, y, z), jaw(x, y, z), 0.2 * s);
        return smin(a, chin(x, y, z), 0.16 * s);
    };
    const head: Field = (x, y, z) => {
        let d = skull(x, y, z);
        d = ssub(d, eyeL(x, y, z), 0.09 * s);
        d = ssub(d, eyeR(x, y, z), 0.09 * s);
        d = smin(d, nose(x, y, z), 0.09 * s);
        d = smin(d, lips(x, y, z), 0.05 * s);
        d = smin(d, earL(x, y, z), 0.035 * s);
        d = smin(d, earR(x, y, z), 0.035 * s);
        return d;
    };
    return { head, skull };
}

/** The bare body as fields, shaped by its sliders and hung on the bind pose. */
export function anatomy(
    posed: Posed,
    body: BodyDoc,
    face: FaceDoc,
    fingers: boolean,
    voxel: number,
): Anatomy {
    const { H, headH, hipY } = posed.p;
    const thick = mix(0.84, 1.28, body.build);
    const c = body.curve;
    const hips = mix(0.92, 1.1, body.hips) * mix(0.95, 1.08, c) * thick;
    const waist = mix(1.06, 0.86, c) * thick;
    const chest = mix(0.92, 1.1, body.shoulders) * mix(1.04, 0.96, c) * thick;

    const neckBase = joint(posed, 'neck_01');
    const crotchY = hipY - 0.05 * H;
    const torso = new Loft(
        [0, crotchY, -0.002 * H],
        [0, neckBase[1] + 0.02 * H, -0.004 * H],
        [
            { t: 0, u: 0.056 * H * hips, v: 0.05 * H * thick },
            {
                t: 0.16,
                u: 0.09 * H * hips,
                v: 0.064 * H * thick,
                ov: -0.006 * H,
            },
            {
                t: 0.34,
                u: 0.08 * H * mix(hips, waist, 0.5),
                v: 0.054 * H * thick,
            },
            {
                t: 0.52,
                u: 0.066 * H * waist,
                v: 0.047 * H * thick,
                ov: 0.006 * H,
            },
            {
                t: 0.74,
                u: 0.082 * H * chest,
                v: 0.056 * H * thick,
                ov: 0.004 * H,
            },
            { t: 0.88, u: 0.08 * H * chest, v: 0.05 * H * thick },
            { t: 0.965, u: 0.05 * H * chest, v: 0.036 * H * thick },
            { t: 1, u: 0.03 * H, v: 0.03 * H },
        ],
    );

    const neckR = 0.03 * H * mix(0.9, 1.2, body.build);
    const headBone = joint(posed, 'Head');
    const neck = new Loft(
        [0, neckBase[1] - 0.012 * H, -0.004 * H],
        [headBone[0], headBone[1] + 0.02 * H, headBone[2]],
        [
            { t: 0, u: neckR * 1.25, v: neckR * 1.2 },
            { t: 0.5, u: neckR, v: neckR },
            { t: 1, u: neckR * 1.02, v: neckR * 1.02 },
        ],
    );

    const arm = (side: 'l' | 'r') => {
        const a = joint(posed, `upperarm_${side}`);
        const e = joint(posed, `lowerarm_${side}`);
        const w = joint(posed, `hand_${side}`);
        const elbow =
            Math.hypot(e[0] - a[0], e[1] - a[1], e[2] - a[2]) /
            Math.hypot(w[0] - a[0], w[1] - a[1], w[2] - a[2]);
        const r = thick * H;
        return {
            elbow,
            loft: new Loft(
                a,
                w,
                [
                    { t: 0, u: 0.036 * r, v: 0.036 * r },
                    { t: 0.12, u: 0.038 * r, v: 0.038 * r },
                    { t: 0.3, u: 0.031 * r, v: 0.032 * r },
                    { t: elbow - 0.03, u: 0.0255 * r, v: 0.0265 * r },
                    { t: elbow + 0.1, u: 0.028 * r, v: 0.029 * r },
                    { t: 0.85, u: 0.0205 * r, v: 0.0185 * r },
                    { t: 1, u: 0.0185 * r, v: 0.0155 * r },
                ],
                [0, 0, 1],
            ),
        };
    };

    const leg = (side: 'l' | 'r') => {
        const a = joint(posed, `thigh_${side}`);
        const k = joint(posed, `calf_${side}`);
        const f = joint(posed, `foot_${side}`);
        const knee =
            Math.hypot(k[0] - a[0], k[1] - a[1], k[2] - a[2]) /
            Math.hypot(f[0] - a[0], f[1] - a[1], f[2] - a[2]);
        const r = thick * H;
        const thigh = mix(0.96, 1.1, c) * mix(0.96, 1.04, body.hips);
        return {
            knee,
            loft: new Loft(a, f, [
                { t: 0, u: 0.064 * r * thigh, v: 0.07 * r * thigh },
                { t: 0.16, u: 0.06 * r * thigh, v: 0.064 * r * thigh },
                { t: 0.36, u: 0.046 * r, v: 0.05 * r },
                { t: knee - 0.02, u: 0.0345 * r, v: 0.037 * r },
                { t: knee + 0.02, u: 0.033 * r, v: 0.036 * r },
                { t: knee + 0.14, u: 0.037 * r, v: 0.042 * r, ov: -0.004 * H },
                { t: 0.88, u: 0.022 * r, v: 0.024 * r },
                { t: 1, u: 0.02 * r, v: 0.021 * r },
            ]),
        };
    };

    const foot = (side: 'l' | 'r') => {
        const ankle = joint(posed, `foot_${side}`);
        const toe = joint(posed, `ball_leaf_${side}`);
        const heel: V3 = [ankle[0], 0.028 * H, ankle[2] - 0.034 * H];
        return new Loft(
            heel,
            [toe[0], 0.022 * H, toe[2]],
            [
                { t: 0, u: 0.024 * H, v: 0.028 * H },
                { t: 0.3, u: 0.026 * H, v: 0.032 * H },
                { t: 0.7, u: 0.032 * H, v: 0.022 * H },
                { t: 1, u: 0.026 * H, v: 0.016 * H },
            ],
        );
    };

    const left = arm('l');
    const right = arm('r');
    const legL = leg('l');
    const legR = leg('r');
    const footL = foot('l');
    const footR = foot('r');
    const chestField = torsoField(torso, body, H);
    const kArm = 0.05 * H;
    const kLeg = 0.05 * H;
    const trunk: Field = (x, y, z) => {
        let d = chestField(x, y, z);
        d = smin(d, neck.d(x, y, z), 0.03 * H);
        d = smin(d, (x > 0 ? left : right).loft.d(x, y, z), kArm);
        d = smin(d, (x > 0 ? legL : legR).loft.d(x, y, z), kLeg);
        d = smin(d, (x > 0 ? footL : footR).d(x, y, z), 0.03 * H);
        return d;
    };

    const headC: V3 = [0, H - headH / 2, 0.01 * H];
    const { head, skull } = headFields(headC, headH, face);
    return {
        H,
        headH,
        headC,
        torso,
        neck,
        armL: left.loft,
        armR: right.loft,
        legL: legL.loft,
        legR: legR.loft,
        footL,
        footR,
        chest: chestField,
        trunk,
        head,
        skull,
        handL: handField(posed, 'l', H, fingers),
        handR: handField(posed, 'r', H, fingers),
        soft: voxel * 1.1,
        elbow: left.elbow,
        knee: legL.knee,
        waistT: 0.52,
    };
}
