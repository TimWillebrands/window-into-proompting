import { useEffect, useState } from 'react';
import { create } from 'zustand';
import { persist } from 'zustand/middleware';
import type { BoardThread } from '../../api/model/boardThread';

/** Max post body length enforced by the backend. */
export const MAX_BODY = 4000;
export const MAX_TITLE = 140;

/** Unix-ms timestamp from the API's `number | string` long. */
export const ms = (v: number | string | undefined) => Number(v ?? 0);

/** Robohash avatar; the Player gets the human set like D'Accord's user. */
export const avatarUrl = (authorId: string, isPlayer: boolean, size = 64) =>
    `https://robohash.org/${encodeURIComponent(authorId)}.png?size=${size}x${size}${isPlayer ? '&set=set5' : ''}`;

/** Re-render tick so relative times stay fresh. */
export function useNow(intervalMs = 30_000) {
    const [now, setNow] = useState(() => Date.now());
    useEffect(() => {
        const t = window.setInterval(() => setNow(Date.now()), intervalMs);
        return () => window.clearInterval(t);
    }, [intervalMs]);
    return now;
}

export function relativeTime(at: number, now: number) {
    const s = Math.max(0, Math.round((now - at) / 1000));
    if (s < 45) return 'just now';
    const m = Math.round(s / 60);
    if (m < 60) return `${m} min ago`;
    const h = Math.round(m / 60);
    if (h < 24) return `${h} hour${h === 1 ? '' : 's'} ago`;
    const d = Math.round(h / 24);
    if (d === 1) return 'yesterday';
    if (d < 7) return `${d} days ago`;
    return fullDate(at);
}

/** phpBB-style "Wed Oct 21, 2026 3:04 pm". */
export function fullDate(at: number) {
    const d = new Date(at);
    const day = d.toLocaleDateString('en-US', {
        weekday: 'short',
        month: 'short',
        day: 'numeric',
        year: 'numeric',
    });
    const time = d
        .toLocaleTimeString('en-US', { hour: 'numeric', minute: '2-digit' })
        .toLowerCase();
    return `${day.replace(/^(\w+),/, '$1')} ${time}`;
}

export function lastPost(t: BoardThread) {
    const r = t.replies?.[t.replies.length - 1];
    return r
        ? { authorId: r.authorId ?? '', authorName: r.authorName ?? '?' }
        : { authorId: t.authorId ?? '', authorName: t.authorName ?? '?' };
}

/** Post count per author across the whole board. */
export function postCounts(threads: BoardThread[]) {
    const counts = new Map<string, number>();
    const bump = (id?: string) =>
        id && counts.set(id, (counts.get(id) ?? 0) + 1);
    for (const t of threads) {
        bump(t.authorId);
        for (const r of t.replies ?? []) bump(r.authorId);
    }
    return counts;
}

interface SeenStore {
    /** Anything older than this counts as read. */
    since: number;
    seen: Record<string, number>;
    markSeen: (threadId: string, at: number) => void;
    markAllRead: () => void;
}

/** Per-browser read cursors, phpBB "new posts" style. */
export const useBoardSeen = create<SeenStore>()(
    persist(
        (set) => ({
            since: Date.now(),
            seen: {},
            markSeen: (threadId, at) =>
                set((s) =>
                    (s.seen[threadId] ?? 0) >= at
                        ? s
                        : { seen: { ...s.seen, [threadId]: at } },
                ),
            markAllRead: () => set({ since: Date.now(), seen: {} }),
        }),
        { name: 'partyboard-seen' },
    ),
);

export function isUnread(
    t: BoardThread,
    playerId: string | null,
    since: number,
    seen: Record<string, number>,
) {
    if (lastPost(t).authorId === playerId) return false;
    return ms(t.lastActivityAt) > (seen[t.id ?? ''] ?? since);
}

/** Replies by others on a thread the Player started. */
export const repliesToPlayer = (t: BoardThread, playerId: string | null) =>
    playerId && t.authorId === playerId
        ? (t.replies ?? []).filter((r) => r.authorId !== playerId).length
        : 0;
