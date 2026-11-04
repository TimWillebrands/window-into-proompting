import type { Anatomy } from './anatomy';
import type { BlobDoc, ExtraDoc, FigureDoc, Hex, Socket } from './doc';
import { type Surface, surfaceNets } from './nets';
import { capsule, type Field, type Loft, sphere, type V3 } from './sdf';
import { joint, type Posed } from './skeleton';

interface Mount {
    at: V3;
    bone: string;
    /** The limb a band wraps, and where on it. */
    limb?: Loft;
    t?: number;
}

function mount(a: Anatomy, posed: Posed, socket: Socket): Mount {
    const s = a.headH;
    const c = a.headC;
    const side = socket.endsWith('-l') ? 'l' : 'r';
    const arm = side === 'l' ? a.armL : a.armR;
    const leg = side === 'l' ? a.legL : a.legR;
    switch (socket) {
        case 'head':
            return { at: c, bone: 'Head' };
        case 'face':
            return { at: [c[0], c[1], c[2] + 0.44 * s], bone: 'Head' };
        case 'ear-l':
        case 'ear-r':
            return {
                at: [(side === 'l' ? 1 : -1) * 0.39 * s, c[1] - 0.08 * s, c[2]],
                bone: 'Head',
            };
        case 'neck':
            return {
                at: a.neck.at(0.45),
                bone: 'neck_01',
                limb: a.neck,
                t: 0.45,
            };
        case 'chest':
            return {
                at: [0, a.torso.at(0.72)[1], 0.075 * a.H],
                bone: 'spine_03',
            };
        case 'back':
            return {
                at: [0, a.torso.at(0.72)[1], -0.07 * a.H],
                bone: 'spine_03',
            };
        case 'waist':
            return {
                at: a.torso.at(0.3),
                bone: 'pelvis',
                limb: a.torso,
                t: 0.3,
            };
        case 'hip-l':
        case 'hip-r':
            return {
                at: [
                    (side === 'l' ? 1 : -1) * 0.095 * a.H,
                    a.torso.at(0.2)[1],
                    0.01 * a.H,
                ],
                bone: 'pelvis',
            };
        case 'wrist-l':
        case 'wrist-r':
            return {
                at: arm.at(0.93),
                bone: `lowerarm_${side}`,
                limb: arm,
                t: 0.93,
            };
        case 'forearm-l':
        case 'forearm-r':
            return {
                at: arm.at(0.72),
                bone: `lowerarm_${side}`,
                limb: arm,
                t: 0.72,
            };
        case 'hand-l':
        case 'hand-r':
            return { at: joint(posed, `hand_${side}`), bone: `hand_${side}` };
        default:
            return {
                at: leg.at(0.94),
                bone: `calf_${side}`,
                limb: leg,
                t: 0.94,
            };
    }
}

export interface Extra {
    surface: Surface;
    colors: Float32Array;
    bone: string;
    metal: boolean;
}

const linear = (hex: Hex): V3 => {
    const n = Number.parseInt(hex.slice(1), 16);
    return [(n >> 16) & 255, (n >> 8) & 255, n & 255].map((c) => {
        const k = c / 255;
        return k <= 0.04045 ? k / 12.92 : ((k + 0.055) / 1.055) ** 2.4;
    }) as V3;
};

/** One blob as a field in world space, and how far it reaches. */
function blobField(
    b: BlobDoc,
    m: Mount,
    a: Anatomy,
    grow: number,
): { field: Field; reach: number; centre: V3 } {
    const s = a.headH;
    const c = a.headC;
    const at: V3 = [
        m.at[0] + b.at[0] * s,
        m.at[1] + b.at[1] * s,
        m.at[2] + b.at[2] * s,
    ];
    const r = b.r * s;
    switch (b.shape) {
        case 'capsule': {
            const to = b.to ?? b.at;
            const end: V3 = [
                m.at[0] + to[0] * s,
                m.at[1] + to[1] * s,
                m.at[2] + to[2] * s,
            ];
            const mid: V3 = [
                (at[0] + end[0]) / 2,
                (at[1] + end[1]) / 2,
                (at[2] + end[2]) / 2,
            ];
            return {
                field: capsule(at, end, r),
                reach:
                    Math.hypot(end[0] - at[0], end[1] - at[1], end[2] - at[2]) /
                        2 +
                    r,
                centre: mid,
            };
        }
        case 'ring': {
            const tube = (b.tube ?? 0.03) * s;
            const axis = b.axis ?? 'z';
            return {
                field: (x, y, z) => {
                    const p = [x - at[0], y - at[1], z - at[2]];
                    const [u, v, w] =
                        axis === 'x'
                            ? [p[1], p[2], p[0]]
                            : axis === 'y'
                              ? [p[0], p[2], p[1]]
                              : [p[0], p[1], p[2]];
                    return Math.hypot(Math.hypot(u, v) - r, w) - tube;
                },
                reach: r + tube,
                centre: at,
            };
        }
        case 'band': {
            const limb = m.limb;
            if (!limb) return { field: sphere(at, r), reach: r, centre: at };
            const thick = (b.tube ?? 0.02) * s + grow;
            const t0 = (m.t ?? 0.5) + (b.at[1] * s) / limb.length;
            return {
                field: (x, y, z) => {
                    const d = limb.d(x, y, z, thick);
                    return Math.max(d, Math.abs(limb.t - t0) * limb.length - r);
                },
                reach: Math.max(...limb.radii(t0)) + thick + r + 0.02 * s,
                centre: limb.at(t0),
            };
        }
        case 'shell': {
            const edge = c[1] + b.at[1] * s;
            return {
                field: (x, y, z) => Math.max(a.skull(x, y, z) - r, edge - y),
                reach: 0.72 * s,
                centre: c,
            };
        }
        default:
            return { field: sphere(at, r), reach: r, centre: at };
    }
}

/** Accessories as blended solids hung on sockets: the LLM's way of making something new to wear. */
export function buildExtra(
    e: ExtraDoc,
    a: Anatomy,
    posed: Posed,
    h: number,
    grow: number,
): Extra[] {
    const m = mount(a, posed, e.socket);
    const out: Extra[] = [];
    for (const metal of [false, true]) {
        const blobs = e.blobs.filter((b) => !!b.metal === metal);
        if (blobs.length === 0) continue;
        const parts = blobs.map((b) => ({
            ...blobField(b, m, a, grow),
            doc: b,
        }));
        const lo: V3 = [Infinity, Infinity, Infinity];
        const hi: V3 = [-Infinity, -Infinity, -Infinity];
        for (const p of parts)
            for (let k = 0; k < 3; k++) {
                lo[k] = Math.min(lo[k], p.centre[k] - p.reach - 2 * h);
                hi[k] = Math.max(hi[k], p.centre[k] + p.reach + 2 * h);
            }
        const field: Field = (x, y, z) => {
            let d = parts[0].field(x, y, z);
            for (let i = 1; i < parts.length; i++)
                d = Math.min(d, parts[i].field(x, y, z));
            return d;
        };
        const surface = surfaceNets(field, lo, hi, h);
        const colors = new Float32Array(surface.positions.length);
        const palette = parts.map((p) => linear(p.doc.color));
        for (let i = 0; i < colors.length; i += 3) {
            let best = 0;
            let least = Infinity;
            parts.forEach((p, k) => {
                const d = Math.abs(
                    p.field(
                        surface.positions[i],
                        surface.positions[i + 1],
                        surface.positions[i + 2],
                    ),
                );
                if (d < least) {
                    least = d;
                    best = k;
                }
            });
            colors.set(palette[best], i);
        }
        out.push({ surface, colors, bone: m.bone, metal });
    }
    return out;
}

/** The lowest hat edge in a doc, in world height: hair above it stays under the hat. */
export function hatLine(doc: FigureDoc, a: Anatomy): number {
    let y = Number.POSITIVE_INFINITY;
    for (const e of doc.extras)
        for (const b of e.blobs)
            if (b.hat)
                y = Math.min(
                    y,
                    a.headC[1] + b.at[1] * a.headH + 0.02 * a.headH,
                );
    return y;
}
