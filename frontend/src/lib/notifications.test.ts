import { describe, expect, it } from 'vitest';
import {
    arrivals,
    balloonFor,
    type PlayerNotification,
    tabTitle,
    targetWindowId,
} from './notifications';

const n = (
    id: string,
    at: number,
    extra: Partial<PlayerNotification> = {},
): PlayerNotification => ({
    id,
    kind: 'dm',
    at,
    title: `title ${id}`,
    body: `body ${id}`,
    app: 'messenger',
    targetId: `chat-${id}`,
    unread: true,
    ...extra,
});

describe('arrivals', () => {
    it('are unread items not seen before', () => {
        const known = new Set(['a']);
        const found = arrivals(known, [
            n('a', 1),
            n('b', 2),
            n('c', 3, { unread: false }),
        ]);
        expect(found.map((x) => x.id)).toEqual(['b']);
    });
});

describe('balloonFor', () => {
    it('shows a single arrival as itself', () => {
        const b = balloonFor([n('a', 1)], false);
        expect(b).toMatchObject({
            title: 'title a',
            body: 'body a',
            icon: '💬',
        });
    });

    it('says while you were away for one that came in while gone', () => {
        expect(balloonFor([n('a', 1)], true)?.title).toBe(
            'While you were away: title a',
        );
    });

    it('sums up several, newest first, three lines and the rest counted', () => {
        const b = balloonFor(
            [n('a', 1), n('b', 4), n('c', 3), n('d', 2), n('e', 0)],
            true,
        );
        expect(b?.title).toBe('While you were away');
        expect(b?.body).toBe('• title b\n• title c\n• title d\n…and 2 more');
        expect(b?.items).toHaveLength(5);
    });

    it('counts several that came in during play', () => {
        expect(balloonFor([n('a', 1), n('b', 2)], false)?.title).toBe(
            '2 new notifications',
        );
    });

    it('is nothing for nothing', () => {
        expect(balloonFor([], true)).toBeNull();
    });
});

describe('targetWindowId', () => {
    it('is the DM window for a message, else the app', () => {
        expect(targetWindowId(n('a', 1))).toBe('dm-chat-a');
        expect(targetWindowId(n('b', 1, { app: 'board' }))).toBe('board');
    });
});

describe('tabTitle', () => {
    it('prefixes the unread count once', () => {
        expect(tabTitle('Partytown', 3)).toBe('(3) Partytown');
        expect(tabTitle('(3) Partytown', 5)).toBe('(5) Partytown');
        expect(tabTitle('(5) Partytown', 0)).toBe('Partytown');
    });
});
