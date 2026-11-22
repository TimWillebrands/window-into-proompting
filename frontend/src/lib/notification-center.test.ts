import { beforeEach, describe, expect, it } from 'vitest';
import {
    type DesktopNotification,
    fromWorld,
    groupBySource,
    notify,
    SOURCES,
    sourceFor,
    summaryFor,
    useNotificationCenter,
} from './notification-center';
import type { PlayerNotification } from './notifications';

const world = (
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
    unread: true,
    ...extra,
});

const card = (id: string, at: number, app = 'messenger') =>
    fromWorld(world(id, at, { app }));

describe('sourceFor', () => {
    it('names the app a notification belongs to', () => {
        expect(sourceFor(world('a', 1))).toBe(SOURCES.messenger);
        expect(sourceFor(world('b', 1, { app: 'board' }))).toBe(SOURCES.board);
        expect(
            sourceFor(world('c', 1, { kind: 'gathering', app: 'places' })),
        ).toBe(SOURCES.calendar);
        expect(sourceFor(world('d', 1, { app: 'wardrobe' }))).toBe(
            SOURCES.wardrobe,
        );
    });
});

describe('groupBySource', () => {
    it('stacks per app, newest app first, newest on top, counting new ones', () => {
        const groups = groupBySource([
            card('m1', 10),
            card('b1', 30, 'board'),
            { ...card('m2', 20), unread: false },
            card('b2', 5, 'board'),
        ]);
        expect(groups.map((g) => g.source.id)).toEqual(['board', 'messenger']);
        expect(groups[1].items.map((n) => n.id)).toEqual(['m2', 'm1']);
        expect(groups[1].unread).toBe(1);
    });
});

describe('summaryFor', () => {
    it('shows a lone arrival during play as itself', () => {
        const n = card('a', 1);
        expect(summaryFor([n], false)).toBe(n);
    });

    it('sums up what came in while away, three newest and the rest counted', () => {
        const s = summaryFor(
            [
                card('a', 1),
                card('b', 4),
                card('c', 3),
                card('d', 2),
                card('e', 0),
            ],
            true,
        ) as DesktopNotification;
        expect(s.title).toBe('While you were away');
        expect(s.body).toBe('title b\ntitle c\ntitle d\nand 2 more');
        expect(s.source).toBe(SOURCES.system);
        expect(s.world).toBeUndefined();
    });

    it('is nothing for nothing', () => {
        expect(summaryFor([], true)).toBeNull();
    });
});

describe('the center store', () => {
    beforeEach(() => useNotificationCenter.getState().reset());

    it('notify() files a system notification, shows a banner and announces it', () => {
        notify({ title: 'This World is stopped', body: 'Look around.' });
        const s = useNotificationCenter.getState();
        expect(s.local).toHaveLength(1);
        expect(s.banners[0].title).toBe('This World is stopped');
        expect(s.announce).toBe(
            'Partytown: This World is stopped. Look around.',
        );
    });

    it('sends no banners while the center is open, and opening clears them', () => {
        notify({ title: 'one', body: '' });
        useNotificationCenter.getState().setOpen(true);
        expect(useNotificationCenter.getState().banners).toEqual([]);
        notify({ title: 'two', body: '' });
        expect(useNotificationCenter.getState().banners).toEqual([]);
        expect(useNotificationCenter.getState().local).toHaveLength(2);
    });

    it('keeps at most four banners, newest first', () => {
        for (const t of ['1', '2', '3', '4', '5'])
            notify({ title: t, body: '' });
        expect(
            useNotificationCenter.getState().banners.map((b) => b.title),
        ).toEqual(['5', '4', '3', '2']);
    });

    it('dismissing removes it from the center and the banners', () => {
        const id = notify({ title: 'gone', body: '' });
        useNotificationCenter.getState().dismissLocal([id]);
        const s = useNotificationCenter.getState();
        expect(s.local).toEqual([]);
        expect(s.banners).toEqual([]);
    });
});
