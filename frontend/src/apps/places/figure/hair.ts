import type { Anatomy } from './anatomy';
import type { HairDoc, HairZone, HairZoneName } from './doc';
import { netsFromGrid, type Surface } from './nets';
import {
    capsule,
    clamp,
    type Field,
    mix,
    norm,
    smin,
    smooth,
    type V3,
} from './sdf';

/** [lonMin, lonMax, latMin, latMax] of each named patch of scalp, degrees. */
const ZONES: Record<HairZoneName, [number, number, number, number]> = {
    crown: [-180, 180, 62, 90],
    top: [-110, 110, 40, 75],
    fringe: [-62, 62, 34, 62],
    sideL: [55, 125, 0, 45],
    sideR: [-125, -55, 0, 45],
    back: [115, 245, -8, 50],
    nape: [130, 230, -32, 4],
};

const rng = (start: number) => {
    let seed = start;
    return () => {
        seed += 0x6d2b79f5;
        let t = seed;
        t = Math.imul(t ^ (t >>> 15), t | 1);
        t ^= t + Math.imul(t ^ (t >>> 7), t | 61);
        return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
    };
};

export interface HairMesh extends Surface {
    colors: Float32Array;
}

const hexRgb = (hex: string): V3 => {
    const n = Number.parseInt(hex.slice(1), 16);
    const srgb = [(n >> 16) & 255, (n >> 8) & 255, n & 255].map((c) => c / 255);
    return srgb.map((c) =>
        c <= 0.04045 ? c / 12.92 : ((c + 0.055) / 1.055) ** 2.4,
    ) as V3;
};

const STEPS = 10;

/** Blurs vertex colours over the mesh, so lock boundaries read as soft instead of speckled. */
export function smoothColours(
    indices: Uint32Array,
    colors: Float32Array,
    rounds: number,
) {
    const n = colors.length / 3;
    for (let r = 0; r < rounds; r++) {
        const sum = new Float32Array(colors.length);
        const count = new Uint16Array(n);
        for (let t = 0; t < indices.length; t += 3)
            for (let k = 0; k < 3; k++) {
                const a = indices[t + k];
                const b = indices[t + ((k + 1) % 3)];
                for (let c = 0; c < 3; c++) {
                    sum[a * 3 + c] += colors[b * 3 + c];
                    sum[b * 3 + c] += colors[a * 3 + c];
                }
                count[a]++;
                count[b]++;
            }
        for (let i = 0; i < n; i++)
            if (count[i])
                for (let c = 0; c < 3; c++)
                    colors[i * 3 + c] =
                        colors[i * 3 + c] * 0.4 +
                        (sum[i * 3 + c] / count[i]) * 0.6;
    }
}

interface Lock {
    path: V3[];
    /** Radius along the path. */
    radius: number[];
    colour: (f: number) => V3;
}

/** Grows locks from the scalp, then sculpts them into one chunky solid: gravity, sweep and curl, pushed clear of the skull. */
export function growHair(
    a: Anatomy,
    doc: HairDoc,
    cap: Field,
    h: number,
    cutAbove: number,
    worn: Field,
): HairMesh {
    const s = a.headH;
    const c = a.headC;
    const clear = 0.01 * s;
    const collide = (p: V3, r: number) => {
        const e = 0.01 * s;
        const d = a.skull(p[0], p[1], p[2]);
        if (d < clear + r) {
            const n = norm([
                a.skull(p[0] + e, p[1], p[2]) - a.skull(p[0] - e, p[1], p[2]),
                a.skull(p[0], p[1] + e, p[2]) - a.skull(p[0], p[1] - e, p[2]),
                a.skull(p[0], p[1], p[2] + e) - a.skull(p[0], p[1], p[2] - e),
            ]);
            const push = clear + r - d;
            p[0] += n[0] * push;
            p[1] += n[1] * push;
            p[2] += n[2] * push;
        }
        // Lie on the clothes, not through them.
        const body = Math.min(
            a.trunk(p[0], p[1], p[2]),
            worn(p[0], p[1], p[2]),
        );
        if (body < r) {
            const out = norm([p[0], 0.3, p[2]]);
            const push = r - body;
            p[0] += out[0] * push;
            p[1] += out[1] * push;
            p[2] += out[2] * push;
        }
    };

    const grow = (
        zone: HairZone,
        seed: number,
        lon: number,
        lat: number,
    ): Lock => {
        const r = rng(seed);
        const la = (lat * Math.PI) / 180;
        const lo = (lon * Math.PI) / 180;
        const out: V3 = norm([
            Math.sin(lo) * Math.cos(la),
            Math.sin(la),
            Math.cos(lo) * Math.cos(la),
        ]);
        // Walk out from inside the skull to find the scalp along this ray.
        const root: V3 = [c[0], c[1] + 0.07 * s, c[2] - 0.02 * s];
        for (let i = 0; i < 48; i++) {
            const d = a.skull(root[0], root[1], root[2]);
            if (d > -0.006 * s) break;
            const step = Math.max(-d, 0.004 * s);
            root[0] += out[0] * step;
            root[1] += out[1] * step;
            root[2] += out[2] * step;
        }
        const len = zone.len * s * (1 - zone.jag * 0.5 * r());
        const fat = zone.width * s * 0.5 * (0.85 + r() * 0.3);
        const step = len / STEPS;
        const wob = (r() - 0.5) * 0.5;
        // Down the scalp, as a comb would take it.
        const slide = norm([
            -out[0] * out[1],
            1 - out[1] * out[1] + 0.001,
            -out[2] * out[1],
        ]);
        const along: V3 = [-slide[0], -slide[1], -slide[2]];
        let dir: V3 = norm([
            out[0] * (0.2 + zone.lift) + along[0] * (1 - zone.lift),
            out[1] * (0.2 + zone.lift) + along[1] * (1 - zone.lift),
            out[2] * (0.2 + zone.lift) + along[2] * (1 - zone.lift),
        ]);
        const path: V3[] = [[...root]];
        const radius: number[] = [fat * 0.8];
        const p: V3 = [...root];
        const twist = r() * 2 * Math.PI;
        for (let i = 1; i <= STEPS; i++) {
            const f = i / STEPS;
            dir[1] -= zone.fall * 0.3 * (0.4 + f);
            dir[0] += zone.sweep * 0.32 * (1 - f * 0.6) + wob * 0.1;
            if (zone.curl > 0) {
                const a2 = twist + f * zone.curl * 11;
                dir[0] += Math.cos(a2) * zone.curl * 0.55;
                dir[2] += Math.sin(a2) * zone.curl * 0.55;
            }
            dir = norm(dir);
            const prev: V3 = [...p];
            p[0] += dir[0] * step;
            p[1] += dir[1] * step;
            p[2] += dir[2] * step;
            const rad = fat * (1 - f ** 1.7) + 0.005 * s;
            collide(p, rad * 0.75);
            dir = norm([p[0] - prev[0], p[1] - prev[1], p[2] - prev[2]]);
            path.push([...p]);
            radius.push(rad);
        }
        const main = hexRgb(zone.color ?? doc.color);
        const tips = doc.tips ? hexRgb(doc.tips) : null;
        const bands = zone.bands?.map(hexRgb);
        const shade = 0.9 + r() * 0.2;
        const colour = (f: number): V3 => {
            if (bands) {
                const b =
                    bands[
                        clamp(Math.floor(f * bands.length), 0, bands.length - 1)
                    ];
                return [b[0] * shade, b[1] * shade, b[2] * shade];
            }
            const k = tips ? smooth(0.5, 0.78, f) : 0;
            const t = tips ?? main;
            return [
                mix(main[0], t[0], k) * shade,
                mix(main[1], t[1], k) * shade,
                mix(main[2], t[2], k) * shade,
            ];
        };
        return { path, radius, colour };
    };

    const locks: Lock[] = [];
    doc.zones.forEach((zone, zi) => {
        const box = typeof zone.at === 'string' ? ZONES[zone.at] : zone.at;
        const r = rng(zi * 977 + 13);
        const n = Math.round(clamp(zone.count, 1, 60));
        for (let i = 0; i < n; i++) {
            // Even-ish scatter across the patch, front to back.
            const lon = mix(box[0], box[1], (i + 0.15 + r() * 0.7) / n);
            const lat = mix(box[2], box[3], r());
            locks.push(grow(zone, zi * 131 + i * 17 + 3, lon, lat));
        }
    });

    // The sculpting grid: tight round the hair, not the whole Figure.
    const min: V3 = [c[0] - 0.56 * s, c[1] - 0.46 * s, c[2] - 0.6 * s];
    const max: V3 = [c[0] + 0.56 * s, c[1] + 0.68 * s, c[2] + 0.62 * s];
    for (const l of locks)
        l.path.forEach((p, i) => {
            for (let k = 0; k < 3; k++) {
                min[k] = Math.min(min[k], p[k] - l.radius[i] - 3 * h);
                max[k] = Math.max(max[k], p[k] + l.radius[i] + 3 * h);
            }
        });
    const nx = Math.ceil((max[0] - min[0]) / h) + 1;
    const ny = Math.ceil((max[1] - min[1]) / h) + 1;
    const nz = Math.ceil((max[2] - min[2]) / h) + 1;
    const sy = nx;
    const sz = nx * ny;
    const FAR = 4 * h;
    const grid = new Float32Array(nx * ny * nz).fill(FAR);
    const owner = new Int16Array(nx * ny * nz).fill(-1);
    const along = new Uint8Array(nx * ny * nz);
    const groove = 0.09 * s;

    locks.forEach((lock, li) => {
        for (let i = 0; i + 1 < lock.path.length; i++) {
            const p0 = lock.path[i];
            const p1 = lock.path[i + 1];
            const r0 = lock.radius[i];
            const r1 = lock.radius[i + 1];
            const seg = capsule(p0, p1, r0, r1);
            const reach = Math.max(r0, r1) + 3 * h;
            const lo = [0, 1, 2].map((k) =>
                Math.max(
                    0,
                    Math.floor((Math.min(p0[k], p1[k]) - reach - min[k]) / h),
                ),
            );
            const hi = [0, 1, 2].map((k) =>
                Math.min(
                    [nx, ny, nz][k] - 1,
                    Math.ceil((Math.max(p0[k], p1[k]) + reach - min[k]) / h),
                ),
            );
            const f = (i + 0.5) / (lock.path.length - 1);
            for (let z = lo[2]; z <= hi[2]; z++)
                for (let y = lo[1]; y <= hi[1]; y++)
                    for (let x = lo[0]; x <= hi[0]; x++) {
                        const d = seg(
                            min[0] + x * h,
                            min[1] + y * h,
                            min[2] + z * h,
                        );
                        const at = x + y * sy + z * sz;
                        if (d < grid[at]) {
                            owner[at] = li;
                            along[at] = f * 255;
                        }
                        grid[at] = smin(grid[at], d, groove);
                    }
        }
    });
    if (doc.cap > 0)
        for (let z = 0; z < nz; z++)
            for (let y = 0; y < ny; y++)
                for (let x = 0; x < nx; x++) {
                    const px = min[0] + x * h;
                    const py = min[1] + y * h;
                    const pz = min[2] + z * h;
                    if (
                        Math.abs(px - c[0]) > 0.6 * s ||
                        Math.abs(py - c[1]) > 0.7 * s ||
                        Math.abs(pz - c[2]) > 0.65 * s
                    )
                        continue;
                    const at = x + y * sy + z * sz;
                    grid[at] = smin(grid[at], cap(px, py, pz), 0.03 * s);
                }

    const sample: Field = (x, y, z) => {
        const gx = clamp((x - min[0]) / h, 0, nx - 1.001);
        const gy = clamp((y - min[1]) / h, 0, ny - 1.001);
        const gz = clamp((z - min[2]) / h, 0, nz - 1.001);
        const ix = gx | 0;
        const iy = gy | 0;
        const iz = gz | 0;
        const fx = gx - ix;
        const fy = gy - iy;
        const fz = gz - iz;
        const at = ix + iy * sy + iz * sz;
        const a0 = mix(grid[at], grid[at + 1], fx);
        const a1 = mix(grid[at + sy], grid[at + sy + 1], fx);
        const b0 = mix(grid[at + sz], grid[at + sz + 1], fx);
        const b1 = mix(grid[at + sz + sy], grid[at + sz + sy + 1], fx);
        return mix(mix(a0, a1, fy), mix(b0, b1, fy), fz);
    };
    if (Number.isFinite(cutAbove))
        for (let z = 0; z < nz; z++)
            for (let y = 0; y < ny; y++) {
                const over = min[1] + y * h - cutAbove;
                for (let x = 0; x < nx; x++) {
                    const at = x + y * sy + z * sz;
                    if (over > grid[at]) grid[at] = over;
                }
            }
    const surface = netsFromGrid(grid, [nx, ny, nz], min, h, sample);

    const base = hexRgb(doc.color);
    const colors = new Float32Array(surface.positions.length);
    for (let i = 0; i < colors.length; i += 3) {
        const gx = Math.round((surface.positions[i] - min[0]) / h);
        const gy = Math.round((surface.positions[i + 1] - min[1]) / h);
        const gz = Math.round((surface.positions[i + 2] - min[2]) / h);
        const at =
            clamp(gx, 0, nx - 1) +
            clamp(gy, 0, ny - 1) * sy +
            clamp(gz, 0, nz - 1) * sz;
        const rgb =
            owner[at] >= 0 ? locks[owner[at]].colour(along[at] / 255) : base;
        colors[i] = rgb[0];
        colors[i + 1] = rgb[1];
        colors[i + 2] = rgb[2];
    }
    smoothColours(surface.indices, colors, 3);
    return { ...surface, colors };
}
