import { describe, expect, it } from 'vitest';
import {
    arrivals,
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
