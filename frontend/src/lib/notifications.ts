import { useQuery, useQueryClient } from '@tanstack/react-query';
import { create } from 'zustand';
import {
    getWorldsPartyIdNotifications,
    postWorldsPartyIdNotificationsRead,
} from '#api/party-zone';
import type { NotificationFeed } from '../api/model/notificationFeed';
import type { PlayerNotification } from '../api/model/playerNotification';

export type { PlayerNotification };

export const NOTIFICATION_KINDS = {
    dm: 'dm',
    boardReply: 'board-reply',
    boardMention: 'board-mention',
    respect: 'respect',
    gathering: 'gathering',
    gift: 'gift',
} as const;

const ICONS: Record<string, string> = {
    dm: '💬',
    'board-reply': '📌',
    'board-mention': '📣',
    respect: '★',
    gathering: '📅',
    gift: '🎁',
};

export const iconFor = (kind: string) => ICONS[kind] ?? 'ℹ️';

export const atOf = (n: Pick<PlayerNotification, 'at'>) => Number(n.at ?? 0);

export function targetWindowId(n: PlayerNotification): string {
    if (n.app === 'messenger' && n.targetId) return `dm-${n.targetId}`;
    return n.app;
}

export function arrivals(
    known: ReadonlySet<string>,
    items: readonly PlayerNotification[],
): PlayerNotification[] {
    return items.filter((n) => n.unread && !known.has(n.id));
}

export interface Balloon {
    id: string;
    title: string;
    body: string;
    icon: string;
    items: PlayerNotification[];
}

const SUMMARY_LINES = 3;

export function balloonFor(
    arrived: readonly PlayerNotification[],
    away: boolean,
): Balloon | null {
    if (arrived.length === 0) return null;
    const newest = [...arrived].sort((a, b) => atOf(b) - atOf(a));
    if (newest.length === 1) {
        const [n] = newest;
        return {
            id: n.id,
            title: away ? `While you were away: ${n.title}` : n.title,
            body: n.body,
            icon: iconFor(n.kind),
            items: newest,
        };
    }
    const lines = newest.slice(0, SUMMARY_LINES).map((n) => `• ${n.title}`);
    const more = newest.length - SUMMARY_LINES;
    if (more > 0) lines.push(`…and ${more} more`);
    return {
        id: newest.map((n) => n.id).join('|'),
        title: away
            ? 'While you were away'
            : `${newest.length} new notifications`,
        body: lines.join('\n'),
        icon: 'ℹ️',
        items: newest,
    };
}

export function tabTitle(base: string, unread: number) {
    const bare = base.replace(/^\(\d+\)\s+/, '');
    return unread > 0 ? `(${unread}) ${bare}` : bare;
}

interface BalloonStore {
    queue: Balloon[];
    anchor: HTMLElement | null;
    show: (balloon: Balloon) => void;
    dismiss: (id: string) => void;
    clear: () => void;
    setAnchor: (el: HTMLElement | null) => void;
}

const MAX_QUEUED = 3;

export const useTrayBalloons = create<BalloonStore>((set) => ({
    queue: [],
    anchor: null,
    show: (balloon) =>
        set((s) =>
            s.queue.some((b) => b.id === balloon.id)
                ? s
                : { queue: [...s.queue, balloon].slice(-MAX_QUEUED) },
        ),
    dismiss: (id) =>
        set((s) => ({ queue: s.queue.filter((b) => b.id !== id) })),
    clear: () => set({ queue: [] }),
    setAnchor: (anchor) => set({ anchor }),
}));

export const notificationsKey = (worldId: string) => ['notifications', worldId];

const EMPTY: NotificationFeed = { items: [], seenAt: 0, unread: 0 };

export function useNotifications(worldId: string, enabled: boolean) {
    const { data } = useQuery({
        queryKey: notificationsKey(worldId),
        queryFn: async () =>
            (await getWorldsPartyIdNotifications(worldId)).data,
        enabled,
        refetchInterval: 60_000,
        refetchOnWindowFocus: true,
        staleTime: 5_000,
    });
    const feed = data ?? EMPTY;
    return { feed, unread: Number(feed.unread ?? 0), loaded: !!data };
}

export function useMarkNotificationsRead(worldId: string) {
    const queryClient = useQueryClient();
    return async (what: { ids?: string[]; all?: boolean }) => {
        if (!what.all && !what.ids?.length) return;
        const key = notificationsKey(worldId);
        const before = queryClient.getQueryData<NotificationFeed>(key);
        if (before) {
            const ids = new Set(what.ids ?? []);
            const items = before.items.map((n) =>
                what.all || ids.has(n.id) ? { ...n, unread: false } : n,
            );
            queryClient.setQueryData<NotificationFeed>(key, {
                ...before,
                items,
                unread: items.filter((n) => n.unread).length,
            });
        }
        try {
            const res = await postWorldsPartyIdNotificationsRead(worldId, {
                ids: what.ids ?? null,
                all: what.all ?? false,
            });
            queryClient.setQueryData(key, res.data);
        } catch {
            void queryClient.invalidateQueries({ queryKey: key });
        }
    };
}
