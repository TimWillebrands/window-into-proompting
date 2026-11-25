import type { IconName } from '@partytown/ui';
import type { Body } from '../../api/model/body';
import type { FurniView } from '../../api/model/furniView';
import type { GrowthRow } from '../../api/model/growthRow';
import type { Place } from '../../api/model/place';
import { AVATAR_COLOURS } from './iso/appearance';

export const toMs = (v: number | string | null | undefined) =>
    v == null ? 0 : Number(v);

let skew = 0;
export const setServerClock = (server: number, receivedAt: number) => {
    skew = server - receivedAt;
};
export const serverNow = () => Date.now() + skew;

export const bodyPlaceId = (b: Body) => b.destPlaceId ?? b.placeId;

export const bodySpotId = (b: Body) => b.destSpotId ?? b.spotId;

export const bodiesIn = (bodies: Body[], placeId: string | undefined) =>
    bodies.filter((b) => bodyPlaceId(b) === placeId);

export const livesIn = (place: Place, who: string | null) =>
    !!who &&
    place.kind === 'home' &&
    ((place.ownerId === who && !place.vacated) ||
        (place.housemates ?? []).includes(who));

export const workplaceDoor = (place: Place) =>
    place.kind === 'public' &&
    !place.private &&
    (place.access === 'staff' || place.access === 'appointment') &&
    (place.staff ?? []).length > 0;

export const hosts = (place: Place, who: string | null) =>
    livesIn(place, who) ||
    (!!place.private && place.madeBy === who) ||
    (!!who && workplaceDoor(place) && (place.staff ?? []).includes(who));

export const placeLabel = (
    place: Place | undefined,
    viewerId: string | null,
) =>
    !place
        ? 'somewhere'
        : livesIn(place, viewerId)
          ? 'Home'
          : (place.name ?? '')
                .replace(/^the /, '')
                .replace(/^./, (c) => c.toUpperCase());

export const mayEnter = (place: Place, viewerId: string | null) =>
    (place.kind !== 'home' && !place.private && !workplaceDoor(place)) ||
    hosts(place, viewerId) ||
    (!!viewerId && (place.keyholders ?? []).includes(viewerId)) ||
    (!!viewerId && (place.guests ?? []).includes(viewerId)) ||
    (!!viewerId && (place.allowed ?? []).includes(viewerId));

export function walkingText(b: Body, now: number): string | null {
    if (!b.destPlaceId) return null;
    const left = Math.max(0, Math.ceil((toMs(b.arriveAt) - now) / 1000));
    return left === 0 ? 'arriving…' : `arriving in ${left}s`;
}

export const isLocale = (p: Place) => p.kind === 'locale';

export function awayLines(
    bodies: Body[],
    places: Place[],
): { participantId: string; text: string }[] {
    const locales = new Map(
        places.filter(isLocale).map((l) => [l.id, l] as const),
    );
    return bodies.flatMap((b) => {
        const at = locales.get(bodyPlaceId(b) ?? '');
        if (!at) return [];
        const where = at.name?.endsWith("'s work") ? '' : `, ${at.name}`;
        return [
            {
                participantId: b.participantId ?? '',
                text: `${b.name} — ${b.awayAs ?? 'away'}${where}`,
            },
        ];
    });
}

export function growingSummary(row: GrowthRow, openAt: number) {
    const footfall = Number(row.footfall);
    const ready = !row.missing;
    return {
        name: row.district ? `${row.name} (${row.district})` : row.name,
        share: Math.min(1, footfall / openAt),
        ready,
        note: ready
            ? 'would open'
            : `${Number(row.people)}p · ${Number(row.days)}d · ${row.missing}`,
        who: row.contributors.join(', '),
    };
}

export const navigatorPlaces = (places: Place[], viewerId: string | null) => {
    const rank = (p: Place) =>
        p.kind === 'public' ? 0 : livesIn(p, viewerId) ? 1 : 2;
    return places.filter((p) => !p.vacated).sort((a, b) => rank(a) - rank(b));
};

export function avatarColour(id: string | undefined): string {
    let h = 0;
    for (const c of id ?? '') h = (h * 31 + c.charCodeAt(0)) | 0;
    return AVATAR_COLOURS[Math.abs(h) % AVATAR_COLOURS.length];
}

export const postureIcon = (b: Body): IconName =>
    b.destPlaceId
        ? 'walk'
        : b.asleep
          ? 'sleep'
          : b.posture === 'Sitting'
            ? 'chair'
            : 'standing';

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

export type Heard = { heardBy?: string[] | null; volume?: string | null };

export const volumeOf = (m: object) => (m as Heard).volume ?? 'talk';

export const canHear = (
    m: object,
    viewerId: string | null,
    allEars = false,
) => {
    const heard = (m as Heard).heardBy;
    return allEars || !heard || (!!viewerId && heard.includes(viewerId));
};

export type EarshotRun<M> =
    | { heard: true; line: M }
    | { heard: false; lines: M[] };

export function earshotRuns<M extends object>(
    lines: M[],
    viewerId: string | null,
    allEars = false,
): EarshotRun<M>[] {
    const out: EarshotRun<M>[] = [];
    for (const m of lines) {
        const last = out.at(-1);
        if (canHear(m, viewerId, allEars)) out.push({ heard: true, line: m });
        else if (last && !last.heard) last.lines.push(m);
        else out.push({ heard: false, lines: [m] });
    }
    return out;
}

export const scribble = (text: string | null | undefined) =>
    (text ?? '')
        .split(/\s+/)
        .filter(Boolean)
        .slice(0, 10)
        .map((w) =>
            '~'.repeat(Math.min(5, Math.max(2, Math.ceil(w.length / 2)))),
        )
        .join(' ');

export function placeIcon(place: Place, viewerId: string | null): IconName {
    if (place.kind === 'home')
        return livesIn(place, viewerId)
            ? 'home'
            : mayEnter(place, viewerId)
              ? 'house'
              : 'locked';
    const scene: Record<string, IconName> = {
        cafe: 'coffee',
        lobby: 'reception',
        centre: 'tree',
    };
    if (workplaceDoor(place) && !mayEnter(place, viewerId)) return 'locked';
    if (place.scene && scene[place.scene]) return scene[place.scene];
    const name = (place.name ?? '').toLowerCase();
    const cues: [RegExp, IconName][] = [
        [/record|vinyl|music/, 'disc'],
        [/bar|pub|club|lounge/, 'cocktail'],
        [/shop|store|market|boutique/, 'shopping'],
        [/park|garden|square|beach/, 'tree'],
        [/library|book/, 'books'],
        [/gym|pool|sport/, 'swim'],
        [/arcade|game/, 'joystick'],
        [/restaurant|diner|kitchen|bakery/, 'dining'],
        [/roof|terrace/, 'skyline'],
    ];
    return (
        cues.find(([re]) => re.test(name))?.[1] ??
        (place.customScene ? 'sparkle' : 'office')
    );
}

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
