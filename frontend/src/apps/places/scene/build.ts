import * as THREE from 'three';
import { mergeGeometries } from 'three/examples/jsm/utils/BufferGeometryUtils.js';
import { fitScale, meshNow } from './meshes';
import {
    colourOf,
    type MaterialRule,
    MOTION_TAGS,
    type Part,
    type Prefab,
    type PrefabVariant,
    partsOf,
    REGISTRY,
    turn,
    type Vec3,
} from './prefab';

/** Parts merged by how they're lit: plain, always glowing, glowing at night. Tagged parts stay separate for animation. */
export interface Built {
    plain: THREE.BufferGeometry | null;
    glow: THREE.BufferGeometry | null;
    night: THREE.BufferGeometry | null;
    clear: {
        geometry: THREE.BufferGeometry;
        colour: string;
        opacity: number;
    }[];
    tagged: {
        tag: string;
        geometry: THREE.BufferGeometry;
        colour: string;
        /** Its pivot in the world; the geometry is centred on it. */
        at: THREE.Vector3;
        glow: boolean;
    }[];
    signs: {
        w: number;
        h: number;
        text: string;
        bg: string;
        fg: string;
        matrix: THREE.Matrix4;
    }[];
}

/** Flat-shading wants one vertex per face corner; already-flat geometry stays as it is. */
export const unindexed = (g: THREE.BufferGeometry) =>
    g.index ? g.toNonIndexed() : g;

/** Triangular prism, ridge along x. */
function prism(w: number, h: number, d: number): THREE.BufferGeometry {
    const x0 = -w / 2;
    const x1 = w / 2;
    const a = [-d / 2, 0];
    const b = [d / 2, 0];
    const c = [0, h];
    const v = (x: number, p: number[]) => [x, p[1], p[0]];
    const tri = (...pts: number[][]) => pts.flat();
    const quad = (p: number[], q: number[], r: number[], s: number[]) => [
        ...p,
        ...q,
        ...r,
        ...p,
        ...r,
        ...s,
    ];
    const pos = [
        ...tri(v(x0, a), v(x0, c), v(x0, b)),
        ...tri(v(x1, a), v(x1, b), v(x1, c)),
        ...quad(v(x0, b), v(x0, c), v(x1, c), v(x1, b)),
        ...quad(v(x0, a), v(x1, a), v(x1, c), v(x0, c)),
        ...quad(v(x0, a), v(x0, b), v(x1, b), v(x1, a)),
    ];
    const g = new THREE.BufferGeometry();
    g.setAttribute('position', new THREE.Float32BufferAttribute(pos, 3));
    g.computeVertexNormals();
    return g;
}

const cache = new Map<string, THREE.BufferGeometry>();

/** Catalogue material names that mean the same thing in every model. */
const MATERIAL_DEFAULTS: Record<string, MaterialRule> = {
    window: { c: '#f3e2b0', glow: 'night' },
    glass: { glow: 'night' },
    light: { glow: true },
};

/** One leaf part's shape at the origin (bottom-centre convention); where it goes is the caller's matrix. */
function shape(part: Part): THREE.BufferGeometry | null {
    let g: THREE.BufferGeometry;
    if ('box' in part) {
        const [w, h, d] = part.box;
        g = new THREE.BoxGeometry(w, h, d).translate(0, h / 2, 0);
    } else if ('cyl' in part) {
        const [rt, rb, h] =
            part.cyl.length === 3
                ? part.cyl
                : [part.cyl[0], part.cyl[0], part.cyl[1]];
        g = new THREE.CylinderGeometry(rt, rb, h, part.sides ?? 10).translate(
            0,
            h / 2,
            0,
        );
    } else if ('cone' in part) {
        const [r, h] = part.cone;
        g = new THREE.ConeGeometry(r, h, part.sides ?? 10).translate(
            0,
            h / 2,
            0,
        );
    } else if ('ball' in part) {
        g = new THREE.IcosahedronGeometry(part.ball, part.detail ?? 1).scale(
            1,
            part.squash ?? 1,
            1,
        );
    } else if ('dome' in part) {
        g = new THREE.SphereGeometry(
            part.dome,
            10,
            5,
            0,
            Math.PI * 2,
            0,
            Math.PI / 2,
        ).scale(1, part.squash ?? 1, 1);
    } else if ('capsule' in part) {
        const [r, h] = part.capsule;
        const body = Math.max(0.001, h - 2 * r);
        g = new THREE.CapsuleGeometry(r, body, 3, 8).translate(
            0,
            body / 2 + r,
            0,
        );
    } else if ('gable' in part) {
        g = prism(...part.gable);
    } else if ('wedge' in part) {
        g = ramp(...part.wedge);
    } else if ('pyramid' in part) {
        const [w, h, d] = part.pyramid;
        const top = Math.min(0.98, Math.max(0, part.top ?? 0));
        g = new THREE.CylinderGeometry(
            top * Math.SQRT1_2,
            Math.SQRT1_2,
            h,
            4,
            1,
        )
            .rotateY(Math.PI / 4)
            .scale(w, 1, d)
            .translate(0, h / 2, 0);
    } else if ('ring' in part) {
        g = new THREE.TorusGeometry(
            part.ring[0],
            part.ring[1],
            6,
            14,
            part.arc ?? Math.PI * 2,
        );
    } else if ('lathe' in part) {
        if (part.lathe.length < 2) return null;
        g = new THREE.LatheGeometry(
            part.lathe.map(([r, y]) => new THREE.Vector2(Math.max(0, r), y)),
            part.sides ?? 10,
        );
    } else if ('extrude' in part) {
        if (part.extrude.length < 3) return null;
        const outline = new THREE.Shape(
            part.extrude.map(([x, y]) => new THREE.Vector2(x, y)),
        );
        const depth = part.depth ?? 0.05;
        g = new THREE.ExtrudeGeometry(outline, {
            depth,
            bevelEnabled: false,
            curveSegments: 1,
        }).translate(0, 0, -depth / 2);
    } else if ('tube' in part) {
        if (part.tube.length < 2) return null;
        const pts = part.tube.map(([x, y, z]) => new THREE.Vector3(x, y, z));
        const curve =
            pts.length === 2
                ? new THREE.LineCurve3(pts[0], pts[1])
                : new THREE.CatmullRomCurve3(
                      pts,
                      !!part.closed,
                      'catmullrom',
                      0.4,
                  );
        g = new THREE.TubeGeometry(
            curve,
            pts.length === 2 ? 1 : pts.length * 4,
            part.r ?? 0.02,
            part.sides ?? 5,
            !!part.closed,
        );
    } else return null;
    g = unindexed(g);
    g.computeVertexNormals();
    return g;
}

/** Ramp: full height at -z, down to the floor at +z. */
function ramp(w: number, h: number, d: number): THREE.BufferGeometry {
    const [x0, x1, z0, z1] = [-w / 2, w / 2, -d / 2, d / 2];
    const tri = (...p: number[][]) => p.flat();
    const quad = (p: number[], q: number[], r: number[], s2: number[]) => [
        ...p,
        ...q,
        ...r,
        ...p,
        ...r,
        ...s2,
    ];
    const pos = [
        ...quad([x0, 0, z0], [x0, h, z0], [x1, h, z0], [x1, 0, z0]),
        ...quad([x0, h, z0], [x0, 0, z1], [x1, 0, z1], [x1, h, z0]),
        ...quad([x0, 0, z0], [x1, 0, z0], [x1, 0, z1], [x0, 0, z1]),
        ...tri([x0, 0, z0], [x0, 0, z1], [x0, h, z0]),
        ...tri([x1, 0, z0], [x1, h, z0], [x1, 0, z1]),
    ];
    const g = new THREE.BufferGeometry();
    g.setAttribute('position', new THREE.Float32BufferAttribute(pos, 3));
    g.computeVertexNormals();
    return g;
}

const X = new THREE.Vector3(1, 0, 0);
const Y = new THREE.Vector3(0, 1, 0);
const Z = new THREE.Vector3(0, 0, 1);

/** A part's own placement: stretched, turned (x, then z, then y), then moved. */
function own(part: Part): THREE.Matrix4 {
    const [x, y, z] = part.at ?? [0, 0, 0];
    const q = new THREE.Quaternion();
    if (part.ry)
        q.multiply(new THREE.Quaternion().setFromAxisAngle(Y, part.ry));
    if (part.rz)
        q.multiply(new THREE.Quaternion().setFromAxisAngle(Z, part.rz));
    if (part.rx)
        q.multiply(new THREE.Quaternion().setFromAxisAngle(X, part.rx));
    const [sx, sy, sz] = part.s ?? [1, 1, 1];
    return new THREE.Matrix4().compose(
        new THREE.Vector3(x, y, z),
        q,
        new THREE.Vector3(sx, sy, sz),
    );
}

/** Every copy a part asks for (array, spin, mirror), as matrices in its parent's space. */
function copies(part: Part): THREE.Matrix4[] {
    let ms = [own(part)];
    if (part.array && part.array.n > 1) {
        const [dx, dy, dz] = part.array.step ?? [0, 0, 0];
        const n = Math.min(24, Math.floor(part.array.n));
        ms = ms.flatMap((m) =>
            Array.from({ length: n }, (_, i) =>
                new THREE.Matrix4()
                    .makeTranslation(dx * i, dy * i, dz * i)
                    .multiply(m),
            ),
        );
    }
    if (part.spin && part.spin > 1) {
        const n = Math.min(24, Math.floor(part.spin));
        ms = ms.flatMap((m) =>
            Array.from({ length: n }, (_, i) =>
                new THREE.Matrix4()
                    .makeRotationY((i / n) * Math.PI * 2)
                    .multiply(m),
            ),
        );
    }
    if (part.mirror) {
        const flips: Vec3[] =
            part.mirror === 'xz'
                ? [
                      [-1, 1, 1],
                      [1, 1, -1],
                      [-1, 1, -1],
                  ]
                : part.mirror === 'x'
                  ? [[-1, 1, 1]]
                  : [[1, 1, -1]];
        ms = ms.flatMap((m) => [
            m,
            ...flips.map((f) =>
                new THREE.Matrix4().makeScale(...f).multiply(m),
            ),
        ]);
    }
    return ms;
}

export const MAX_LEAVES = 400;

interface Leaf {
    part: Part;
    matrix: THREE.Matrix4;
    colour: string;
}

/** Groups, `use`d prefabs and repeats flattened to leaves, each with its matrix and colour. */
function leavesOf(
    parts: Part[],
    prefab: Prefab,
    variant: PrefabVariant,
    parent: THREE.Matrix4,
    out: Leaf[],
    depth = 0,
) {
    for (const part of parts) {
        if (out.length >= MAX_LEAVES) return;
        for (const m of copies(part)) {
            const matrix = parent.clone().multiply(m);
            if ('group' in part) {
                if (depth < 4)
                    leavesOf(
                        part.group,
                        prefab,
                        variant,
                        matrix,
                        out,
                        depth + 1,
                    );
            } else if ('use' in part) {
                const used = REGISTRY[part.use];
                if (!used || depth >= 3) continue;
                const v = { ...variant, tint: part.tint ?? {} };
                leavesOf(partsOf(used, v), used, v, matrix, out, depth + 1);
            } else
                out.push({
                    part,
                    matrix,
                    colour: colourOf(part.c, prefab, variant),
                });
        }
    }
}

/** A mirrored copy turns its triangles inside out; turn them back. */
function unflip(g: THREE.BufferGeometry) {
    for (const name of ['position', 'normal', 'uv', 'color']) {
        const a = g.getAttribute(name) as THREE.BufferAttribute | undefined;
        if (!a) continue;
        const n = a.itemSize;
        const arr = a.array as Float32Array;
        for (let i = 0; i + 3 * n <= arr.length; i += 3 * n)
            for (let k = 0; k < n; k++) {
                const t = arr[i + n + k];
                arr[i + n + k] = arr[i + 2 * n + k];
                arr[i + 2 * n + k] = t;
            }
    }
    return g;
}

/** Tags that move or light up a part, so it stays its own mesh. */
const LIVE = new Set<string>(MOTION_TAGS);

function paint(g: THREE.BufferGeometry, hex: string) {
    const c = new THREE.Color(hex);
    const n = g.getAttribute('position').count;
    const cols = new Float32Array(n * 3);
    for (let i = 0; i < n; i++) cols.set([c.r, c.g, c.b], i * 3);
    g.setAttribute('color', new THREE.BufferAttribute(cols, 3));
    if (!g.getAttribute('uv'))
        g.setAttribute(
            'uv',
            new THREE.BufferAttribute(new Float32Array(n * 2), 2),
        );
    return g;
}

const strip = (g: THREE.BufferGeometry) => {
    for (const name of Object.keys(g.attributes))
        if (!['position', 'normal', 'color', 'uv'].includes(name))
            g.deleteAttribute(name);
    return g;
};

const merge = (gs: THREE.BufferGeometry[]) =>
    gs.length ? mergeGeometries(gs.map(strip)) : null;

export interface Placement {
    prefab: Prefab;
    variant: PrefabVariant;
    /** Footprint centre in world units. */
    x: number;
    z: number;
    y: number;
    rot: number;
}

/** Bake placements into a handful of merged meshes. */
export function build(placements: Placement[]): Built {
    const plain: THREE.BufferGeometry[] = [];
    const glow: THREE.BufferGeometry[] = [];
    const night: THREE.BufferGeometry[] = [];
    const out: Built = {
        plain: null,
        glow: null,
        night: null,
        clear: [],
        tagged: [],
        signs: [],
    };
    for (const pl of placements) {
        let shift = 0;
        if (pl.prefab.wall) shift = -0.44;
        const [sx, sz] = turn(0, shift, pl.rot);
        const m = new THREE.Matrix4().compose(
            new THREE.Vector3(pl.x + sx, pl.y, pl.z + sz),
            new THREE.Quaternion().setFromAxisAngle(
                new THREE.Vector3(0, 1, 0),
                (pl.rot * Math.PI) / 2,
            ),
            new THREE.Vector3(1, 1, 1),
        );
        const found: Leaf[] = [];
        leavesOf(
            partsOf(pl.prefab, pl.variant),
            pl.prefab,
            pl.variant,
            m,
            found,
        );
        for (const { part, matrix, colour } of found) {
            if ('sign' in part) {
                out.signs.push({
                    w: part.sign[0],
                    h: part.sign[1],
                    text: part.text,
                    bg: colour,
                    fg: part.fg ?? '#fff',
                    matrix: matrix
                        .clone()
                        .multiply(
                            new THREE.Matrix4().makeTranslation(
                                0,
                                part.sign[1] / 2,
                                0,
                            ),
                        ),
                });
                continue;
            }
            if ('glb' in part) {
                const mesh = meshNow(part.glb);
                const s = mesh ? fitScale(mesh.size, part.fit) : 1;
                const local = matrix
                    .clone()
                    .multiply(new THREE.Matrix4().makeScale(s, s, s));
                if (!mesh) {
                    // Stand-in until the model lands (or for good, if it never does).
                    const [w, h, d] = part.fit;
                    const box = unindexed(
                        new THREE.BoxGeometry(
                            w * 0.8,
                            h || 0.6,
                            d * 0.8,
                        ).translate(0, (h || 0.6) / 2, 0),
                    );
                    plain.push(paint(box.applyMatrix4(local), colour));
                    continue;
                }
                for (const g of mesh.groups) {
                    const rule =
                        part.mats?.[g.material] ??
                        MATERIAL_DEFAULTS[g.material];
                    const hex = rule?.c
                        ? colourOf(rule.c, pl.prefab, pl.variant)
                        : g.colour;
                    const placed = paint(
                        g.geometry.clone().applyMatrix4(local),
                        hex,
                    );
                    const lit = rule?.glow ?? (g.emissive ? 'night' : false);
                    (lit === 'night' ? night : lit ? glow : plain).push(placed);
                }
                continue;
            }
            const k = JSON.stringify({
                ...part,
                c: 0,
                at: 0,
                rx: 0,
                ry: 0,
                rz: 0,
                s: 0,
                array: 0,
                spin: 0,
                mirror: 0,
                tag: 0,
                glow: 0,
                opacity: 0,
                flat: 0,
            });
            let g = cache.get(k);
            if (!g) {
                const made = shape(part);
                if (!made) continue;
                g = made;
                cache.set(k, g);
            }
            const moved = g.clone().applyMatrix4(matrix);
            if (matrix.determinant() < 0) unflip(moved);
            const placed = paint(moved, colour);
            if (part.tag && LIVE.has(part.tag)) {
                const box = new THREE.Box3().setFromBufferAttribute(
                    placed.getAttribute('position') as THREE.BufferAttribute,
                );
                const at = box.getCenter(new THREE.Vector3());
                // Sway and flames hinge at the foot; the rest turn about their middle.
                if (part.tag === 'sway' || part.tag === 'flame')
                    at.y = box.min.y;
                placed.translate(-at.x, -at.y, -at.z);
                out.tagged.push({
                    tag: part.tag,
                    geometry: placed,
                    colour,
                    at,
                    glow: !!part.glow,
                });
            } else if (part.opacity !== undefined && part.opacity < 1)
                out.clear.push({
                    geometry: placed,
                    colour,
                    opacity: part.opacity,
                });
            else if (part.glow === 'night') night.push(placed);
            else if (part.glow) glow.push(placed);
            else plain.push(placed);
        }
    }
    out.plain = merge(plain);
    out.glow = merge(glow);
    out.night = merge(night);
    return out;
}

/** A box with its top painted differently from its sides (ground tiles, walls). */
export function slab(
    w: number,
    h: number,
    d: number,
    x: number,
    y: number,
    z: number,
    top: string,
    side: string,
) {
    const g = unindexed(
        new THREE.BoxGeometry(w, h, d).translate(x, y + h / 2, z),
    );
    const t = new THREE.Color(top);
    const s = new THREE.Color(side);
    const n = g.getAttribute('position').count;
    const cols = new Float32Array(n * 3);
    // BoxGeometry face order: +x, -x, +y, -y, +z, -z; six vertices per face once non-indexed.
    for (let i = 0; i < n; i++) {
        const face = Math.floor(i / 6);
        const c =
            face === 2
                ? t
                : face === 0 || face === 4
                  ? s
                  : s.clone().multiplyScalar(0.82);
        cols.set([c.r, c.g, c.b], i * 3);
    }
    g.setAttribute('color', new THREE.BufferAttribute(cols, 3));
    return g;
}

export const mergeAll = merge;
