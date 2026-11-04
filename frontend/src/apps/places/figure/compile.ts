import { type Anatomy, anatomy } from './anatomy';
import type { FigureDoc, Hex } from './doc';
import { buildExtra, hatLine } from './extras';
import { bottomField, legwearField, shoeFields, topField } from './garments';
import { growHair, smoothColours } from './hair';
import { type Surface, surfaceNets } from './nets';
import { type Field, sint, smin, smooth, type V3 } from './sdf';
import { BONES, boneIndex, type Posed, poseFor } from './skeleton';
import { bodySkinner, handSkinner, rigid, type Skinner } from './skin';
import { occlusion, type PanelFrame, unwrap } from './unwrap';

export type LayerKind = 'skin' | 'head' | 'cloth' | 'hair';

export interface Layer {
    name: string;
    kind: LayerKind;
    color: Hex;
    /** Which garment to paint on this layer's atlas. */
    paint?: 'top' | 'outer' | 'under' | 'bottom' | 'legwear';
    frame?: PanelFrame;
    /** Glossy highlight, 0..1. */
    shine?: number;
    /** Skin shows through: no culling under it, no ink line. */
    sheer?: boolean;
    /** Holes painted in: cut them, don't blend. */
    holes?: boolean;
    opacity?: number;
    positions: Float32Array;
    normals: Float32Array;
    indices: Uint32Array;
    skinIndex: Uint16Array;
    skinWeight: Float32Array;
    /** Linear RGB, occlusion baked in. */
    colors: Float32Array;
    uvs?: Float32Array;
}

export interface Compiled {
    layers: Layer[];
    /** Local translation and rotation per bone. */
    bones: { t: V3; q: [number, number, number, number] }[];
    /** How high the soles lift the whole Figure. */
    lift: number;
    pelvisScale: number;
    height: number;
    chestY: number;
    bellyY: number;
    H: number;
}

export interface Detail {
    /** Cell size for the body, in tiles. */
    voxel: number;
    fingers: boolean;
}

export const SCENE_DETAIL: Detail = { voxel: 0.021, fingers: false };
export const CLOSE_DETAIL: Detail = { voxel: 0.013, fingers: true };

function skinned(surface: Surface, skinner: Skinner) {
    const count = surface.positions.length / 3;
    const skinIndex = new Uint16Array(count * 4);
    const skinWeight = new Float32Array(count * 4);
    for (let i = 0; i < count; i++)
        skinner.weigh(
            surface.positions[i * 3],
            surface.positions[i * 3 + 1],
            surface.positions[i * 3 + 2],
            skinIndex,
            skinWeight,
            i * 4,
        );
    return { skinIndex, skinWeight };
}

/** Drops triangles entirely inside something worn over them: no poke-through, fewer polys. */
function cull(s: Surface, cover: Field | null, margin: number): Surface {
    if (!cover) return s;
    const hidden = new Uint8Array(s.positions.length / 3);
    for (let i = 0; i < hidden.length; i++)
        hidden[i] =
            cover(
                s.positions[i * 3],
                s.positions[i * 3 + 1],
                s.positions[i * 3 + 2],
            ) < -margin
                ? 1
                : 0;
    const kept: number[] = [];
    for (let i = 0; i < s.indices.length; i += 3) {
        const [a, b, c] = [s.indices[i], s.indices[i + 1], s.indices[i + 2]];
        if (!(hidden[a] && hidden[b] && hidden[c])) kept.push(a, b, c);
    }
    return { ...s, indices: Uint32Array.from(kept) };
}

type Cloth = { sheer?: number; pattern?: { kind: string } };

const hasHoles = (g: Cloth) => g.pattern?.kind === 'fishnet';
const isSheer = (g: Cloth) => (g.sheer ?? 0) > 0.25 || hasHoles(g);
const seeThrough = (g: Cloth) => ({
    sheer: isSheer(g),
    holes: hasHoles(g),
    opacity: hasHoles(g) ? 1 : 1 - (g.sheer ?? 0) * 0.6,
});

const unite = (fields: Field[]): Field | null =>
    fields.length === 0
        ? null
        : (x, y, z) => {
              let d = fields[0](x, y, z);
              for (let i = 1; i < fields.length; i++)
                  d = Math.min(d, fields[i](x, y, z));
              return d;
          };

function capField(a: Anatomy): Field {
    const s = a.headH;
    const c = a.headC;
    return (x, y, z) => {
        const d = a.skull(x, y, z) - 0.014 * s;
        const py = y - c[1];
        const pz = z - c[2];
        const front = pz * 0.82 - py * 1.1 - 0.3 * s;
        const nape = -py - 0.27 * s;
        const temple = Math.abs(x) * 0.7 - py * 1.2 - 0.22 * s + pz * 0.25;
        return sint(
            sint(sint(d, front, 0.03 * s), nape, 0.03 * s),
            temple,
            0.03 * s,
        );
    };
}

/** Turns a Figure doc into mesh buffers. Pure, so it can run in a worker. */
export function compileFigure(doc: FigureDoc, detail: Detail): Compiled {
    const posed: Posed = poseFor(doc.body);
    const a = anatomy(posed, doc.body, doc.face, detail.fingers, detail.voxel);
    const { H } = a;
    const h = detail.voxel;
    const skinner = bodySkinner(a, posed);
    const wide = 0.52 * H;
    const layers: Layer[] = [];

    const white = (s: Surface) => new Float32Array(s.positions.length).fill(1);
    const push = (
        name: string,
        kind: LayerKind,
        color: Hex,
        s: Surface,
        by: Skinner,
        extra?: Partial<Layer>,
    ) => {
        const colors = extra?.colors ?? white(s);
        // Shade by what's crowding the surface; self-shadowing from the field itself.
        const dark = occlusion(s, shadowField, 0.05 * H);
        for (let i = 0; i < dark.length; i++) {
            const k = 1 - dark[i] * 0.62;
            colors[i * 3] *= k;
            colors[i * 3 + 1] *= k;
            colors[i * 3 + 2] *= k * (0.96 + 0.04 * k);
        }
        layers.push({
            name,
            kind,
            color,
            ...s,
            ...skinned(s, by),
            ...extra,
            colors,
        });
    };

    const shadowField: Field = (x, y, z) =>
        Math.min(a.trunk(x, y, z), a.head(x, y, z));
    const min: V3 = [-wide, -0.12 * H, -0.22 * H];
    const max: V3 = [wide, H * 1.02, 0.24 * H];

    // Outermost first, so each layer can cull against what's over it.
    const shoes = shoeFields(a, doc.shoes);
    const bottom = bottomField(a, doc.bottom);
    // Each layer wraps what's beneath it, so build inside out; polygonise outside in, to cull against what's over it.
    let beneath: Field = bottom;
    const tops = [doc.under, doc.top, doc.outer]
        .filter((t) => !!t)
        .map((top, layer) => {
            const field = topField(a, top, layer, beneath);
            const inner = beneath;
            beneath = (x, y, z) => Math.min(inner(x, y, z), field(x, y, z));
            return { top, field };
        })
        .reverse();
    const over: Field[] = [];

    const shoeSurface = surfaceNets(
        shoes.field,
        [-0.2 * H, -0.14 * H, -0.14 * H],
        [0.2 * H, 0.34 * H, 0.22 * H],
        h * 0.95,
    );
    const shoeColors = new Float32Array(shoeSurface.positions.length);
    {
        const hex = (v: string) => {
            const n = Number.parseInt(v.slice(1), 16);
            return [(n >> 16) & 255, (n >> 8) & 255, n & 255].map((c) => {
                const k = c / 255;
                return k <= 0.04045 ? k / 12.92 : ((k + 0.055) / 1.055) ** 2.4;
            });
        };
        const zones = [
            hex(doc.shoes.color),
            hex(doc.shoes.sole),
            hex(doc.shoes.trim),
        ];
        for (let i = 0; i < shoeSurface.positions.length; i += 3) {
            const c =
                zones[
                    shoes.zone(
                        shoeSurface.positions[i],
                        shoeSurface.positions[i + 1],
                        shoeSurface.positions[i + 2],
                    )
                ];
            shoeColors[i] = c[0];
            shoeColors[i + 1] = c[1];
            shoeColors[i + 2] = c[2];
        }
    }
    smoothColours(shoeSurface.indices, shoeColors, 1);
    push('shoes', 'cloth', '#ffffff', shoeSurface, skinner, {
        colors: shoeColors,
    });
    over.push(shoes.field);

    const topFrame: PanelFrame = { y0: 0.2 * H, y1: 0.8 * H };
    tops.forEach(({ top, field: f }, i) => {
        const s = cull(surfaceNets(f, min, max, h), unite(over), 0.002 * H);
        const paint =
            top === doc.outer ? 'outer' : top === doc.under ? 'under' : 'top';
        push(`top${i}`, 'cloth', '#ffffff', unwrap(a, s, topFrame), skinner, {
            paint,
            frame: topFrame,
            ...seeThrough(top),
        });
        // Skin shows through sheer cloth, so it can't be culled.
        if (!isSheer(top)) over.push(f);
    });

    {
        const s = surfaceNets(
            bottom,
            [-0.3 * H, -0.02 * H, min[2]],
            [0.3 * H, 0.72 * H, max[2]],
            h,
        );
        // Tops hang over the waistband.
        const frame: PanelFrame = { y0: 0.05 * H, y1: 0.67 * H };
        const kept = cull(s, unite(over.slice(1)), 0.002 * H);
        push('bottom', 'cloth', '#ffffff', unwrap(a, kept, frame), skinner, {
            paint: 'bottom',
            frame,
        });
        over.push(bottom);
    }

    if (doc.legwear) {
        const f = legwearField(a, doc.legwear);
        const s = surfaceNets(
            f,
            [-0.2 * H, -0.02 * H, -0.14 * H],
            [0.2 * H, 0.56 * H, 0.14 * H],
            h,
        );
        const frame: PanelFrame = { y0: 0, y1: 0.6 * H };
        push(
            'legwear',
            'cloth',
            '#ffffff',
            unwrap(a, cull(s, unite(over), 0.001 * H), frame),
            skinner,
            { paint: 'legwear', frame, ...seeThrough(doc.legwear) },
        );
        if (!isSheer(doc.legwear)) over.push(f);
    }

    const trunk = surfaceNets(a.trunk, min, max, h);
    push(
        'skin',
        'skin',
        doc.body.skin,
        cull(trunk, unite(over), 0.004 * H),
        skinner,
    );

    for (const side of ['l', 'r'] as const) {
        const f = side === 'l' ? a.handL : a.handR;
        const sign = side === 'l' ? 1 : -1;
        const wrist = posed.world[boneIndex.get(`hand_${side}`) as number];
        const leaf =
            posed.world[boneIndex.get(`middle_04_leaf_${side}`) as number];
        const pad = 0.06 * H;
        const xs = [wrist[0], leaf[0]].sort((p, q) => p - q);
        const ys = [wrist[1], leaf[1]].sort((p, q) => p - q);
        const s = surfaceNets(
            f,
            [xs[0] - pad, ys[0] - pad, wrist[2] - pad],
            [xs[1] + pad, ys[1] + pad, wrist[2] + pad],
            detail.fingers ? h * 0.42 : h * 0.6,
        );
        push(
            `hand-${side}`,
            'skin',
            doc.body.skin,
            s,
            handSkinner(posed, side),
        );
        void sign;
    }

    const s = a.headH;
    const c = a.headC;
    const head = surfaceNets(
        a.head,
        [-0.6 * s, c[1] - 0.62 * s, -0.6 * s + c[2]],
        [0.6 * s, c[1] + 0.6 * s, 0.62 * s + c[2]],
        h * 0.72,
    );
    const uvs = new Float32Array((head.positions.length / 3) * 2);
    for (let i = 0; i < uvs.length / 2; i++) {
        uvs[i * 2] = (head.positions[i * 3] - c[0]) / s + 0.5;
        uvs[i * 2 + 1] = (head.positions[i * 3 + 1] - c[1]) / s + 0.55;
    }
    push('head', 'head', doc.body.skin, head, rigid('Head'), { uvs });

    if (doc.hair.cap > 0 || doc.hair.zones.length > 0) {
        const { colors, ...surface } = growHair(
            a,
            doc.hair,
            capField(a),
            h * 0.8,
            hatLine(doc, a),
            beneath,
        );
        const neckY = a.neck.a[1];
        const chinY = c[1] - 0.5 * s;
        const head = boneIndex.get('Head') as number;
        const chest = boneIndex.get('spine_03') as number;
        push(
            'hair',
            'hair',
            '#ffffff',
            surface,
            {
                // Long hair rides the shoulders, not the skull.
                weigh(_x, y, _z, into, weights, at) {
                    const k = smooth(neckY - 0.03 * H, chinY + 0.04 * H, y);
                    into[at] = head;
                    into[at + 1] = chest;
                    weights[at] = k;
                    weights[at + 1] = 1 - k;
                },
            },
            { colors, shine: 0.7 },
        );
    }

    const grow = 0.012 * H + doc.top.fit * 0.026 * H;
    for (const extra of doc.extras) {
        for (const e of buildExtra(extra, a, posed, h * 0.45, grow)) {
            push(extra.name, 'cloth', '#ffffff', e.surface, rigid(e.bone), {
                colors: e.colors,
                shine: e.metal ? 0.7 : undefined,
            });
        }
    }

    return {
        layers,
        bones: posed.t.map((t, i) => ({
            t,
            q: posed.q[i].toArray() as [number, number, number, number],
        })),
        lift: shoes.lift,
        pelvisScale: posed.pelvisScale,
        height: H + shoes.lift,
        chestY: a.torso.at(0.72)[1],
        bellyY: a.torso.at(0.42)[1],
        H,
    };
}

export { BONES, smin };
