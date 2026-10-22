import type { Body } from '../../../api/model/body';
import type { Pt } from '../iso/iso';
import { toMs } from '../places-lib';
import { entrance, pathIn, type Scene, type Slot } from './scene';

export const STEP_MS = 280;
/** Share of a between-Places walk spent visible in the Place you leave or arrive in. */
export const DOORWAY = 0.3;

export interface Walk {
    path: Pt[];
    startedAt: number;
}

export interface Pose {
    tile: Pt;
    moving: boolean;
    /** Where it ends up (seat height and facing). */
    slot?: Slot;
    /** Next tile while moving, for facing. */
    toward?: Pt;
}

const sitsDown = (b: Body) =>
    b.posture === 'Sitting' || b.posture === 'Lying' || b.posture === 'Walking';

/** Where each Body in (or walking to a spot in) this Place should end up: seats first for sitters, then standing room. */
export function targets(
    scene: Scene,
    placeId: string | undefined,
    bodies: Body[],
): Map<string, Slot> {
    const bySpot = new Map<string, Body[]>();
    for (const b of bodies) {
        const spot =
            (b.destPlaceId ?? b.placeId) === placeId
                ? (b.destSpotId ?? b.spotId)
                : undefined;
        if (!spot) continue;
        bySpot.set(spot, [...(bySpot.get(spot) ?? []), b]);
    }
    const out = new Map<string, Slot>();
    for (const [spotId, here] of bySpot) {
        const spot = scene.spots[spotId];
        const ordered = [...here].sort((a, b) =>
            (a.participantId ?? '').localeCompare(b.participantId ?? ''),
        );
        if (!spot) {
            for (const b of ordered)
                out.set(b.participantId ?? '', { tile: entrance(scene) });
            continue;
        }
        const seats = [...spot.seats];
        const stands = [...spot.stands];
        for (const b of ordered) {
            const slot = (sitsDown(b) && seats.shift()) ||
                stands.shift() ||
                seats.shift() || { tile: spot.at };
            out.set(b.participantId ?? '', slot);
        }
    }
    return out;
}

/** Where a Body is drawn now: walking within the Place, through the door, or settled on its slot. */
export function poseOf(
    b: Body,
    scene: Scene,
    placeId: string | undefined,
    sceneOf: (placeId: string | null | undefined) => string | undefined,
    walks: Map<string, Walk>,
    shown: Map<string, Pt>,
    slots: Map<string, Slot>,
    now: number,
): Pose | null {
    const id = b.participantId ?? '';
    const leaving =
        b.destPlaceId && b.placeId === placeId && b.destPlaceId !== placeId;
    const arriving =
        b.destPlaceId && b.destPlaceId === placeId && b.placeId !== placeId;
    if (leaving || arriving) {
        const start = toMs(b.departedAt);
        const end = toMs(b.arriveAt);
        const p = end > start ? (now - start) / (end - start) : 1;
        if (leaving && p < DOORWAY) {
            const from = shown.get(id) ?? entrance(scene);
            const door = entrance(scene, sceneOf(b.destPlaceId), b.destPlaceId);
            const path = pathIn(scene, round(from), door);
            return along(path, p / DOORWAY);
        }
        if (arriving && p > 1 - DOORWAY) {
            const door = entrance(scene, sceneOf(b.placeId), b.placeId);
            const slot = slots.get(id);
            const path = pathIn(scene, door, round(slot?.tile ?? door));
            return { ...along(path, (p - (1 - DOORWAY)) / DOORWAY), slot };
        }
        return null;
    }
    if ((b.destPlaceId ?? b.placeId) !== placeId) return null;
    const slot = slots.get(id);
    const walk = walks.get(id);
    if (walk) {
        const total = Math.max(1, walk.path.length - 1) * STEP_MS;
        const t = (now - walk.startedAt) / total;
        if (t < 1) return { ...along(walk.path, t), slot };
    }
    return {
        tile: slot?.tile ?? shown.get(id) ?? entrance(scene),
        moving: false,
        slot,
    };
}

export const round = (p: Pt): Pt => ({
    x: Math.round(p.x),
    y: Math.round(p.y),
});

/** Position along a tile path at t ∈ [0, 1], and the tile it's heading for. */
export function along(path: Pt[], t: number): Pose {
    if (path.length === 0) return { tile: { x: 0, y: 0 }, moving: false };
    if (path.length === 1 || t >= 1)
        return { tile: path[path.length - 1], moving: t < 1 };
    const f = Math.max(0, t) * (path.length - 1);
    const i = Math.floor(f);
    const a = path[i];
    const b = path[i + 1];
    return {
        tile: {
            x: a.x + (b.x - a.x) * (f - i),
            y: a.y + (b.y - a.y) * (f - i),
        },
        moving: true,
        toward: b,
    };
}
