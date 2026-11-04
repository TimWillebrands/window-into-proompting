import * as THREE from 'three';
import type { BodyDoc } from './doc';
import type { V3 } from './sdf';
import { mix } from './sdf';
import bones from './skeleton.gen.json';

export interface BoneRest {
    name: string;
    parent: number;
    t: V3;
    q: [number, number, number, number];
}

export const BONES = bones as BoneRest[];
export const boneIndex = new Map(BONES.map((b, i) => [b.name, i]));

/** How far the arms are lowered in the bind pose: A-pose skins better than the T the clips rest in. */
const ARM_DROP = 0.92;
/** Height of a person with average sliders, in tiles. */
export const BASE_HEIGHT = 1.32;
/** The rig's own height, near enough. */
const RIG_HEIGHT = 1.8;

export interface Proportions {
    /** Total height. */
    H: number;
    headH: number;
    hipY: number;
    neckY: number;
    shoulderX: number;
    hipX: number;
    armLen: number;
}

export function proportions(b: BodyDoc): Proportions {
    const H = BASE_HEIGHT * mix(0.92, 1.09, b.height);
    return {
        H,
        headH: H * mix(0.215, 0.265, b.head),
        hipY: H * mix(0.43, 0.5, b.legs),
        neckY: H * 0.71,
        shoulderX: H * mix(0.092, 0.122, b.shoulders),
        hipX: H * mix(0.042, 0.058, b.hips),
        armLen: H * 0.325,
    };
}

export interface Posed {
    /** Local translation per bone. */
    t: V3[];
    /** Local rotation per bone, bind pose. */
    q: THREE.Quaternion[];
    /** World position per bone, bind pose. */
    world: V3[];
    /** World rotation per bone, bind pose. */
    worldQ: THREE.Quaternion[];
    p: Proportions;
    /** What the clip's pelvis translation is multiplied by. */
    pelvisScale: number;
}

const chainOf = (
    name: string,
): 'leg' | 'spine' | 'neck' | 'arm' | 'hand' | 'other' => {
    if (/^(thigh|calf|foot|ball)/.test(name)) return 'leg';
    if (/^spine|^clavicle|^neck/.test(name)) return 'spine';
    if (name === 'Head') return 'neck';
    if (/^(lowerarm|hand)/.test(name)) return 'arm';
    if (/^(index|middle|ring|pinky|thumb)/.test(name)) return 'hand';
    return 'other';
};

/** The rig scaled to a body and lowered into the bind pose; rest rotations are untouched so clips apply as they are. */
export function poseFor(b: BodyDoc): Posed {
    const p = proportions(b);
    const hipRig = 0.932;
    const legScale = p.hipY / hipRig;
    const spineScale = (p.neckY - p.hipY) / (1.488 - hipRig);
    const neckScale = (p.H - p.headH * 0.86 - p.neckY) / (1.569 - 1.488);
    const armScale = p.armLen / 0.547;
    const handScale = (p.H / RIG_HEIGHT) * 0.92;
    const t = BONES.map((bone) => {
        const s = {
            leg: legScale,
            spine: spineScale,
            neck: neckScale,
            arm: armScale,
            hand: handScale,
            other: 1,
        }[chainOf(bone.name)];
        const v: V3 = [bone.t[0] * s, bone.t[1] * s, bone.t[2] * s];
        if (bone.name === 'pelvis') {
            v[1] *= spineScale;
            v[2] *= legScale;
        }
        if (/^thigh/.test(bone.name)) v[0] = Math.sign(bone.t[0]) * p.hipX;
        if (/^upperarm/.test(bone.name)) {
            // Clavicle length sets the shoulder line.
            const k = (p.shoulderX - 0.019 * spineScale) / 0.173;
            v[0] = bone.t[0] * k;
            v[1] = bone.t[1] * k;
            v[2] = bone.t[2] * k;
        }
        return v;
    });
    const q = BONES.map((bone) => new THREE.Quaternion(...bone.q));
    const world: V3[] = [];
    const worldQ: THREE.Quaternion[] = [];
    const drop = (side: 1 | -1) =>
        new THREE.Quaternion().setFromAxisAngle(
            new THREE.Vector3(0, 0, 1),
            -side * ARM_DROP,
        );
    BONES.forEach((bone, i) => {
        const parentQ =
            bone.parent < 0 ? new THREE.Quaternion() : worldQ[bone.parent];
        const parentP =
            bone.parent < 0 ? ([0, 0, 0] as V3) : world[bone.parent];
        const v = new THREE.Vector3(...t[i]).applyQuaternion(parentQ);
        world[i] = [parentP[0] + v.x, parentP[1] + v.y, parentP[2] + v.z];
        const wq = parentQ.clone().multiply(q[i]);
        if (bone.name === 'upperarm_l' || bone.name === 'upperarm_r') {
            wq.premultiply(drop(bone.name.endsWith('_l') ? 1 : -1));
            q[i] = parentQ.clone().invert().multiply(wq);
        }
        worldQ[i] = wq;
    });
    return { t, q, world, worldQ, p, pelvisScale: legScale };
}

export const joint = (posed: Posed, name: string): V3 =>
    posed.world[boneIndex.get(name) as number];
