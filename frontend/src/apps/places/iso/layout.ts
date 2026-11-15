import type { Pt } from './iso';

export interface SpotLayout {
    anchor: Pt;
    slots: Pt[];
}

export interface RoomLayout {
    w: number;
    h: number;
    door: Pt;
    spots: Record<string, SpotLayout>;
}

const CENTRES: Pt[] = [
    { x: 2, y: 2 },
    { x: 7, y: 2 },
    { x: 2, y: 6 },
    { x: 7, y: 6 },
    { x: 5, y: 4 },
    { x: 4, y: 1 },
];

const RING: Pt[] = [
    { x: 1, y: 0 },
    { x: 0, y: 1 },
    { x: -1, y: 0 },
    { x: 0, y: -1 },
    { x: 1, y: 1 },
    { x: -1, y: 1 },
    { x: 1, y: -1 },
    { x: -1, y: -1 },
];

export function layoutFor(spotIds: string[], w = 10, h = 8): RoomLayout {
    const spots: Record<string, SpotLayout> = {};
    spotIds.slice(0, CENTRES.length).forEach((id, i) => {
        const anchor = CENTRES[i];
        spots[id] = {
            anchor,
            slots: RING.map((d) => ({
                x: anchor.x + d.x,
                y: anchor.y + d.y,
            })).filter((p) => p.x >= 0 && p.y >= 0 && p.x < w && p.y < h),
        };
    });
    return { w, h, door: { x: 0, y: Math.floor(h / 2) }, spots };
}

export function slotTile(
    layout: RoomLayout,
    spotId: string | undefined,
    index: number,
): Pt {
    const spot = spotId ? layout.spots[spotId] : undefined;
    if (!spot || spot.slots.length === 0) return layout.door;
    return spot.slots[index % spot.slots.length];
}

export const blocked = (layout: RoomLayout) =>
    new Set(
        Object.values(layout.spots).map((s) => `${s.anchor.x},${s.anchor.y}`),
    );
