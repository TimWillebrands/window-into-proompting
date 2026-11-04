/** Signed distance fields: the Figure's modelling language, because blended blobs forgive coarse numbers. */
export type V3 = [number, number, number];
export type Field = (x: number, y: number, z: number) => number;

export const clamp = (v: number, lo: number, hi: number) =>
    v < lo ? lo : v > hi ? hi : v;
export const mix = (a: number, b: number, t: number) => a + (b - a) * t;
export const smooth = (lo: number, hi: number, v: number) => {
    const t = clamp((v - lo) / (hi - lo), 0, 1);
    return t * t * (3 - 2 * t);
};

/** Smooth union; k is the width of the blend. */
export const smin = (a: number, b: number, k: number) => {
    const h = Math.max(k - Math.abs(a - b), 0) / k;
    return Math.min(a, b) - h * h * k * 0.25;
};
/** Smooth subtraction of b from a. */
export const ssub = (a: number, b: number, k: number) => -smin(-a, b, k);
/** Smooth intersection. */
export const sint = (a: number, b: number, k: number) => -smin(-a, -b, k);

export const sphere =
    (c: V3, r: number): Field =>
    (x, y, z) =>
        Math.hypot(x - c[0], y - c[1], z - c[2]) - r;

export const ellipsoid = (c: V3, r: V3): Field => {
    const [rx, ry, rz] = r;
    return (x, y, z) => {
        const px = x - c[0];
        const py = y - c[1];
        const pz = z - c[2];
        const k0 = Math.hypot(px / rx, py / ry, pz / rz);
        const k1 = Math.hypot(px / (rx * rx), py / (ry * ry), pz / (rz * rz));
        return k1 < 1e-9 ? -Math.min(rx, ry, rz) : (k0 * (k0 - 1)) / k1;
    };
};

/** A cone with round ends between two points. */
export const capsule = (a: V3, b: V3, ra: number, rb = ra): Field => {
    const bx = b[0] - a[0];
    const by = b[1] - a[1];
    const bz = b[2] - a[2];
    const len2 = bx * bx + by * by + bz * bz || 1e-9;
    return (x, y, z) => {
        const px = x - a[0];
        const py = y - a[1];
        const pz = z - a[2];
        const t = clamp((px * bx + py * by + pz * bz) / len2, 0, 1);
        return (
            Math.hypot(px - bx * t, py - by * t, pz - bz * t) - mix(ra, rb, t)
        );
    };
};

const SAMPLES = 48;

export interface LoftRow {
    /** 0..1 along the axis. */
    t: number;
    /** Radii across (u) and through (v). */
    u: number;
    v: number;
    /** Centre offsets, for a spine that curves. */
    ou?: number;
    ov?: number;
}

const sub = (a: V3, b: V3): V3 => [a[0] - b[0], a[1] - b[1], a[2] - b[2]];
export const dot = (a: V3, b: V3) => a[0] * b[0] + a[1] * b[1] + a[2] * b[2];
export const cross = (a: V3, b: V3): V3 => [
    a[1] * b[2] - a[2] * b[1],
    a[2] * b[0] - a[0] * b[2],
    a[0] * b[1] - a[1] * b[0],
];
export const norm = (a: V3): V3 => {
    const l = Math.hypot(a[0], a[1], a[2]) || 1;
    return [a[0] / l, a[1] / l, a[2] / l];
};

/** Catmull-Rom through rows of one key, sampled evenly. */
function sampleRows(rows: LoftRow[], key: keyof LoftRow, out: Float32Array) {
    const value = (i: number) =>
        (rows[clamp(i, 0, rows.length - 1)][key] as number | undefined) ?? 0;
    for (let s = 0; s < SAMPLES; s++) {
        const t = s / (SAMPLES - 1);
        let i = 0;
        while (i < rows.length - 2 && rows[i + 1].t < t) i++;
        const span = rows[i + 1].t - rows[i].t || 1;
        const f = clamp((t - rows[i].t) / span, 0, 1);
        const [p0, p1, p2, p3] = [
            value(i - 1),
            value(i),
            value(i + 1),
            value(i + 2),
        ];
        out[s] =
            0.5 *
            (2 * p1 +
                (p2 - p0) * f +
                (2 * p0 - 5 * p1 + 4 * p2 - p3) * f * f +
                (3 * p1 - p0 - 3 * p2 + p3) * f * f * f);
    }
}

/** An elliptical tube along a straight axis with a radius table: anatomy as numbers. */
export class Loft {
    readonly a: V3;
    readonly u: V3;
    readonly v: V3;
    readonly w: V3;
    readonly length: number;
    /** Unclamped position along the axis from the last call to d. */
    t = 0;
    /** Angle round the axis from the last call to d. */
    angle = 0;
    private ru = new Float32Array(SAMPLES);
    private rv = new Float32Array(SAMPLES);
    private ou = new Float32Array(SAMPLES);
    private ov = new Float32Array(SAMPLES);

    constructor(a: V3, b: V3, rows: LoftRow[], across: V3 = [1, 0, 0]) {
        this.a = a;
        const axis = sub(b, a);
        this.length = Math.hypot(axis[0], axis[1], axis[2]);
        this.w = norm(axis);
        this.v = norm(cross(across, this.w));
        this.u = cross(this.w, this.v);
        sampleRows(rows, 'u', this.ru);
        sampleRows(rows, 'v', this.rv);
        sampleRows(rows, 'ou', this.ou);
        sampleRows(rows, 'ov', this.ov);
    }

    /** Point at t on the axis, centre offsets included. */
    at(t: number): V3 {
        const i = Math.round(clamp(t, 0, 1) * (SAMPLES - 1));
        const { a, u, v, w, length } = this;
        return [0, 1, 2].map(
            (c) =>
                a[c] +
                w[c] * length * t +
                u[c] * this.ou[i] +
                v[c] * this.ov[i],
        ) as V3;
    }

    /** Radii at t, for hanging things off the surface. */
    radii(t: number): [number, number] {
        const i = Math.round(clamp(t, 0, 1) * (SAMPLES - 1));
        return [this.ru[i], this.rv[i]];
    }

    d(x: number, y: number, z: number, grow = 0): number {
        const { a, u, v, w, length } = this;
        const px = x - a[0];
        const py = y - a[1];
        const pz = z - a[2];
        const along = px * w[0] + py * w[1] + pz * w[2];
        const t = along / length;
        this.t = t;
        const s = clamp(t, 0, 1) * (SAMPLES - 1);
        const i = Math.min(SAMPLES - 2, s | 0);
        const f = s - i;
        const ra = this.ru[i] + (this.ru[i + 1] - this.ru[i]) * f + grow;
        const rb = this.rv[i] + (this.rv[i + 1] - this.rv[i]) * f + grow;
        const pu =
            px * u[0] +
            py * u[1] +
            pz * u[2] -
            (this.ou[i] + (this.ou[i + 1] - this.ou[i]) * f);
        const pv =
            px * v[0] +
            py * v[1] +
            pz * v[2] -
            (this.ov[i] + (this.ov[i + 1] - this.ov[i]) * f);
        this.angle = Math.atan2(pu, pv);
        const rm = ra < rb ? ra : rb;
        const pw = along - clamp(t, 0, 1) * length;
        const q = Math.sqrt(
            (pu * pu) / (ra * ra) +
                (pv * pv) / (rb * rb) +
                (pw * pw) / (rm * rm),
        );
        return (q - 1) * rm;
    }
}

const hash = (x: number, y: number, z: number) => {
    let h = (x * 374761393 + y * 668265263 + z * 2147483647) | 0;
    h = Math.imul(h ^ (h >>> 13), 1274126177);
    return ((h ^ (h >>> 16)) >>> 0) / 4294967295;
};

/** Smooth value noise in -1..1, for folds and tousle. */
export function noise(x: number, y: number, z: number): number {
    const ix = Math.floor(x);
    const iy = Math.floor(y);
    const iz = Math.floor(z);
    const fx = x - ix;
    const fy = y - iy;
    const fz = z - iz;
    const ux = fx * fx * (3 - 2 * fx);
    const uy = fy * fy * (3 - 2 * fy);
    const uz = fz * fz * (3 - 2 * fz);
    const c = (a: number, b: number, d: number) => hash(ix + a, iy + b, iz + d);
    const x00 = mix(c(0, 0, 0), c(1, 0, 0), ux);
    const x10 = mix(c(0, 1, 0), c(1, 1, 0), ux);
    const x01 = mix(c(0, 0, 1), c(1, 0, 1), ux);
    const x11 = mix(c(0, 1, 1), c(1, 1, 1), ux);
    return mix(mix(x00, x10, uy), mix(x01, x11, uy), uz) * 2 - 1;
}
