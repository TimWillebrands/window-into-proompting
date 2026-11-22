import {
    type DesktopNotification,
    SOURCES,
} from '../../../lib/notification-center';

export const NOW = Date.UTC(2026, 10, 22, 20, 0);
const MIN = 60_000;

const n = (
    id: string,
    source: keyof typeof SOURCES,
    minutesAgo: number,
    title: string,
    body: string,
    unread = false,
): DesktopNotification => ({
    id,
    source: SOURCES[source],
    at: NOW - minutesAgo * MIN,
    title,
    body,
    unread,
});

export const SAMPLE: DesktopNotification[] = [
    n(
        'dm1',
        'messenger',
        0,
        'Ana sent you 2 messages',
        'are you coming to the pub quiz tonight?',
        true,
    ),
    n(
        'dm2',
        'messenger',
        34,
        'Bo sent you a message',
        'found your scarf on the bench by the pond',
        true,
    ),
    n(
        'dm3',
        'messenger',
        120,
        'Cas sent you a message',
        'thanks for yesterday :)',
    ),
    n(
        'b1',
        'board',
        12,
        'Bo and Ana replied on your post',
        'the bakery on the corner does them better',
        true,
    ),
    n(
        'b2',
        'board',
        90,
        'Cas gave you Respect!',
        'first rain of the autumn and I forgot my coat',
    ),
    n(
        'c1',
        'calendar',
        200,
        '“Pub quiz” is starting',
        "At The Anchor. You said you'd be there.",
    ),
    n(
        'g1',
        'wardrobe',
        1500,
        'Denise gave you a present',
        "Red scarf. It's in your Wardrobe.",
    ),
];
