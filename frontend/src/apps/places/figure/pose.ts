import * as THREE from 'three';
import type { MoveDoc } from './moves';
import { BONES } from './skeleton';

/** A posture in words a person (or LLM) would use; degrees, all optional. See ADR 0028. */
export interface PoseDoc {
    /** [lean forward, turn, tilt]. */
    spine?: [number, number, number];
    head?: [number, number, number];
    /** [forward, out, twist], from hanging at the side. */
    armL?: [number, number, number];
    armR?: [number, number, number];
    elbowL?: number;
    elbowR?: number;
    /** [forward, out], from straight down. */
    legL?: [number, number];
    legR?: [number, number];
    kneeL?: number;
    kneeR?: number;
    /** Hips sink by this much of the leg (0..1). */
    sink?: number;
    /** Hips shift toward their left (+) or right, in leg lengths. */
    shift?: number;
    handL?: Hand;
    handR?: Hand;
}

export type Hand =
    | 'relaxed'
    | 'open'
    | 'fist'
    | 'point'
    | 'peace'
    | 'horns'
    | 'grip';

const deg = THREE.MathUtils.degToRad;

const restLocal = BONES.map((b) => new THREE.Quaternion(...b.q));
const restWorld: THREE.Quaternion[] = [];
BONES.forEach((b, i) => {
    restWorld[i] = (
        b.parent < 0 ? new THREE.Quaternion() : restWorld[b.parent].clone()
    ).multiply(restLocal[i]);
});
const index = new Map(BONES.map((b, i) => [b.name, i]));

/** Local rotation for a bone turned by r about world-aligned axes, relative to the T rest. */
function turned(name: string, r: THREE.Quaternion): THREE.Quaternion {
    const i = index.get(name) as number;
    const w = restWorld[i];
    return restLocal[i]
        .clone()
        .multiply(w.clone().invert().multiply(r).multiply(w));
}

const E = (x: number, y: number, z: number, order: THREE.EulerOrder = 'XYZ') =>
    new THREE.Quaternion().setFromEuler(
        new THREE.Euler(deg(x), deg(y), deg(z), order),
    );

const FINGERS = ['index', 'middle', 'ring', 'pinky'] as const;

/** Curl per finger for each hand shape, 0..1, and the thumb. */
const HANDS: Record<Hand, [number, number, number, number, number]> = {
    relaxed: [0.3, 0.35, 0.4, 0.45, 0.2],
    open: [0.02, 0.02, 0.02, 0.02, 0],
    fist: [1, 1, 1, 1, 0.8],
    grip: [0.7, 0.75, 0.8, 0.8, 0.5],
    point: [0, 1, 1, 1, 0.8],
    peace: [0, 0, 1, 1, 0.8],
    horns: [0, 1, 1, 0, 0.8],
};

export interface PoseTargets {
    q: Map<string, THREE.Quaternion>;
    /** Pelvis offset in rig units: [side, up, forward]. */
    pelvis: [number, number, number];
}

const PELVIS_Z = 0.917;

/** Pose words → bone rotations on the rig. */
export function poseTargets(p: PoseDoc): PoseTargets {
    const q = new Map<string, THREE.Quaternion>();
    const [lean, turn, tilt] = p.spine ?? [0, 0, 0];
    for (const [bone, share] of [
        ['spine_01', 0.3],
        ['spine_02', 0.35],
        ['spine_03', 0.35],
    ] as const)
        q.set(bone, turned(bone, E(lean * share, turn * share, -tilt * share)));
    const [nod, look, cock] = p.head ?? [0, 0, 0];
    q.set(
        'neck_01',
        turned('neck_01', E(nod * 0.45, look * 0.45, -cock * 0.45)),
    );
    q.set('Head', turned('Head', E(nod * 0.55, look * 0.55, -cock * 0.55)));

    for (const [side, sign] of [
        ['l', 1],
        ['r', -1],
    ] as const) {
        const up = side === 'l' ? p.armL : p.armR;
        const [fwd, out, twist] = up ?? [0, 8, 0];
        // Down from the T, out by some, swung forward, then rolled along itself.
        const hang = E(0, 0, -sign * (90 - out));
        const swing = E(-fwd, 0, 0);
        const roll = new THREE.Quaternion().setFromAxisAngle(
            new THREE.Vector3(sign, 0, 0),
            deg(twist),
        );
        q.set(
            `upperarm_${side}`,
            turned(`upperarm_${side}`, swing.multiply(hang).multiply(roll)),
        );
        const bend = (side === 'l' ? p.elbowL : p.elbowR) ?? 12;
        q.set(
            `lowerarm_${side}`,
            turned(`lowerarm_${side}`, E(0, -sign * bend, 0)),
        );
        q.set(`clavicle_${side}`, turned(`clavicle_${side}`, E(0, 0, 0)));
        q.set(`hand_${side}`, turned(`hand_${side}`, E(0, 0, 0)));

        const leg = side === 'l' ? p.legL : p.legR;
        const [lf, lo] = leg ?? [0, 0];
        q.set(`thigh_${side}`, turned(`thigh_${side}`, E(-lf, 0, sign * lo)));
        const knee = (side === 'l' ? p.kneeL : p.kneeR) ?? 0;
        q.set(`calf_${side}`, turned(`calf_${side}`, E(knee, 0, 0)));
        q.set(`foot_${side}`, turned(`foot_${side}`, E(0, 0, 0)));
        q.set(`ball_${side}`, turned(`ball_${side}`, E(0, 0, 0)));

        const shape = HANDS[(side === 'l' ? p.handL : p.handR) ?? 'relaxed'];
        FINGERS.forEach((f, i) => {
            for (const [n, k] of [
                ['01', 62],
                ['02', 88],
                ['03', 58],
            ] as const)
                q.set(
                    `${f}_${n}_${side}`,
                    turned(`${f}_${n}_${side}`, E(0, 0, -sign * shape[i] * k)),
                );
        });
        for (const [n, k] of [
            ['02', 24],
            ['03', 30],
        ] as const)
            q.set(
                `thumb_${n}_${side}`,
                turned(`thumb_${n}_${side}`, E(0, 0, -sign * shape[4] * k)),
            );
    }
    const sink = (p.sink ?? 0) * 0.932;
    return { q, pelvis: [(p.shift ?? 0) * 0.932, 0, -sink] };
}

/** A keyed move as a looping clip; pelvis translation scaled to the Figure. */
export function moveClip(
    name: string,
    move: MoveDoc,
    pelvisScale: number,
): THREE.AnimationClip {
    const keys = move.keys.map((k) => ({ t: k.t, ...poseTargets(k) }));
    // Close the loop back onto the first key.
    const times = [...keys.map((k) => k.t * move.loop), move.loop];
    const ring = [...keys, keys[0]];
    const tracks: THREE.KeyframeTrack[] = [...keys[0].q.keys()].map((bone) => {
        const v: number[] = [];
        for (const k of ring)
            v.push(...(k.q.get(bone) as THREE.Quaternion).toArray());
        return new THREE.QuaternionKeyframeTrack(
            `${bone}.quaternion`,
            times,
            v,
        );
    });
    const rest = BONES[index.get('pelvis') as number].t;
    const v: number[] = [];
    for (const k of ring)
        v.push(
            (rest[0] + k.pelvis[0]) * pelvisScale,
            rest[1] * pelvisScale,
            (PELVIS_Z + k.pelvis[2]) * pelvisScale,
        );
    tracks.push(new THREE.VectorKeyframeTrack('pelvis.position', times, v));
    return new THREE.AnimationClip(name, move.loop, tracks);
}
