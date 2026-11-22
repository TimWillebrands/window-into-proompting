import { useQuery, useQueryClient } from '@tanstack/react-query';
import {
    getWorldsPartyIdNotifications,
    postWorldsPartyIdNotificationsDismiss,
    postWorldsPartyIdNotificationsRead,
} from '#api/party-zone';
import type { NotificationFeed } from '../api/model/notificationFeed';
import type { PlayerNotification } from '../api/model/playerNotification';

export type { PlayerNotification };

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

export function tabTitle(base: string, unread: number) {
    const bare = base.replace(/^\(\d+\)\s+/, '');
    return unread > 0 ? `(${unread}) ${bare}` : bare;
}

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

type Change = { ids?: string[]; all?: boolean };

function useFeedChange(
    worldId: string,
    local: (n: PlayerNotification, hit: boolean) => PlayerNotification | null,
    send: (
        worldId: string,
        change: Change,
    ) => Promise<{ data: NotificationFeed }>,
) {
    const queryClient = useQueryClient();
    return async (change: Change) => {
        if (!change.all && !change.ids?.length) return;
        const key = notificationsKey(worldId);
        const before = queryClient.getQueryData<NotificationFeed>(key);
        if (before) {
            const ids = new Set(change.ids ?? []);
            const items = before.items
                .map((n) => local(n, !!change.all || ids.has(n.id)))
                .filter((n): n is PlayerNotification => n !== null);
            queryClient.setQueryData<NotificationFeed>(key, {
                ...before,
                items,
                unread: items.filter((n) => n.unread).length,
            });
        }
        try {
            queryClient.setQueryData(key, (await send(worldId, change)).data);
        } catch {
            void queryClient.invalidateQueries({ queryKey: key });
        }
    };
}

const asRequest = (change: Change) => ({
    ids: change.ids ?? null,
    all: change.all ?? false,
});

export function useMarkNotificationsRead(worldId: string) {
    return useFeedChange(
        worldId,
        (n, hit) => (hit ? { ...n, unread: false } : n),
        (id, change) =>
            postWorldsPartyIdNotificationsRead(id, asRequest(change)),
    );
}

export function useDismissNotifications(worldId: string) {
    return useFeedChange(
        worldId,
        (n, hit) => (hit ? null : n),
        (id, change) =>
            postWorldsPartyIdNotificationsDismiss(id, asRequest(change)),
    );
}
