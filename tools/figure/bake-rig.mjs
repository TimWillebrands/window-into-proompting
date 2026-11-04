#!/usr/bin/env node
// Bakes Quaternius' Universal Animation Library (CC0) into the Figure rig: node tools/figure/bake-rig.mjs <UAL1_Standard.glb>
import { readFileSync, writeFileSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const ROOT = resolve(dirname(fileURLToPath(import.meta.url)), '../..');
const OUT = join(ROOT, 'frontend/src/apps/places/figure');
const CLIPS = {
    Idle_Loop: 'idle',
    Idle_Talking_Loop: 'talk',
    Walk_Loop: 'walk',
    Walk_Formal_Loop: 'stride',
    Jog_Fwd_Loop: 'jog',
    Sitting_Idle_Loop: 'sit',
    Sitting_Talking_Loop: 'sit-talk',
    Dance_Loop: 'dance',
    Crouch_Idle_Loop: 'crouch',
    Interact: 'interact',
    PickUp_Table: 'pick-up',
    Hit_Chest: 'flinch',
};
const FPS = 15;
const EPS = 0.004;

const glb = readFileSync(process.argv[2]);
const jsonLength = glb.readUInt32LE(12);
const gltf = JSON.parse(glb.subarray(20, 20 + jsonLength).toString());
const bin = glb.subarray(20 + jsonLength + 8);

const accessor = (i) => {
    const a = gltf.accessors[i];
    const view = gltf.bufferViews[a.bufferView];
    const width = { SCALAR: 1, VEC3: 3, VEC4: 4 }[a.type];
    const start = (view.byteOffset ?? 0) + (a.byteOffset ?? 0);
    const out = new Float32Array(a.count * width);
    for (let k = 0; k < out.length; k++) out[k] = bin.readFloatLE(start + k * 4);
    return { values: out, width };
};

const skin = gltf.skins[0];
const joints = new Set(skin.joints);
const parents = new Map();
gltf.nodes.forEach((n, i) => {
    for (const c of n.children ?? []) parents.set(c, i);
});
const r3 = (v) => Math.round(v * 1e4) / 1e4;
// Parents first, so a bone's parent index is always lower.
const order = [];
const visit = (i) => {
    if (joints.has(i)) order.push(i);
    for (const c of gltf.nodes[i].children ?? []) visit(c);
};
for (const i of joints) if (!joints.has(parents.get(i))) visit(i);
const bones = order.map((i) => {
    const n = gltf.nodes[i];
    return {
        name: n.name,
        parent: order.indexOf(parents.get(i)),
        t: (n.translation ?? [0, 0, 0]).map(r3),
        q: (n.rotation ?? [0, 0, 0, 1]).map(r3),
    };
});

const lerpKey = (track, time) => {
    const { times, values, width } = track;
    if (time <= times[0]) return [...values.subarray(0, width)];
    let k = 1;
    while (k < times.length - 1 && times[k] < time) k++;
    const f = (time - times[k - 1]) / (times[k] - times[k - 1] || 1);
    const a = values.subarray((k - 1) * width, k * width);
    const b = values.subarray(k * width, (k + 1) * width);
    const flip = width === 4 && a.reduce((s, v, i) => s + v * b[i], 0) < 0 ? -1 : 1;
    const out = [...a].map((v, i) => v + (b[i] * flip - v) * Math.min(1, f));
    const len = width === 4 ? Math.hypot(...out) : 1;
    return out.map((v) => v / len);
};

/** Drops keys a straight line between their neighbours reproduces. */
function thin(times, keys) {
    const keep = [0];
    for (let i = 1; i < keys.length - 1; i++) {
        const a = keep[keep.length - 1];
        const f = (times[i] - times[a]) / (times[i + 1] - times[a]);
        const off = keys[i].reduce(
            (m, v, c) => Math.max(m, Math.abs(v - (keys[a][c] + (keys[i + 1][c] - keys[a][c]) * f))),
            0,
        );
        if (off > EPS) keep.push(i);
    }
    keep.push(keys.length - 1);
    return keep;
}

const clips = {};
for (const anim of gltf.animations) {
    const name = CLIPS[anim.name];
    if (!name) continue;
    const tracks = {};
    let duration = 0;
    for (const ch of anim.channels) {
        const node = gltf.nodes[ch.target.node];
        const isPelvis = node.name === 'pelvis' && ch.target.path === 'translation';
        if (ch.target.path !== 'rotation' && !isPelvis) continue;
        const sampler = anim.samplers[ch.sampler];
        const input = accessor(sampler.input).values;
        const { values, width } = accessor(sampler.output);
        duration = Math.max(duration, input[input.length - 1]);
        const track = { times: input, values, width };
        const steps = Math.max(2, Math.round(input[input.length - 1] * FPS) + 1);
        const times = Array.from({ length: steps }, (_, i) => (input[input.length - 1] * i) / (steps - 1));
        const keys = times.map((t) => lerpKey(track, t));
        const rest = isPelvis ? node.translation : node.rotation ?? [0, 0, 0, 1];
        const still = keys.every((k) => k.every((v, c) => Math.abs(v - rest[c]) < EPS));
        if (still) continue;
        const keep = thin(times, keys);
        tracks[isPelvis ? 'pelvis.position' : node.name] = {
            t: keep.map((i) => r3(times[i])),
            v: keep.flatMap((i) => keys[i].map(r3)),
        };
    }
    clips[name] = { duration: r3(duration), tracks };
}

writeFileSync(join(OUT, 'skeleton.gen.json'), `${JSON.stringify(bones)}\n`);
writeFileSync(join(OUT, 'clips.gen.json'), `${JSON.stringify(clips)}\n`);
console.log(`${bones.length} bones, ${Object.keys(clips).length} clips`);
