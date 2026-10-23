import { describe, expect, it } from 'vitest';
import type { BoardThread } from '../../api/model/boardThread';
import {
    bumpedBy,
    involvesPlayer,
    playerActivityAt,
    relativeTime,
    remaining,
    respectLabel,
    sortFeed,
} from './board-lib';

const ME = 'me';

const thread = (over: Partial<BoardThread>): BoardThread => ({
    id: 't',
    authorId: 'ana',
    authorName: 'Ana',
    body: 'hello',
    createdAt: 100,
    lastActivityAt: 100,
    replies: [],
    respect: [],
    ...over,
});

describe('board-lib', () => {
    it('orders the feed by latest activity, so replies bump a post', () => {
        const old = thread({ id: 'old', createdAt: 1, lastActivityAt: 500 });
        const fresh = thread({
            id: 'fresh',
            createdAt: 300,
            lastActivityAt: 300,
        });
        expect(sortFeed([fresh, old]).map((t) => t.id)).toEqual([
            'old',
            'fresh',
        ]);
    });

    it('names the reply that bumped a post', () => {
        const t = thread({
            replies: [
                { id: 'r', authorId: 'bo', authorName: 'Bo', createdAt: 200 },
            ],
        });
        expect(bumpedBy(t)?.authorName).toBe('Bo');
        expect(bumpedBy(thread({}))).toBeUndefined();
    });

    it('lists who gave Respect!, with you last-named as "you"', () => {
        expect(respectLabel([], ME)).toBe('Give Respect!');
        expect(
            respectLabel(
                [
                    { giverId: 'ana', giverName: 'Ana', at: 1 },
                    { giverId: 'bo', giverName: 'Bo', at: 2 },
                    { giverId: ME, giverName: 'Tim', at: 3 },
                ],
                ME,
            ),
        ).toBe('Respect! from Ana, Bo and you');
    });

    it('finds posts that involve the Player by authorship, reply or name', () => {
        expect(involvesPlayer(thread({ authorId: ME }), ME, 'Tim')).toBe(true);
        expect(
            involvesPlayer(thread({ body: 'what does tim think?' }), ME, 'Tim'),
        ).toBe(true);
        expect(
            involvesPlayer(
                thread({ replies: [{ authorId: ME, body: 'hi' }] }),
                ME,
                'Tim',
            ),
        ).toBe(true);
        expect(involvesPlayer(thread({}), ME, 'Tim')).toBe(false);
    });

    it('dates activity on your own lines: replies under your post and Respect! from others', () => {
        const t = thread({
            authorId: ME,
            respect: [{ giverId: 'ana', giverName: 'Ana', at: 400 }],
            replies: [{ authorId: 'bo', createdAt: 300 }],
        });
        expect(playerActivityAt(t, ME)).toBe(400);
        expect(playerActivityAt(thread({}), ME)).toBe(0);
    });

    it('counts down to the 280 cap on trimmed text', () => {
        expect(remaining('  hi  ')).toBe(278);
        expect(remaining('x'.repeat(300))).toBe(-20);
    });

    it('uses short feed times', () => {
        const now = 10 * 60 * 60 * 1000;
        expect(relativeTime(now - 10_000, now)).toBe('just now');
        expect(relativeTime(now - 5 * 60_000, now)).toBe('5m');
        expect(relativeTime(now - 3 * 3_600_000, now)).toBe('3h');
    });
});
