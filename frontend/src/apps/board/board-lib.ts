import { useEffect, useState } from 'react';
import { create } from 'zustand';
import { persist } from 'zustand/middleware';
import type { BoardReply } from '../../api/model/boardReply';
import type { BoardRespect } from '../../api/model/boardRespect';
import type { BoardThread } from '../../api/model/boardThread';

/** Post and reply length cap, enforced by the backend too. */
export const MAX_BODY = 280;

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

/** Feed-style short times: "just now", "5m", "3h", "yesterday", "4d", then a date. */
export function relativeTime(at: number, now: number) {
    const s = Math.max(0, Math.round((now - at) / 1000));
    if (s < 45) return 'just now';
    const m = Math.round(s / 60);
    if (m < 60) return `${m}m`;
    const h = Math.round(m / 60);
    if (h < 24) return `${h}h`;
    const d = Math.round(h / 24);
    if (d === 1) return 'yesterday';
    if (d < 7) return `${d}d`;
    return new Date(at).toLocaleDateString('en-GB', {
        day: 'numeric',
        month: 'short',
    });
}

/** Tooltip date: "Wed 21 Oct 2026, 15:04". */
export function fullDate(at: number) {
    return new Date(at).toLocaleString('en-GB', {
        weekday: 'short',
        day: 'numeric',
        month: 'short',
        year: 'numeric',
        hour: '2-digit',
        minute: '2-digit',
    });
}

/** Feed order: newest activity first, so a post with fresh replies comes back up. */
export const sortFeed = (threads: BoardThread[]) =>
    [...threads].sort((a, b) => ms(b.lastActivityAt) - ms(a.lastActivityAt));

/** Why a bumped post is back on top: its newest reply, when that's newer than the post. */
export function bumpedBy(t: BoardThread) {
    const last = t.replies?.[t.replies.length - 1];
    return last && ms(last.createdAt) > ms(t.createdAt) ? last : undefined;
}

export const hasRespectFrom = (
    respect: BoardRespect[] | undefined,
    id: string | null,
) => !!id && (respect ?? []).some((r) => r.giverId === id);

/** "Respect! from Denise, Pieter and you". */
export function respectLabel(
    respect: BoardRespect[] | undefined,
    playerId: string | null,
) {
    const names = (respect ?? []).map((r) =>
        r.giverId === playerId ? 'you' : (r.giverName ?? '?'),
    );
    if (names.length === 0) return 'Give Respect!';
    const list =
        names.length === 1
            ? names[0]
            : `${names.slice(0, -1).join(', ')} and ${names[names.length - 1]}`;
    return `Respect! from ${list}`;
}

/** The Player took part: wrote the post, replied under it, or is named in it. */
export function involvesPlayer(
    t: BoardThread,
    playerId: string | null,
    playerName: string | null,
) {
    if (!playerId) return false;
    const named = (s?: string) =>
        !!playerName &&
        !!s &&
        s.toLowerCase().includes(playerName.split(' ')[0].toLowerCase());
    return (
        t.authorId === playerId ||
        named(t.body) ||
        (t.replies ?? []).some((r) => r.authorId === playerId || named(r.body))
    );
}

/** Newest moment anything happened to the Player's own lines: a reply under their post or a Respect!. */
export function playerActivityAt(t: BoardThread, playerId: string | null) {
    if (!playerId) return 0;
    const own = (lines: { authorId?: string; respect?: BoardRespect[] }[]) =>
        lines
            .filter((l) => l.authorId === playerId)
            .flatMap((l) => l.respect ?? [])
            .filter((r) => r.giverId !== playerId)
            .map((r) => ms(r.at));
    const replies =
        t.authorId === playerId
            ? (t.replies ?? [])
                  .filter((r) => r.authorId !== playerId)
                  .map((r) => ms(r.createdAt))
            : [];
    return Math.max(0, ...own([t]), ...own(t.replies ?? []), ...replies);
}

/** Lines by others newer than the cursor: what to highlight as new. */
export const isNewLine = (
    line: Pick<BoardReply, 'authorId' | 'createdAt'>,
    playerId: string | null,
    since: number,
) => line.authorId !== playerId && ms(line.createdAt) > since;

interface SeenStore {
    /** When the Player last caught up; newer lines by others show as new. */
    since: number;
    catchUp: (at: number) => void;
}

/** Per-browser "last caught up" cursor. */
export const useBoardSeen = create<SeenStore>()(
    persist(
        (set) => ({
            since: Date.now(),
            catchUp: (at) => set((s) => (at > s.since ? { since: at } : s)),
        }),
        { name: 'partyboard-seen-v2' },
    ),
);

/** Count down to the cap; negative means over. */
export const remaining = (text: string) => MAX_BODY - text.trim().length;
