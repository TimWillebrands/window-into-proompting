import * as THREE from 'three';
import { mergeAll, type Placement, slab, unindexed } from './build';
import { rng } from './prefab';
import { HOUSE, HOUSE_STUB, mix } from './prefabs';
import {
    facing,
    type Ground,
    isFront,
    type Scene,
    TOP,
    WALKABLE,
} from './scene';

const WALL_H = 2.4;
const T = 0.12;

const GROUND: Partial<Record<Ground, { top: string; side: string }>> = {
    street: { top: '#8c8279', side: '#6e4a3b' },
    quay: { top: '#b3a796', side: '#7b4b3a' },
    bridge: { top: '#9d968d', side: '#86563f' },
    grass: { top: '#6fae4f', side: '#6b4f36' },
    house: { top: '#5f5a55', side: '#6b4a3a' },
    sand: { top: '#e3cf9a', side: '#b89b62' },
    path: { top: '#b89c74', side: '#6b4f36' },
    cobble: { top: '#8f8a84', side: '#5c5753' },
    deck: { top: '#a57a4c', side: '#6b4a2c' },
    pond: { top: '#3f8fa8', side: '#2c5f70' },
    flowers: { top: '#6fae4f', side: '#6b4f36' },
    hedge: { top: '#6fae4f', side: '#6b4f36' },
    fence: { top: '#6fae4f', side: '#6b4f36' },
    snow: { top: '#eef3f7', side: '#b9c4cf' },
};

const PETALS = ['#f2c14e', '#e86a92', '#f4f1de', '#c77dff', '#ff8552'];

export interface GroundMeshes {
    solid: THREE.BufferGeometry | null;
    /** Window panes: they show the sky, so they're lit by time of day. */
    glass: THREE.BufferGeometry | null;
    /** Arch lights under bridges, on at night. */
    lights: THREE.BufferGeometry | null;
    /** Daylight falling in through the windows, on the floor. */
    sunlit: THREE.BufferGeometry | null;
    houses: Placement[];
}

/** Floors, walls (cut away in front), quays, bridges and the houses' plots, merged per scene. */
export function buildGround(s: Scene): GroundMeshes {
    const solid: THREE.BufferGeometry[] = [];
    const glass: THREE.BufferGeometry[] = [];
    const lights: THREE.BufferGeometry[] = [];
    const sunlit: THREE.BufferGeometry[] = [];
    const houses: Placement[] = [];
    const outdoor = !!s.def.outdoor;
    const floor = s.def.floor ?? '#c8a173';
    const wall = s.def.wall ?? '#e9dcc4';
    const cap = mix(wall, '#000000', 0.35);
    const accent = s.def.accent ?? mix(floor, '#7a2f3d', 0.7);
    const floorStyle = s.def.floorStyle ?? 'planks';
    const wallStyle = s.def.wallStyle ?? 'wainscot';
    const g = (x: number, z: number): Ground =>
        s.cells[z]?.[x]?.ground ?? 'void';
    const bottom = outdoor ? -1.0 : -0.3;

    for (let z = 0; z < s.h; z++)
        for (let x = 0; x < s.w; x++) {
            const cell = s.cells[z][x];
            const r = rng((x + 3) * 7919 + (z + 5) * 104_729);
            const jitter = (c: string) =>
                mix(c, r() < 0.5 ? '#000000' : '#ffffff', r() * 0.06);
            const kind = cell.ground;
            if (kind === 'floor' || (kind === 'door' && !outdoor)) {
                const side = mix(floor, '#000000', 0.3);
                if (kind === 'door')
                    solid.push(
                        slab(
                            1,
                            -bottom,
                            1,
                            x + 0.5,
                            bottom,
                            z + 0.5,
                            mix(floor, '#000000', 0.25),
                            side,
                        ),
                    );
                else if (floorStyle === 'planks') {
                    // Boards run along x, a seam every tile and a half.
                    for (let i = 0; i < 4; i++) {
                        const shade = jitter(
                            mix(floor, '#000000', ((i + z) % 3) * 0.035),
                        );
                        solid.push(
                            slab(
                                1,
                                -bottom,
                                0.25,
                                x + 0.5,
                                bottom,
                                z + 0.125 + i * 0.25,
                                shade,
                                side,
                            ),
                        );
                        const seam = ((x * 2 + i * 3 + z) % 3) / 3 + 0.15;
                        solid.push(
                            slab(
                                0.03,
                                0.004,
                                0.25,
                                x + seam,
                                0,
                                z + 0.125 + i * 0.25,
                                mix(floor, '#000000', 0.13),
                                side,
                            ),
                        );
                    }
                    solid.push(
                        slab(
                            1,
                            0.003,
                            0.015,
                            x + 0.5,
                            0,
                            z + 0.008,
                            mix(floor, '#000000', 0.1),
                            side,
                        ),
                    );
                } else if (floorStyle === 'tiles') {
                    solid.push(
                        slab(
                            1,
                            -bottom - 0.01,
                            1,
                            x + 0.5,
                            bottom,
                            z + 0.5,
                            mix(floor, '#000000', 0.3),
                            side,
                        ),
                    );
                    for (const [dx, dz] of [
                        [0.25, 0.25],
                        [0.75, 0.25],
                        [0.25, 0.75],
                        [0.75, 0.75],
                    ])
                        solid.push(
                            slab(
                                0.47,
                                0.012,
                                0.47,
                                x + dx,
                                -0.012,
                                z + dz,
                                jitter(
                                    (Math.round(dx * 2 + dz * 2) + x + z) % 2
                                        ? floor
                                        : mix(floor, '#ffffff', 0.12),
                                ),
                                side,
                            ),
                        );
                } else {
                    const top =
                        floorStyle === 'checker'
                            ? (x + z) % 2
                                ? floor
                                : mix(floor, '#000000', 0.06)
                            : floorStyle === 'concrete'
                              ? jitter(jitter(floor))
                              : floor;
                    solid.push(
                        slab(
                            1,
                            -bottom,
                            1,
                            x + 0.5,
                            bottom,
                            z + 0.5,
                            top,
                            side,
                        ),
                    );
                }
                // Where floor meets a back wall it's a touch darker: cheap depth.
                if (kind === 'floor') {
                    const shade = mix(floor, '#000000', 0.2);
                    if (['wall', 'window'].includes(g(x, z - 1)))
                        solid.push(
                            slab(
                                1,
                                0.006,
                                0.14,
                                x + 0.5,
                                0,
                                z + 0.07,
                                shade,
                                shade,
                            ),
                        );
                    if (['wall', 'window'].includes(g(x - 1, z)))
                        solid.push(
                            slab(
                                0.14,
                                0.006,
                                1,
                                x + 0.07,
                                0,
                                z + 0.5,
                                shade,
                                shade,
                            ),
                        );
                    if (g(x, z - 1) === 'window')
                        sunlit.push(
                            slab(
                                0.8,
                                0.001,
                                1.7,
                                x + 0.5,
                                0.012,
                                z + 1.15,
                                '#fff3d6',
                                '#fff3d6',
                            ),
                        );
                    if (g(x - 1, z) === 'window')
                        sunlit.push(
                            slab(
                                1.7,
                                0.001,
                                0.8,
                                x + 1.15,
                                0.012,
                                z + 0.5,
                                '#fff3d6',
                                '#fff3d6',
                            ),
                        );
                }
            } else if (
                kind === 'carpet' ||
                kind === 'tile' ||
                kind === 'stage'
            ) {
                const top = TOP[kind];
                const base = kind === 'tile' ? '#e9ecef' : accent;
                const side = mix(base, '#000000', 0.35);
                if (kind === 'tile') {
                    solid.push(
                        slab(
                            1,
                            -bottom - 0.01,
                            1,
                            x + 0.5,
                            bottom,
                            z + 0.5,
                            '#9aa3ab',
                            side,
                        ),
                    );
                    for (const [dx, dz] of [
                        [0.25, 0.25],
                        [0.75, 0.25],
                        [0.25, 0.75],
                        [0.75, 0.75],
                    ])
                        solid.push(
                            slab(
                                0.47,
                                0.012,
                                0.47,
                                x + dx,
                                -0.012,
                                z + dz,
                                Math.round(dx * 2 + dz * 2) % 2
                                    ? base
                                    : mix(base, accent, 0.45),
                                side,
                            ),
                        );
                } else {
                    solid.push(
                        slab(
                            1,
                            top - bottom,
                            1,
                            x + 0.5,
                            bottom,
                            z + 0.5,
                            kind === 'carpet'
                                ? jitter(base)
                                : (x + z) % 2
                                  ? base
                                  : mix(base, '#000000', 0.08),
                            side,
                        ),
                    );
                    if (kind === 'stage') {
                        const lip = mix(base, '#ffffff', 0.25);
                        if (g(x, z + 1) !== 'stage')
                            solid.push(
                                slab(
                                    1,
                                    0.03,
                                    0.06,
                                    x + 0.5,
                                    top,
                                    z + 0.97,
                                    lip,
                                    lip,
                                ),
                            );
                        if (g(x + 1, z) !== 'stage')
                            solid.push(
                                slab(
                                    0.06,
                                    0.03,
                                    1,
                                    x + 0.97,
                                    top,
                                    z + 0.5,
                                    lip,
                                    lip,
                                ),
                            );
                    }
                }
            } else if (kind === 'water') {
                solid.push(
                    slab(
                        1,
                        0.56,
                        1,
                        x + 0.5,
                        -1.0,
                        z + 0.5,
                        '#1d4350',
                        '#1a3a46',
                    ),
                );
            } else if (GROUND[kind]) {
                const c = GROUND[kind];
                if (!c) continue;
                const top = TOP[kind];
                solid.push(
                    slab(
                        1,
                        top - bottom,
                        1,
                        x + 0.5,
                        bottom,
                        z + 0.5,
                        jitter(c.top),
                        c.side,
                    ),
                );
                if (kind === 'street' && r() < 0.5)
                    solid.push(
                        slab(
                            0.3,
                            0.005,
                            0.3,
                            x + 0.2 + r() * 0.6,
                            top,
                            z + 0.2 + r() * 0.6,
                            mix(c.top, '#000000', 0.12),
                            c.side,
                        ),
                    );
                if (kind === 'bridge') bridgeRails(s, x, z, solid);
                if (kind === 'cobble')
                    for (let i = 0; i < 5; i++)
                        solid.push(
                            slab(
                                0.2 + r() * 0.12,
                                0.012,
                                0.2 + r() * 0.12,
                                x + 0.15 + r() * 0.7,
                                top,
                                z + 0.15 + r() * 0.7,
                                mix(
                                    c.top,
                                    r() < 0.5 ? '#000000' : '#ffffff',
                                    0.1 + r() * 0.1,
                                ),
                                c.side,
                            ),
                        );
                if (kind === 'deck')
                    for (let i = 1; i < 4; i++)
                        solid.push(
                            slab(
                                0.02,
                                0.004,
                                1,
                                x + i * 0.25,
                                top,
                                z + 0.5,
                                mix(c.top, '#000000', 0.3),
                                c.side,
                            ),
                        );
                if (kind === 'path' || kind === 'sand')
                    for (let i = 0; i < 2; i++)
                        solid.push(
                            slab(
                                0.08,
                                0.01,
                                0.08,
                                x + 0.1 + r() * 0.8,
                                top,
                                z + 0.1 + r() * 0.8,
                                mix(c.top, '#000000', 0.18),
                                c.side,
                            ),
                        );
                if (kind === 'grass' && r() < 0.35)
                    solid.push(
                        slab(
                            0.05,
                            0.07,
                            0.05,
                            x + 0.15 + r() * 0.7,
                            top,
                            z + 0.15 + r() * 0.7,
                            mix(c.top, '#ffffff', 0.12),
                            mix(c.top, '#000000', 0.1),
                        ),
                    );
                if (kind === 'flowers')
                    for (let i = 0; i < 5; i++) {
                        const [fx, fz] = [
                            x + 0.12 + r() * 0.76,
                            z + 0.12 + r() * 0.76,
                        ];
                        const petal = PETALS[Math.floor(r() * PETALS.length)];
                        solid.push(
                            slab(
                                0.025,
                                0.14,
                                0.025,
                                fx,
                                top,
                                fz,
                                '#3f7d34',
                                '#3f7d34',
                            ),
                        );
                        solid.push(
                            slab(
                                0.09,
                                0.06,
                                0.09,
                                fx,
                                top + 0.13,
                                fz,
                                petal,
                                mix(petal, '#000000', 0.15),
                            ),
                        );
                    }
                if (kind === 'hedge') {
                    const leaf = jitter('#3f7d34');
                    solid.push(
                        slab(
                            0.92,
                            0.72,
                            0.92,
                            x + 0.5,
                            top,
                            z + 0.5,
                            mix(leaf, '#ffffff', 0.08),
                            leaf,
                        ),
                    );
                }
                if (kind === 'fence') {
                    const wood = '#d9cdb8';
                    const alongX =
                        g(x - 1, z) === 'fence' || g(x + 1, z) === 'fence';
                    for (const d of [-0.4, 0, 0.4])
                        solid.push(
                            alongX
                                ? slab(
                                      0.07,
                                      0.62,
                                      0.07,
                                      x + 0.5 + d,
                                      top,
                                      z + 0.5,
                                      wood,
                                      wood,
                                  )
                                : slab(
                                      0.07,
                                      0.62,
                                      0.07,
                                      x + 0.5,
                                      top,
                                      z + 0.5 + d,
                                      wood,
                                      wood,
                                  ),
                        );
                    for (const y of [0.22, 0.46])
                        solid.push(
                            alongX
                                ? slab(
                                      1,
                                      0.06,
                                      0.04,
                                      x + 0.5,
                                      top + y,
                                      z + 0.5,
                                      wood,
                                      wood,
                                  )
                                : slab(
                                      0.04,
                                      0.06,
                                      1,
                                      x + 0.5,
                                      top + y,
                                      z + 0.5,
                                      wood,
                                      wood,
                                  ),
                        );
                }
                if (kind === 'house') {
                    const front = isFront(s, x, z);
                    const face = facing(s, x, z);
                    houses.push({
                        prefab: front ? HOUSE_STUB : HOUSE(cell.house),
                        variant: {
                            seed: (x + 1) * 7919 + (z + 1) * 104_729,
                            tint: {},
                        },
                        x: x + 0.5,
                        z: z + 0.5,
                        y: 0,
                        rot: face === 'x' ? 1 : 0,
                    });
                }
            }
            if (
                kind === 'wall' ||
                kind === 'window' ||
                (kind === 'door' && !outdoor)
            ) {
                if (isFront(s, x, z)) {
                    if (kind === 'door') continue;
                    if (WALKABLE.has(g(x - 1, z)))
                        solid.push(
                            slab(T, 0.16, 1, x + T / 2, 0, z + 0.5, cap, wall),
                        );
                    if (WALKABLE.has(g(x, z - 1)))
                        solid.push(
                            slab(1, 0.16, T, x + 0.5, 0, z + T / 2, cap, wall),
                        );
                    continue;
                }
                const face = facing(s, x, z);
                if (!face) {
                    if (WALKABLE.has(g(x + 1, z + 1)))
                        solid.push(
                            slab(
                                T,
                                WALL_H,
                                T,
                                x + 1 - T / 2,
                                0,
                                z + 1 - T / 2,
                                cap,
                                wall,
                            ),
                        );
                    continue;
                }
                // Along the tile edge next to the room: x-walls run along z, z-walls along x.
                const along = (
                    w: number,
                    h: number,
                    y: number,
                    top: string,
                    side: string,
                    inset = 0,
                ) =>
                    face === 'z'
                        ? slab(
                              w,
                              h,
                              T,
                              x + 0.5,
                              y,
                              z + 1 - T / 2 + inset,
                              top,
                              side,
                          )
                        : slab(
                              T,
                              h,
                              w,
                              x + 1 - T / 2 + inset,
                              y,
                              z + 0.5,
                              top,
                              side,
                          );
                const offset = (
                    d: number,
                    w: number,
                    h: number,
                    y: number,
                    c: string,
                ) =>
                    face === 'z'
                        ? slab(w, h, T, x + 0.5 + d, y, z + 1 - T / 2, cap, c)
                        : slab(T, h, w, x + 1 - T / 2, y, z + 0.5 + d, cap, c);
                const wainscot =
                    wallStyle === 'panel' ? accent : mix(wall, '#000000', 0.12);
                const skirting = mix(wainscot, '#000000', 0.3);
                const dress = (full: boolean) => {
                    // Skirting board and a rail under the ceiling line, on every style.
                    solid.push(along(1, 0.1, 0, skirting, skirting, 0.03));
                    if (wallStyle === 'wainscot')
                        solid.push(
                            along(1, 0.8, 0, wainscot, wainscot, 0.012),
                            along(
                                1,
                                0.04,
                                0.8,
                                mix(wall, '#ffffff', 0.3),
                                mix(wall, '#ffffff', 0.3),
                                0.025,
                            ),
                        );
                    if (wallStyle === 'panel') {
                        solid.push(
                            along(1, 1.05, 0, wainscot, wainscot, 0.012),
                        );
                        solid.push(
                            along(
                                1,
                                0.05,
                                1.05,
                                mix(wainscot, '#ffffff', 0.2),
                                mix(wainscot, '#ffffff', 0.2),
                                0.03,
                            ),
                        );
                        for (const d of [-0.25, 0.25])
                            solid.push(
                                offset(
                                    d,
                                    0.04,
                                    1.0,
                                    0.05,
                                    mix(wainscot, '#000000', 0.18),
                                ),
                            );
                    }
                    if (!full) return;
                    solid.push(
                        along(
                            1,
                            0.12,
                            WALL_H - 0.12,
                            mix(wall, '#ffffff', 0.35),
                            mix(wall, '#ffffff', 0.35),
                            0.02,
                        ),
                    );
                    if (wallStyle === 'stripes')
                        for (const d of [-0.375, 0.125])
                            solid.push(
                                face === 'z'
                                    ? slab(
                                          0.25,
                                          WALL_H - 0.2,
                                          0.01,
                                          x + 0.5 + d,
                                          0.1,
                                          z + 1 + 0.004,
                                          accent,
                                          accent,
                                      )
                                    : slab(
                                          0.01,
                                          WALL_H - 0.2,
                                          0.25,
                                          x + 1 + 0.004,
                                          0.1,
                                          z + 0.5 + d,
                                          accent,
                                          accent,
                                      ),
                            );
                    if (wallStyle === 'brick')
                        for (let row = 0; row < 9; row++)
                            for (const d of row % 2
                                ? [-0.25, 0.25]
                                : [-0.5, 0, 0.5]) {
                                const brick = jitter(
                                    mix(
                                        wall,
                                        '#000000',
                                        0.08 + ((row * 7 + x + z) % 3) * 0.04,
                                    ),
                                );
                                const wide = Math.abs(d) === 0.5 ? 0.22 : 0.46;
                                const dd =
                                    d === -0.5 ? -0.385 : d === 0.5 ? 0.385 : d;
                                solid.push(
                                    face === 'z'
                                        ? slab(
                                              wide,
                                              0.2,
                                              0.012,
                                              x + 0.5 + dd,
                                              0.14 + row * 0.24,
                                              z + 1 + 0.004,
                                              brick,
                                              brick,
                                          )
                                        : slab(
                                              0.012,
                                              0.2,
                                              wide,
                                              x + 1 + 0.004,
                                              0.14 + row * 0.24,
                                              z + 0.5 + dd,
                                              brick,
                                              brick,
                                          ),
                                );
                            }
                };
                if (kind === 'wall') {
                    solid.push(along(1, WALL_H, 0, cap, wall));
                    dress(true);
                } else if (kind === 'window') {
                    solid.push(along(1, 0.9, 0, cap, wall));
                    dress(false);
                    solid.push(
                        along(1, 0.06, 0.88, '#f3efe6', '#f3efe6', 0.06),
                    );
                    solid.push(along(1, 0.55, 1.85, cap, wall));
                    solid.push(
                        offset(-0.45, 0.1, 0.95, 0.9, '#f3efe6'),
                        offset(0.45, 0.1, 0.95, 0.9, '#f3efe6'),
                    );
                    solid.push(
                        offset(0, 0.06, 0.95, 0.9, '#f3efe6'),
                        along(1, 0.06, 0.9, '#f3efe6', '#f3efe6', 0.02),
                    );
                    glass.push(
                        along(0.8, 0.95, 0.9, '#ffffff', '#ffffff', -0.02),
                    );
                } else {
                    solid.push(
                        offset(-0.44, 0.12, 2.0, 0, mix(wall, '#000000', 0.25)),
                        offset(0.44, 0.12, 2.0, 0, mix(wall, '#000000', 0.25)),
                    );
                    solid.push(along(1, WALL_H - 2.0, 2.0, cap, wall));
                    const back =
                        face === 'z'
                            ? slab(
                                  0.8,
                                  2.0,
                                  0.02,
                                  x + 0.5,
                                  0,
                                  z + 0.2,
                                  '#1e1712',
                                  '#1e1712',
                              )
                            : slab(
                                  0.02,
                                  2.0,
                                  0.8,
                                  x + 0.2,
                                  0,
                                  z + 0.5,
                                  '#1e1712',
                                  '#1e1712',
                              );
                    solid.push(back);
                }
            }
        }
    if (outdoor)
        for (const arch of arches(s)) {
            solid.push(arch.dark);
            lights.push(...arch.lights);
        }
    return {
        solid: mergeAll(solid),
        glass: mergeAll(glass),
        lights: mergeAll(lights),
        sunlit: mergeAll(sunlit),
        houses,
    };
}

/** Railings on bridge edges over water. */
function bridgeRails(
    s: Scene,
    x: number,
    z: number,
    out: THREE.BufferGeometry[],
) {
    const top = TOP.bridge;
    const iron = '#2a2f2c';
    const wet = (dx: number, dz: number) =>
        s.cells[z + dz]?.[x + dx]?.ground === 'water';
    const rail = (cx: number, cz: number, alongX: boolean) => {
        out.push(
            alongX
                ? slab(1, 0.05, 0.06, cx, top + 0.5, cz, iron, iron)
                : slab(0.06, 0.05, 1, cx, top + 0.5, cz, iron, iron),
        );
        for (const d of [-0.4, 0, 0.4])
            out.push(
                alongX
                    ? slab(0.04, 0.5, 0.04, cx + d, top, cz, iron, iron)
                    : slab(0.04, 0.5, 0.04, cx, top, cz + d, iron, iron),
            );
        out.push(
            alongX
                ? slab(1, 0.12, 0.12, cx, top, cz, '#9d968d', '#86563f')
                : slab(0.12, 0.12, 1, cx, top, cz, '#9d968d', '#86563f'),
        );
    };
    if (wet(-1, 0)) rail(x + 0.06, z + 0.5, false);
    if (wet(1, 0)) rail(x + 0.94, z + 0.5, false);
    if (wet(0, -1)) rail(x + 0.5, z + 0.06, true);
    if (wet(0, 1)) rail(x + 0.5, z + 0.94, true);
}

/** The dark arch on a bridge's camera-facing side, with its string of lights. */
function arches(s: Scene) {
    const out: {
        dark: THREE.BufferGeometry;
        lights: THREE.BufferGeometry[];
    }[] = [];
    const isBridge = (x: number, z: number) =>
        s.cells[z]?.[x]?.ground === 'bridge';
    const wet = (x: number, z: number) => s.cells[z]?.[x]?.ground === 'water';
    const seen = new Set<string>();
    for (let z = 0; z < s.h; z++)
        for (let x = 0; x < s.w; x++) {
            if (!isBridge(x, z)) continue;
            for (const dir of ['x', 'z'] as const) {
                const k = `${dir}:${x},${z}`;
                if (seen.has(k)) continue;
                const open = dir === 'x' ? wet(x + 1, z) : wet(x, z + 1);
                if (!open) continue;
                let n = 0;
                while (
                    dir === 'x'
                        ? isBridge(x, z + n) && wet(x + 1, z + n)
                        : isBridge(x + n, z) && wet(x + n, z + 1)
                ) {
                    seen.add(
                        dir === 'x' ? `x:${x},${z + n}` : `z:${x + n},${z}`,
                    );
                    n++;
                }
                const r = n * 0.42;
                const cx = dir === 'x' ? x + 1.005 : x + n / 2;
                const cz = dir === 'x' ? z + n / 2 : z + 1.005;
                const disc = new THREE.CylinderGeometry(
                    r,
                    r,
                    0.02,
                    20,
                    1,
                    false,
                    0,
                    Math.PI,
                );
                // Half disc, upper half, in the face's plane.
                if (dir === 'x') disc.rotateZ(Math.PI / 2);
                else disc.rotateX(Math.PI / 2).rotateZ(Math.PI / 2);
                disc.translate(cx, -0.42, cz);
                const dark = paintAll(unindexed(disc), '#122027');
                const lights: THREE.BufferGeometry[] = [];
                for (let i = 1; i < 10; i++) {
                    const a = (i / 10) * Math.PI;
                    const along = Math.cos(a) * (r + 0.06);
                    const up = Math.sin(a) * (r + 0.06) - 0.42;
                    const b = unindexed(
                        new THREE.IcosahedronGeometry(0.035, 0),
                    );
                    b.translate(
                        dir === 'x' ? cx + 0.02 : cx + along,
                        up,
                        dir === 'x' ? cz + along : cz + 0.02,
                    );
                    lights.push(paintAll(b, '#ffe8a3'));
                }
                out.push({ dark, lights });
            }
        }
    return out;
}

function paintAll(g: THREE.BufferGeometry, hex: string) {
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
