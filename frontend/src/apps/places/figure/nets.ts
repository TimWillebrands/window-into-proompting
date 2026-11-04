import type { Field } from './sdf';

export interface Surface {
    positions: Float32Array;
    normals: Float32Array;
    indices: Uint32Array;
}

const BLOCK = 4;
const EDGES = [
    [0, 1],
    [0, 2],
    [0, 4],
    [1, 3],
    [1, 5],
    [2, 3],
    [2, 6],
    [3, 7],
    [4, 5],
    [4, 6],
    [5, 7],
    [6, 7],
];

/** Polygonises a field with surface nets: skips far blocks, then snaps vertices onto the surface. */
export function surfaceNets(
    field: Field,
    min: [number, number, number],
    max: [number, number, number],
    h: number,
): Surface {
    const nx = Math.ceil((max[0] - min[0]) / h) + 1;
    const ny = Math.ceil((max[1] - min[1]) / h) + 1;
    const nz = Math.ceil((max[2] - min[2]) / h) + 1;
    const grid = new Float32Array(nx * ny * nz);
    const sy = nx;
    const sz = nx * ny;
    // A block whose centre is further from the surface than its half diagonal can't contain any.
    const reach = BLOCK * h * 0.87 * 1.8;
    for (let bz = 0; bz < nz; bz += BLOCK)
        for (let by = 0; by < ny; by += BLOCK)
            for (let bx = 0; bx < nx; bx += BLOCK) {
                const d = field(
                    min[0] + (bx + BLOCK / 2) * h,
                    min[1] + (by + BLOCK / 2) * h,
                    min[2] + (bz + BLOCK / 2) * h,
                );
                const far = Math.abs(d) > reach;
                const ez = Math.min(bz + BLOCK, nz);
                const ey = Math.min(by + BLOCK, ny);
                const ex = Math.min(bx + BLOCK, nx);
                for (let z = bz; z < ez; z++)
                    for (let y = by; y < ey; y++)
                        for (let x = bx; x < ex; x++)
                            grid[x + y * sy + z * sz] = far
                                ? d
                                : field(
                                      min[0] + x * h,
                                      min[1] + y * h,
                                      min[2] + z * h,
                                  );
            }

    return netsFromGrid(grid, [nx, ny, nz], min, h, field);
}

/** Surface nets over a filled grid; field refines vertices and gives normals. */
export function netsFromGrid(
    grid: Float32Array,
    [nx, ny, nz]: [number, number, number],
    min: [number, number, number],
    h: number,
    field: Field,
): Surface {
    const sy = nx;
    const sz = nx * ny;
    const cell = new Int32Array(nx * ny * nz).fill(-1);
    const verts: number[] = [];
    const corner = new Float64Array(8);
    for (let z = 0; z < nz - 1; z++)
        for (let y = 0; y < ny - 1; y++)
            for (let x = 0; x < nx - 1; x++) {
                const base = x + y * sy + z * sz;
                let mask = 0;
                for (let c = 0; c < 8; c++) {
                    const v =
                        grid[
                            base +
                                (c & 1) +
                                ((c >> 1) & 1) * sy +
                                ((c >> 2) & 1) * sz
                        ];
                    corner[c] = v;
                    if (v < 0) mask |= 1 << c;
                }
                if (mask === 0 || mask === 255) continue;
                let cx = 0;
                let cy = 0;
                let cz = 0;
                let n = 0;
                for (const [a, b] of EDGES) {
                    const va = corner[a];
                    const vb = corner[b];
                    if (va < 0 === vb < 0) continue;
                    const t = va / (va - vb);
                    cx += (a & 1) + ((b & 1) - (a & 1)) * t;
                    cy +=
                        ((a >> 1) & 1) + (((b >> 1) & 1) - ((a >> 1) & 1)) * t;
                    cz +=
                        ((a >> 2) & 1) + (((b >> 2) & 1) - ((a >> 2) & 1)) * t;
                    n++;
                }
                cell[base] = verts.length / 3;
                verts.push(
                    min[0] + (x + cx / n) * h,
                    min[1] + (y + cy / n) * h,
                    min[2] + (z + cz / n) * h,
                );
            }

    const indices: number[] = [];
    const quad = (
        a: number,
        b: number,
        c: number,
        d: number,
        flip: boolean,
    ) => {
        if (a < 0 || b < 0 || c < 0 || d < 0) return;
        if (flip) indices.push(a, c, b, a, d, c);
        else indices.push(a, b, c, a, c, d);
    };
    for (let z = 1; z < nz - 1; z++)
        for (let y = 1; y < ny - 1; y++)
            for (let x = 1; x < nx - 1; x++) {
                const i = x + y * sy + z * sz;
                const inside = grid[i] < 0;
                if (inside !== grid[i + 1] < 0)
                    quad(
                        cell[i - sy - sz],
                        cell[i - sz],
                        cell[i],
                        cell[i - sy],
                        !inside,
                    );
                if (inside !== grid[i + sy] < 0)
                    quad(
                        cell[i - 1 - sz],
                        cell[i - 1],
                        cell[i],
                        cell[i - sz],
                        !inside,
                    );
                if (inside !== grid[i + sz] < 0)
                    quad(
                        cell[i - 1 - sy],
                        cell[i - sy],
                        cell[i],
                        cell[i - 1],
                        !inside,
                    );
            }

    const count = verts.length / 3;
    const positions = new Float32Array(verts);
    const normals = new Float32Array(count * 3);
    const e = h * 0.5;
    for (let i = 0; i < count; i++) {
        let x = positions[i * 3];
        let y = positions[i * 3 + 1];
        let z = positions[i * 3 + 2];
        let gx = 0;
        let gy = 0;
        let gz = 0;
        for (let step = 0; step < 2; step++) {
            gx = field(x + e, y, z) - field(x - e, y, z);
            gy = field(x, y + e, z) - field(x, y - e, z);
            gz = field(x, y, z + e) - field(x, y, z - e);
            const g2 = gx * gx + gy * gy + gz * gz || 1e-12;
            const d = field(x, y, z);
            const k =
                Math.max(-h, Math.min(h, (d * 2 * e) / Math.sqrt(g2))) /
                Math.sqrt(g2);
            if (step === 0) {
                x -= gx * k;
                y -= gy * k;
                z -= gz * k;
            }
        }
        const l = Math.hypot(gx, gy, gz) || 1;
        positions[i * 3] = x;
        positions[i * 3 + 1] = y;
        positions[i * 3 + 2] = z;
        normals[i * 3] = gx / l;
        normals[i * 3 + 1] = gy / l;
        normals[i * 3 + 2] = gz / l;
    }
    return { positions, normals, indices: Uint32Array.from(indices) };
}
