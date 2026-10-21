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
    place.kind !== 'home' ||
    place.ownerId === viewerId ||
    (!!viewerId && (place.guests ?? []).includes(viewerId));

/** "arriving in 42s" while walking; null when settled. */
export function walkingText(b: Body, now: number): string | null {
    if (!b.destPlaceId) return null;
    const left = Math.max(0, Math.ceil((toMs(b.arriveAt) - now) / 1000));
    return left === 0 ? 'arriving…' : `arriving in ${left}s`;
}

/** Ordered for the Navigator: public Places first, then the viewer's Home. */
export const navigatorPlaces = (places: Place[], viewerId: string | null) => {
    const rank = (p: Place) =>
        p.kind === 'public' ? 0 : p.ownerId === viewerId ? 1 : 2;
    return [...places].sort((a, b) => rank(a) - rank(b));
};

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
    b.destPlaceId
        ? '🚶'
        : b.asleep
          ? '💤'
          : b.posture === 'Sitting'
            ? '🪑'
            : '🧍';

const DECOR: Record<string, string> = {
    poster: '📜',
    painting: '🖼️',
    photo: '📷',
    plant: '🌿',
    rug: '🟫',
    lamp: '🕯️',
    books: '📚',
    guitar: '🎸',
};

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
        case 'decor':
            return DECOR[f.style ?? ''] ?? '🏺';
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

/** A Place's icon in the Navigator and on its nameplate, from its scene or what it's called. */
export function placeIcon(place: Place, viewerId: string | null): string {
    if (place.kind === 'home')
        return place.ownerId === viewerId
            ? '🏠'
            : mayEnter(place, viewerId)
              ? '🏡'
              : '🔒';
    const scene: Record<string, string> = {
        cafe: '☕',
        lobby: '🛎️',
        centre: '🌳',
    };
    if (place.scene && scene[place.scene]) return scene[place.scene];
    const name = (place.name ?? '').toLowerCase();
    const cues: [RegExp, string][] = [
        [/record|vinyl|music/, '💿'],
        [/bar|pub|club|lounge/, '🍸'],
        [/shop|store|market|boutique/, '🛍️'],
        [/park|garden|square|beach/, '🌳'],
        [/library|book/, '📚'],
        [/gym|pool|sport/, '🏊'],
        [/arcade|game/, '🕹️'],
        [/restaurant|diner|kitchen|bakery/, '🍽️'],
        [/roof|terrace/, '🌆'],
    ];
    return (
        cues.find(([re]) => re.test(name))?.[1] ??
        (place.customScene ? '✨' : '🏢')
    );
}

/** Habbo navigator occupancy: how full a Place is, for the bar's colour. */
export function occupancy(
    place: Place,
    count: number,
): { share: number; level: 'calm' | 'busy' | 'full' } {
    const capacity = Math.max(
        1,
        (place.spots ?? []).reduce((n, s) => n + Number(s.capacity ?? 1), 0),
    );
    const share = Math.min(1, count / capacity);
    return {
        share,
        level: share >= 0.8 ? 'full' : share >= 0.45 ? 'busy' : 'calm',
    };
}
