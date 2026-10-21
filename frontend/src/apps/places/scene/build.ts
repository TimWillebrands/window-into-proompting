import * as THREE from 'three';
import { mergeGeometries } from 'three/examples/jsm/utils/BufferGeometryUtils.js';
import {
    colourOf,
    type Part,
    type Prefab,
    type PrefabVariant,
    partsOf,
    turn,
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
        at: THREE.Vector3;
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

/** One part as geometry in prefab-local space (bottom-centre convention). */
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
    } else if ('gable' in part) {
        g = prism(...part.gable);
    } else if ('ring' in part) {
        g = new THREE.TorusGeometry(
            part.ring[0],
            part.ring[1],
            6,
            14,
            part.arc ?? Math.PI * 2,
        );
    } else return null;
    if (part.rx) g.rotateX(part.rx);
    if (part.rz) g.rotateZ(part.rz);
    if (part.ry) g.rotateY(part.ry);
    const [x, y, z] = part.at ?? [0, 0, 0];
    g.translate(x, y, z);
    return g.index ? g.toNonIndexed() : g;
}

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
        for (const part of partsOf(pl.prefab, pl.variant)) {
            const colour = colourOf(part.c, pl.prefab, pl.variant);
            if ('sign' in part) {
                const [x, y, z] = part.at ?? [0, 0, 0];
                const local = new THREE.Matrix4().makeTranslation(
                    x,
                    y + part.sign[1] / 2,
                    z,
                );
                if (part.ry)
                    local.multiply(new THREE.Matrix4().makeRotationY(part.ry));
                out.signs.push({
                    w: part.sign[0],
                    h: part.sign[1],
                    text: part.text,
                    bg: colour,
                    fg: part.fg ?? '#fff',
                    matrix: m.clone().multiply(local),
                });
                continue;
            }
            const k = JSON.stringify({ ...part, c: 0 });
            let g = cache.get(k);
            if (!g) {
                const made = shape(part);
                if (!made) continue;
                g = made;
                cache.set(k, g);
            }
            const placed = paint(g.clone().applyMatrix4(m), colour);
            if (part.tag && part.tag !== 'leaf' && part.tag !== 'window') {
                const box = new THREE.Box3().setFromBufferAttribute(
                    placed.getAttribute('position') as THREE.BufferAttribute,
                );
                out.tagged.push({
                    tag: part.tag,
                    geometry: placed,
                    colour,
                    at: box.getCenter(new THREE.Vector3()),
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
    const g = new THREE.BoxGeometry(w, h, d)
        .translate(x, y + h / 2, z)
        .toNonIndexed();
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
