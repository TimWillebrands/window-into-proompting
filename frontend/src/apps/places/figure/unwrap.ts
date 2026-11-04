import type { Anatomy } from './anatomy';
import type { Surface } from './nets';
import type { Field, Loft } from './sdf';
import { clamp } from './sdf';

/** Texture panels, Roblox-template style: [u, v, w, h] of the atlas, v up. */
export const PANELS = {
    front: [0, 0.5, 0.5, 0.5],
    back: [0.5, 0.5, 0.5, 0.5],
    armL: [0, 0.375, 1, 0.125],
    armR: [0, 0.25, 1, 0.125],
    legL: [0, 0.125, 1, 0.125],
    legR: [0, 0, 1, 0.125],
} as const;
export type Panel = keyof typeof PANELS;

export interface PanelFrame {
    /** Bind-pose heights the front and back panels span. */
    y0: number;
    y1: number;
}

export interface Unwrapped extends Surface {
    uvs: Float32Array;
}

const NAMES = Object.keys(PANELS) as Panel[];

/** Gives a garment panel UVs, splitting vertices along panel edges so seams stay clean. */
export function unwrap(a: Anatomy, s: Surface, frame: PanelFrame): Unwrapped {
    const n = s.positions.length / 3;
    const panel = new Uint8Array(n);
    const pu = new Float32Array(n);
    const pv = new Float32Array(n);
    const half = (frame.y1 - frame.y0) / 2;
    const limb = (
        l: Loft,
        x: number,
        y: number,
        z: number,
        i: number,
        p: Panel,
    ) => {
        l.d(x, y, z);
        panel[i] = NAMES.indexOf(p);
        pu[i] = clamp(l.t, 0, 1);
        // Legs: angle 0 is the back, so the seam goes there and the panel's middle is the front.
        const turn = l.angle / (Math.PI * 2);
        pv[i] = p === 'legL' || p === 'legR' ? (turn + 1) % 1 : turn + 0.5;
    };
    for (let i = 0; i < n; i++) {
        const x = s.positions[i * 3];
        const y = s.positions[i * 3 + 1];
        const z = s.positions[i * 3 + 2];
        const arm = x > 0 ? a.armL : a.armR;
        const leg = x > 0 ? a.legL : a.legR;
        const dA = arm.d(x, y, z);
        const tA = arm.t;
        const dL = leg.d(x, y, z);
        const tL = leg.t;
        const dT = a.torso.d(x, y, z);
        if (tA > 0.04 && dA < dT)
            limb(arm, x, y, z, i, x > 0 ? 'armL' : 'armR');
        else if (tL > 0.06 && dL < dT + 0.02)
            limb(leg, x, y, z, i, x > 0 ? 'legL' : 'legR');
        else {
            const front = z >= 0;
            panel[i] = front ? 0 : 1;
            pu[i] = clamp(0.5 + (front ? x : -x) / (2 * half), 0, 1);
            pv[i] = clamp((y - frame.y0) / (frame.y1 - frame.y0), 0, 1);
        }
    }

    const seen = new Map<number, number>();
    const pos: number[] = [];
    const nrm: number[] = [];
    const uv: number[] = [];
    const indices = new Uint32Array(s.indices.length);
    for (let t = 0; t < s.indices.length; t += 3) {
        const tri = [s.indices[t], s.indices[t + 1], s.indices[t + 2]];
        // A triangle lives on its majority panel.
        const own =
            panel[tri[1]] === panel[tri[2]] ? panel[tri[1]] : panel[tri[0]];
        const rect = PANELS[NAMES[own]];
        const wraps =
            own >= 2 &&
            Math.max(...tri.map((v) => pv[v])) -
                Math.min(...tri.map((v) => pv[v])) >
                0.5;
        for (let k = 0; k < 3; k++) {
            const v = tri[k];
            const key = v * 8 + own + (wraps ? 6 : 0);
            let out = seen.get(key);
            if (out === undefined) {
                out = pos.length / 3;
                seen.set(key, out);
                pos.push(
                    s.positions[v * 3],
                    s.positions[v * 3 + 1],
                    s.positions[v * 3 + 2],
                );
                nrm.push(
                    s.normals[v * 3],
                    s.normals[v * 3 + 1],
                    s.normals[v * 3 + 2],
                );
                const u0 = panel[v] === own ? pu[v] : clamp(pu[tri[0]], 0, 1);
                const v0 = wraps
                    ? pv[tri[0]]
                    : panel[v] === own
                      ? pv[v]
                      : pv[tri[0]];
                uv.push(rect[0] + u0 * rect[2], rect[1] + v0 * rect[3]);
            }
            indices[t + k] = out;
        }
    }
    return {
        positions: new Float32Array(pos),
        normals: new Float32Array(nrm),
        indices,
        uvs: new Float32Array(uv),
    };
}

/** Soft occlusion, baked per vertex: how much of the air just off the surface something else is using. */
export function occlusion(
    s: Surface,
    field: Field,
    reach: number,
): Float32Array {
    const n = s.positions.length / 3;
    const out = new Float32Array(n);
    for (let i = 0; i < n; i++) {
        const x = s.positions[i * 3];
        const y = s.positions[i * 3 + 1];
        const z = s.positions[i * 3 + 2];
        const nx = s.normals[i * 3];
        const ny = s.normals[i * 3 + 1];
        const nz = s.normals[i * 3 + 2];
        let shade = 0;
        for (const k of [0.5, 1]) {
            const d = field(
                x + nx * reach * k,
                y + ny * reach * k,
                z + nz * reach * k,
            );
            shade += clamp(1 - d / (reach * k), 0, 1) * (k === 1 ? 0.6 : 0.4);
        }
        out[i] = shade;
    }
    return out;
}
