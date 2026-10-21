import type { Body } from '../../api/model/body';
import type { FurniView } from '../../api/model/furniView';
import type { Place } from '../../api/model/place';

export const toMs = (v: number | string | null | undefined) =>
    v == null ? 0 : Number(v);

/** Where a Body counts as being: its destination once it's heading somewhere. */
export const bodyPlaceId = (b: Body) => b.destPlaceId ?? b.placeId;

export const bodySpotId = (b: Body) => b.destSpotId ?? b.spotId;

/** Bodies at (or on their way to) a Place. */
export const bodiesIn = (bodies: Body[], placeId: string | undefined) =>
    bodies.filter((b) => bodyPlaceId(b) === placeId);

export const placeLabel = (
    place: Place | undefined,
    viewerId: string | null,
) =>
    !place
        ? 'somewhere'
        : place.ownerId && place.ownerId === viewerId
          ? 'Home'
          : (place.name ?? '')
                .replace(/^the /, '')
                .replace(/^./, (c) => c.toUpperCase());

/** Others' Homes are private in phase 1. */
export const mayEnter = (place: Place, viewerId: string | null) =>
    place.kind !== 'home' || place.ownerId === viewerId;

/** "arriving in 42s" while walking; null when settled. */
export function walkingText(b: Body, now: number): string | null {
    if (!b.destPlaceId) return null;
    const left = Math.max(0, Math.ceil((toMs(b.arriveAt) - now) / 1000));
    return left === 0 ? 'arriving…' : `arriving in ${left}s`;
}

/** Ordered for the Navigator: public Places first, then the viewer's Home. */
export const navigatorPlaces = (places: Place[], viewerId: string | null) =>
    places
        .filter((p) => mayEnter(p, viewerId) || p.kind === 'public')
        .sort((a, b) => (a.kind === b.kind ? 0 : a.kind === 'public' ? -1 : 1));

const AVATAR_COLOURS = [
    '#e57373',
    '#64b5f6',
    '#81c784',
    '#ffb74d',
    '#ba68c8',
    '#4db6ac',
    '#f06292',
    '#a1887f',
];

/** Stable colour per Participant until real Appearances exist. */
export function avatarColour(id: string | undefined): string {
    let h = 0;
    for (const c of id ?? '') h = (h * 31 + c.charCodeAt(0)) | 0;
    return AVATAR_COLOURS[Math.abs(h) % AVATAR_COLOURS.length];
}

export const postureIcon = (b: Body) =>
    b.destPlaceId ? '🚶' : b.posture === 'Sitting' ? '🪑' : '🧍';

/** Icon per furni kind, worse-looking when it needs care. */
export function furniIcon(f: FurniView): string {
    const bad = Number(f.condition ?? 1) < 0.35;
    switch (f.kind) {
        case 'plant':
            return bad ? '🥀' : '🪴';
        case 'cups':
            return '🍽️';
        case 'coffee':
            return bad ? '⚠️' : '☕';
        case 'jukebox':
            return f.on ? '🎶' : '📻';
        case 'lamp':
            return bad ? '⚡' : '💡';
        case 'laptop':
            return '💻';
        case 'bed':
            return '🛏️';
        default:
            return '📦';
    }
}

/** Place talk carries its Earshot; null heardBy means everyone heard (Rooms, DMs, old lines). */
export type Heard = { heardBy?: string[] | null; volume?: string | null };

export const volumeOf = (m: object) => (m as Heard).volume ?? 'talk';

/** Whether the viewer heard a line; the author's all-ears switch hears everything. */
export const canHear = (
    m: object,
    viewerId: string | null,
    allEars = false,
) => {
    const heard = (m as Heard).heardBy;
    return allEars || !heard || (!!viewerId && heard.includes(viewerId));
};

/** The shape of talk you can't make out: same rhythm, no words. */
export const scribble = (text: string | null | undefined) =>
    (text ?? '')
        .split(/\s+/)
        .filter(Boolean)
        .slice(0, 10)
        .map((w) =>
            '~'.repeat(Math.min(5, Math.max(2, Math.ceil(w.length / 2)))),
        )
        .join(' ');
