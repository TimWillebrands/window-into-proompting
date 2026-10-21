import { describe, expect, it } from 'vitest';
import type { Body } from '../../../api/model/body';
import type { Place } from '../../../api/model/place';
import { targets } from './motion';
import { PREFABS } from './prefabs';
import { entrance, key, parseScene, pathIn, rows, WALKABLE } from './scene';
import { generateScene, placeLoose, SCENES, sceneFor } from './scenes';

/** Spots and capacities as backend PlaceSeeds has them. */
const SEEDED: Record<string, Record<string, number>> = {
    cafe: { counter: 3, 'window-table': 2, booth: 4, jukebox: 2 },
    lobby: { entrance: 4, sofas: 4, desk: 3, window: 2 },
    home: { sofa: 3, desk: 1, kitchen: 3, bed: 2 },
    centre: { bridge: 4, bench: 3, steps: 3, cart: 3, bikes: 2 },
};

const placeOf = (scene: string): Place => ({
    id: `p-${scene}`,
    name: scene,
    kind: scene === 'home' ? 'home' : 'public',
    scene,
    spots: Object.keys(SEEDED[scene]).map((id) => ({ id, name: id })),
});

describe('scenes', () => {
    it.each(Object.keys(SCENES))(
        '%s: a rectangular map with every seeded spot',
        (id) => {
            const lines = rows(SCENES[id].map);
            expect(new Set(lines.map((l) => l.length)).size).toBe(1);
            const s = sceneFor(placeOf(id));
            for (const [spot, capacity] of Object.entries(SEEDED[id])) {
                const slots = s.spots[spot];
                expect(slots, spot).toBeDefined();
                expect(
                    slots.seats.length + slots.stands.length,
                    spot,
                ).toBeGreaterThanOrEqual(capacity);
            }
        },
    );

    it.each(Object.keys(SCENES))(
        '%s: every spot is reachable from the way in',
        (id) => {
            const s = sceneFor(placeOf(id));
            const door = entrance(s);
            expect(WALKABLE.has(s.cells[door.y][door.x].ground)).toBe(true);
            for (const [spot, slots] of Object.entries(s.spots))
                for (const slot of [...slots.stands, ...slots.seats]) {
                    const full = pathIn(s, door, slot.tile);
                    expect(full.at(-1)).toEqual(slot.tile);
                    // A seat inside furniture is slid into from the last free tile.
                    const path =
                        slot.seat && s.blocked.has(key(slot.tile))
                            ? full.slice(0, -1)
                            : full;
                    const [a, b] = [
                        full[full.length - 1],
                        path[path.length - 1],
                    ];
                    expect(
                        Math.hypot(a.x - b.x, a.y - b.y),
                    ).toBeLessThanOrEqual(2.9);
                    for (const t of path.slice(1))
                        expect(
                            s.blocked.has(key(t)),
                            `${spot} via ${key(t)}`,
                        ).toBe(false);
                    for (let i = 1; i < path.length; i++)
                        expect(
                            Math.abs(path[i].x - path[i - 1].x) +
                                Math.abs(path[i].y - path[i - 1].y),
                            `${spot} step ${i}`,
                        ).toBe(1);
                }
        },
    );

    it('the city centre has canals, a bridge and a door for each Place that opens onto it', () => {
        const s = sceneFor(placeOf('centre'));
        const grounds = new Set(s.cells.flat().map((c) => c.ground));
        for (const g of ['water', 'bridge', 'street', 'quay', 'house'])
            expect(grounds).toContain(g);
        expect(entrance(s, 'cafe')).toEqual({ x: 19, y: 1 });
        expect(entrance(s, 'somewhere-else')).toEqual({ x: 16, y: 19 });
    });

    it('sitters take seats, standers stand', () => {
        const s = sceneFor(placeOf('home'));
        const bodies: Body[] = [
            {
                participantId: 'a',
                placeId: 'p-home',
                spotId: 'sofa',
                posture: 'Sitting',
            },
            {
                participantId: 'b',
                placeId: 'p-home',
                spotId: 'sofa',
                posture: 'Standing',
            },
        ];
        const t = targets(s, 'p-home', bodies);
        expect(t.get('a')?.seat).toBe(true);
        expect(t.get('b')?.seat).toBeFalsy();
    });

    it('hangs decor on back walls and stands the rest on free floor', () => {
        const s = sceneFor(placeOf('home'));
        const loose = placeLoose(s, [
            { furniId: 'decor-1', kind: 'decor', style: 'poster' },
            { furniId: 'decor-2', kind: 'decor', style: 'guitar' },
        ]);
        expect(loose.map((p) => p.p)).toEqual(['poster', 'guitar']);
        const [poster, guitar] = loose;
        const behind =
            poster.rot === 0
                ? s.cells[poster.at[1] - 1][poster.at[0]]
                : s.cells[poster.at[1]][poster.at[0] - 1];
        expect(behind.ground).toBe('wall');
        expect(s.blocked.has(key({ x: guitar.at[0], y: guitar.at[1] }))).toBe(
            false,
        );
    });

    it('draws a room for a Place nobody authored, a prop per spot', () => {
        const place: Place = {
            id: 'x',
            name: 'the library',
            kind: 'public',
            spots: [
                { id: 'shelves', name: 'the bookshelves' },
                { id: 'sofa', name: 'the reading sofa', seat: true },
                { id: 'desk', name: 'the front desk' },
            ],
        };
        const s = parseScene(generateScene(place), PREFABS);
        expect(Object.keys(s.spots)).toEqual(['shelves', 'sofa', 'desk']);
        expect(s.props.map((p) => p.p)).toEqual(['books', 'sofa', 'desk']);
        expect(s.spots.sofa.seats.length).toBe(2);
    });

    it('rejects a map char nobody defined', () => {
        expect(() =>
            parseScene({ id: 'bad', map: '..?..', spots: {} }, PREFABS),
        ).toThrow(/unknown map char/);
    });
});

describe('prefab catalogue', () => {
    it('matches backend/World/Places/prefabs.json, which the place designer prompts with', async () => {
        const { readFileSync } = await import('node:fs');
        const { resolve } = await import('node:path');
        const catalogue: {
            name: string;
            size: number[];
            solid: boolean;
            wall: boolean;
            seats: number;
        }[] = JSON.parse(
            readFileSync(
                resolve(
                    __dirname,
                    '../../../../../backend/World/Places/prefabs.json',
                ),
                'utf8',
            ),
        );
        const mine = Object.entries(PREFABS).map(([name, p]) => ({
            name,
            size: p.size ?? [1, 1],
            solid: p.solid !== false,
            wall: !!p.wall,
            seats: (p.seats ?? []).length,
        }));
        expect(
            catalogue.map(({ name, size, solid, wall, seats }) => ({
                name,
                size,
                solid,
                wall,
                seats,
            })),
        ).toEqual(mine);
    });
});
