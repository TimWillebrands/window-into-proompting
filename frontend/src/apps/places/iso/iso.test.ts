import { describe, expect, it } from 'vitest';
import { depth, tileCentre, toScreen, toTile } from './iso';
import { blocked, layoutFor, slotTile } from './layout';
import { along, findPath } from './path';

describe('iso projection', () => {
    it('round-trips a tile centre back to its tile', () => {
        for (const t of [
            { x: 0, y: 0 },
            { x: 3, y: 5 },
            { x: 9, y: 1 },
        ]) {
            expect(toTile(tileCentre(t, 2), 2)).toEqual(t);
        }
    });

    it('draws the back of the room first', () => {
        expect(depth({ x: 1, y: 1 })).toBeLessThan(depth({ x: 2, y: 3 }));
        expect(toScreen({ x: 1, y: 0 }).x).toBeGreaterThan(
            toScreen({ x: 0, y: 1 }).x,
        );
    });
});

describe('layout from spots', () => {
    it('gives each spot an anchor and slots around it, inside the room', () => {
        const l = layoutFor(['counter', 'booth', 'jukebox']);
        expect(Object.keys(l.spots)).toEqual(['counter', 'booth', 'jukebox']);
        for (const s of Object.values(l.spots))
            for (const p of s.slots) {
                expect(p.x).toBeGreaterThanOrEqual(0);
                expect(p.y).toBeLessThan(l.h);
                expect(p).not.toEqual(s.anchor);
            }
        expect(slotTile(l, 'counter', 0)).not.toEqual(
            slotTile(l, 'counter', 1),
        );
        expect(slotTile(l, 'nowhere', 0)).toEqual(l.door);
    });
});

describe('walking', () => {
    it('routes around furni and interpolates along the way', () => {
        const l = layoutFor(['a']);
        const anchor = l.spots.a.anchor;
        const from = { x: anchor.x - 1, y: anchor.y };
        const to = { x: anchor.x + 1, y: anchor.y };
        const path = findPath(from, to, l.w, l.h, blocked(l));
        expect(path[0]).toEqual(from);
        expect(path[path.length - 1]).toEqual(to);
        expect(path).not.toContainEqual(anchor);
        expect(along(path, 0)).toEqual(from);
        expect(along(path, 1)).toEqual(to);
    });
});
