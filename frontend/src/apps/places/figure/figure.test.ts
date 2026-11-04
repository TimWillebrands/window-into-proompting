import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { describe, expect, it } from 'vitest';
import type { Look } from '../iso/appearance';
import { compileFigure, SCENE_DETAIL } from './compile';
import { EXTRAS, figureDoc, HAIR, paletteCovers } from './dress';
import { MOVES, standMove, talkMove } from './moves';
import { moveClip } from './pose';
import { SAMPLES } from './samples';
import designed from './samples.llm.json';
import { BONES } from './skeleton';

const LOOK: Look = {
    skin: '#f1c9a5',
    hair: '#5a3a22',
    hairStyle: 'short',
    top: '#d9534f',
    bottom: '#3b3f58',
    accessory: 'none',
    topStyle: 'tshirt',
    bottomStyle: 'jeans',
    shoes: '#2b2b2b',
    shoeStyle: 'sneakers',
    legwear: 'none',
    facialHair: 'none',
    build: 'average',
    height: 'average',
    eyes: '#1d1d1d',
};

const rangeY = (positions: Float32Array) => {
    let lo = Infinity;
    let hi = -Infinity;
    for (let i = 1; i < positions.length; i += 3) {
        lo = Math.min(lo, positions[i]);
        hi = Math.max(hi, positions[i]);
    }
    return [lo, hi];
};

describe('compileFigure', () => {
    it.each(SAMPLES.map((d, i) => [i, d] as const))(
        'builds sample %i soundly and within budget',
        (_, doc) => {
            const c = compileFigure(doc, SCENE_DETAIL);
            const tris = c.layers.reduce((s, l) => s + l.indices.length / 3, 0);
            expect(tris).toBeLessThan(40000);
            for (const l of c.layers) {
                expect(l.positions.every(Number.isFinite)).toBe(true);
                expect(l.normals.every(Number.isFinite)).toBe(true);
                for (let i = 0; i < l.skinWeight.length; i += 4) {
                    const sum =
                        l.skinWeight[i] +
                        l.skinWeight[i + 1] +
                        l.skinWeight[i + 2] +
                        l.skinWeight[i + 3];
                    expect(sum).toBeCloseTo(1, 3);
                }
                for (const b of l.skinIndex)
                    expect(b).toBeLessThan(BONES.length);
                for (const v of l.indices)
                    expect(v).toBeLessThan(l.positions.length / 3);
            }
        },
    );

    it.each(designed.map((d) => [d.id, d] as const))(
        'compiles real LLM answer %s',
        (_, d) => {
            const c = compileFigure(
                figureDoc({ ...LOOK, design: d.design as Look['design'] }),
                SCENE_DETAIL,
            );
            expect(c.layers.length).toBeGreaterThan(5);
            for (const l of c.layers)
                expect(l.positions.every(Number.isFinite)).toBe(true);
        },
    );

    it('leaves a neck showing above a crew collar', () => {
        const c = compileFigure(figureDoc(LOOK), SCENE_DETAIL);
        const top = c.layers.find((l) => l.paint === 'top');
        const head = c.layers.find((l) => l.kind === 'head');
        const collar = rangeY(top?.positions as Float32Array)[1];
        const chin = rangeY(head?.positions as Float32Array)[0];
        expect(chin - collar).toBeGreaterThan(0.015);
    });

    it('keeps a baggy shirt inside a fitted jacket', () => {
        const doc = figureDoc(LOOK);
        const inner = { ...doc.top, fit: 1, sleeve: 1 };
        const jacket = { ...doc.top, fit: 0.1, sleeve: 1, hem: 0.6 };
        const alone = compileFigure({ ...doc, top: inner }, SCENE_DETAIL);
        const worn = compileFigure(
            { ...doc, top: inner, outer: jacket },
            SCENE_DETAIL,
        );
        const tris = (c: typeof alone) =>
            (c.layers.find((l) => l.paint === 'top')?.indices.length ?? 0) / 3;
        expect(tris(worn)).toBeLessThan(tris(alone) * 0.2);
    });

    it('stops a short sleeve above the long one under it', () => {
        const doc = figureDoc(LOOK);
        const tee = { ...doc.top, sleeve: 0.28 };
        const under = { ...doc.top, sleeve: 1, fit: 0.1 };
        const c = compileFigure({ ...doc, top: tee, under }, SCENE_DETAIL);
        const layer = c.layers.find((l) => l.paint === 'top');
        let lowest = Infinity;
        const positions = layer?.positions as Float32Array;
        for (let i = 0; i < positions.length; i += 3)
            if (Math.abs(positions[i]) > 0.25)
                lowest = Math.min(lowest, positions[i + 1]);
        // The long sleeve under it runs to the wrist; the tee stops well above the elbow.
        expect(lowest).toBeGreaterThan(0.74);
    });

    it('stands on the floor', () => {
        const c = compileFigure(figureDoc(LOOK), SCENE_DETAIL);
        const shoes = c.layers.find((l) => l.name === 'shoes');
        expect(rangeY(shoes?.positions as Float32Array)[0]).toBeCloseTo(
            -c.lift,
            1,
        );
    });

    it('survives every hairstyle and extra', { timeout: 30000 }, () => {
        for (const hairStyle of Object.keys(HAIR)) {
            const c = compileFigure(
                figureDoc({ ...LOOK, hairStyle }),
                SCENE_DETAIL,
            );
            expect(c.layers.find((l) => l.kind === 'head')).toBeTruthy();
        }
        for (const accessory of Object.keys(EXTRAS)) {
            const c = compileFigure(
                figureDoc({ ...LOOK, accessory }),
                SCENE_DETAIL,
            );
            if (accessory !== 'none')
                expect(c.layers.some((l) => l.positions.length > 0)).toBe(true);
        }
    });
});

describe('figureDoc', () => {
    it('lays a design over the palette, slot by slot', () => {
        const doc = figureDoc({
            ...LOOK,
            accessory: 'glasses',
            design: {
                shoes: {
                    kind: 'platform',
                    color: '#111111',
                    sole: '#222222',
                    trim: '#333333',
                    platform: 1,
                },
                extras: [{ name: 'chain', socket: 'waist', blobs: [] }],
            },
        });
        expect(doc.shoes.kind).toBe('platform');
        expect(doc.top.color).toBe(LOOK.top);
        expect(doc.extras.map((e) => e.name)).toEqual(['glasses', 'chain']);
    });

    it('lets a dye through a designed cut', () => {
        const hair = { cap: 1, zones: [] } as unknown as NonNullable<
            Look['design']
        >['hair'];
        expect(
            figureDoc({ ...LOOK, hair: '#e57fb0', design: { hair } }).hair
                .color,
        ).toBe('#e57fb0');
    });

    it('covers every value on the backend palette', () => {
        const source = readFileSync(
            resolve(
                __dirname,
                '../../../../../backend/World/Places/Appearance.cs',
            ),
            'utf8',
        );
        const list = (name: string) =>
            [
                ...(
                    source.match(new RegExp(`${name} = \\{([^}]*)\\}`))?.[1] ??
                    ''
                ).matchAll(/"([^"]+)"/g),
            ].map((m) => m[1]);
        for (const [name, covered] of Object.entries(paletteCovers())) {
            const values = list(name);
            expect(values.length).toBeGreaterThan(0);
            expect(values.filter((v) => !covered.includes(v))).toEqual([]);
        }
    });
});

describe('moves', () => {
    it('turn into clips', () => {
        const all = {
            ...MOVES,
            stand: standMove(),
            talk: talkMove({ spine: [10, 0, 0] }),
        };
        for (const [name, move] of Object.entries(all)) {
            const clip = moveClip(name, move, 0.7);
            expect(clip.duration).toBe(move.loop);
            for (const t of clip.tracks)
                expect(Array.from(t.values).every(Number.isFinite)).toBe(true);
        }
    });

    it('cover the poses the backend can set', () => {
        const source = readFileSync(
            resolve(__dirname, '../../../../../backend/World/Places/Poses.cs'),
            'utf8',
        );
        const poses = [...source.matchAll(/"([a-z-]+)"/g)].map((m) => m[1]);
        expect(poses).toContain('lean');
        for (const pose of [
            'lean',
            'lounge',
            'perch',
            'cross-legged',
            'think',
            'wave',
            'hug',
            'slow-dance',
            'high-five',
        ])
            expect(MOVES[pose]).toBeTruthy();
    });
});
