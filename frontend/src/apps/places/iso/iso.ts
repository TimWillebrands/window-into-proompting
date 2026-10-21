/** Isometric projection (Habbo-style 2:1). Tiles are (x, y) on the floor; screen is CSS pixels. */
export const TILE_W = 64;
export const TILE_H = 32;

export interface Pt {
    x: number;
    y: number;
}

/** Top corner of tile (x, y) relative to the room origin. */
export const toScreen = (t: Pt, scale = 1): Pt => ({
    x: ((t.x - t.y) * TILE_W * scale) / 2,
    y: ((t.x + t.y) * TILE_H * scale) / 2,
});

/** Centre of a tile on screen. */
export const tileCentre = (t: Pt, scale = 1): Pt => {
    const s = toScreen(t, scale);
    return { x: s.x, y: s.y + (TILE_H * scale) / 2 };
};

/** Screen point (relative to origin) → the floor tile under it. */
export function toTile(p: Pt, scale = 1): Pt {
    const hw = (TILE_W * scale) / 2;
    const hh = (TILE_H * scale) / 2;
    const fx = (p.x / hw + p.y / hh) / 2;
    const fy = (p.y / hh - p.x / hw) / 2;
    return { x: Math.floor(fx), y: Math.floor(fy) };
}

/** Painter's order: further back (smaller x + y) draws first. */
export const depth = (t: Pt) => t.x + t.y;
