import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useEffect, useRef } from 'react';
import { create } from 'zustand';
import {
    getGetWorldsPartyIdLivesQueryKey,
    getGetWorldsPartyIdMessengerQueryKey,
    getGetWorldsPartyIdMessengerQueryOptions,
    getGetWorldsPartyIdPlacesQueryKey,
    getGetWorldsPartyIdQueryKey,
    getWorldsPartyIdPlayer,
    postWorldsPartyIdPlayer,
    postWorldsPartyIdPlayerTake,
    putWorldsPartyIdPlayerAura,
    putWorldsPartyIdPlayerAuto,
    useGetWorlds,
} from '#api/party-zone';
import { useDesktopContext } from '#lib/desktop-context';
import { useRealtimeStoreActions } from '#lib/realtime-store';
import { ProblemDetailsError } from '../api/custom-fetch';
import type { Body } from '../api/model/body';
import type { DirectChatSummary } from '../api/model/directChatSummary';
import type { Gathering } from '../api/model/gathering';
import type { LifeSummary } from '../api/model/lifeSummary';
import type { PlacesSnapshot } from '../api/model/placesSnapshot';
import type { WorldActivity } from '../api/model/worldActivity';
import { usePlayerIdle } from '../apps/places/use-player-idle';
import { ROOT_PARTY_ID } from './chat-api';
import {
    type DesktopNotification,
    fromWorld,
    notify,
    useNotificationCenter,
} from './notification-center';
import {
    notificationsKey,
    type PlayerNotification,
    useDismissNotifications,
    useMarkNotificationsRead,
    useNotifications,
} from './notifications';
import { useNotificationWatcher } from './use-notification-watcher';
import { type WorldEnvelope, type WorldEventData, worldBus } from './world-bus';

const CHOSEN_WORLD = 'partytown.world';

const remembered =
    typeof window === 'undefined'
        ? null
        : window.localStorage.getItem(CHOSEN_WORLD);

export let WORLD_ID: string = remembered ?? ROOT_PARTY_ID;

interface WorldChoice {
    id: string | null;
    pickedNow: boolean;
    choose: (id: string) => void;
    leave: () => void;
}

export const useWorldChoice = create<WorldChoice>((set) => ({
    id: remembered,
    pickedNow: false,
    choose: (id) => {
        WORLD_ID = id;
        window.localStorage.setItem(CHOSEN_WORLD, id);
        set({ id, pickedNow: true });
    },
    leave: () => {
        window.localStorage.removeItem(CHOSEN_WORLD);
        set({ id: null });
    },
}));

export function useForgetStoppedWorld(): boolean {
    const { id, pickedNow, leave } = useWorldChoice();
    const check = !!id && !pickedNow;
    const { data } = useGetWorlds({ query: { enabled: check } });
    const world = data?.data.worlds.find((w) => w.id === id);
    const stale = check && !!data && (!world || world.stopped);
    useEffect(() => {
        if (stale) leave();
    }, [stale, leave]);
    return check && (!data || stale);
}

const worldPrefix = (suffix = '') => `/api/worlds/${WORLD_ID}${suffix}`;

export function useWorldEvents(handler: (envelope: WorldEnvelope) => void) {
    const ref = useRef(handler);
    ref.current = handler;
    useEffect(() => worldBus.subscribe((e) => ref.current(e)), []);
}

interface PlayerSession {
    userId: string | null;
    hydrated: boolean;
    visiting: boolean;
    sync: (userId: string | null) => void;
    visit: () => void;
}

export const usePlayerSession = create<PlayerSession>((set) => ({
    userId: null,
    hydrated: false,
    visiting: false,
    sync: (userId) => set({ userId, hydrated: true }),
    visit: () => set({ visiting: true }),
}));

const playerKey = (userId: string | null) => ['world-player', WORLD_ID, userId];

export function usePlayerAccount() {
    const userId = usePlayerSession((s) => s.userId);
    const { data, isLoading } = useQuery({
        queryKey: playerKey(userId),
        queryFn: async () => (await getWorldsPartyIdPlayer(WORLD_ID)).data,
        enabled: !!userId,
        staleTime: Number.POSITIVE_INFINITY,
    });
    return { account: data, isLoading };
}

export function useCreatePlayer() {
    const queryClient = useQueryClient();
    const userId = usePlayerSession((s) => s.userId);
    return async (name: string) => {
        const res = await postWorldsPartyIdPlayer(WORLD_ID, { name });
        queryClient.setQueryData(playerKey(userId), res.data);
    };
}

export function useTakePersona() {
    const queryClient = useQueryClient();
    const userId = usePlayerSession((s) => s.userId);
    return async (personaId: string) => {
        const res = await postWorldsPartyIdPlayerTake(WORLD_ID, { personaId });
        queryClient.setQueryData(playerKey(userId), res.data);
        void queryClient.invalidateQueries({
            queryKey: getGetWorldsPartyIdLivesQueryKey(WORLD_ID),
        });
    };
}

export function useSetAuto() {
    const queryClient = useQueryClient();
    const userId = usePlayerSession((s) => s.userId);
    return async (on: boolean, mission: string) => {
        const res = await putWorldsPartyIdPlayerAuto(WORLD_ID, { on, mission });
        queryClient.setQueryData(playerKey(userId), res.data);
        void queryClient.invalidateQueries({
            queryKey: getGetWorldsPartyIdLivesQueryKey(WORLD_ID),
        });
    };
}

export const AURA_MAX = 64;

export function useSetAura() {
    const queryClient = useQueryClient();
    const userId = usePlayerSession((s) => s.userId);
    return async (aura: string) => {
        const res = await putWorldsPartyIdPlayerAura(WORLD_ID, {
            aura: aura.trim() || null,
        });
        queryClient.setQueryData(playerKey(userId), res.data);
    };
}

export function usePlayerOnAuto(): boolean {
    return usePlayerAccount().account?.auto ?? false;
}

export function usePlayerId(): string | null {
    return (usePlayerAccount().account?.playerId as string | null) ?? null;
}

export function usePlayerName(): string | null {
    return usePlayerAccount().account?.name ?? null;
}

export function useIsAdmin(): boolean {
    return usePlayerAccount().account?.isAdmin ?? false;
}

export function useCanOpen() {
    const signedIn = usePlayerSession((s) => !!s.userId);
    const isAdmin = useIsAdmin();
    return (access?: 'member' | 'admin') =>
        !access || (access === 'admin' ? isAdmin : signedIn);
}

export function notifyOnRefusal(error: unknown) {
    if (!(error instanceof ProblemDetailsError)) return;
    if (error.title === 'This World is stopped.')
        notify({
            title: 'This World is stopped',
            body: 'You can look around, but nothing here changes any more. Switch World from the Start menu.',
        });
    else if (error.title === 'Your character is on auto.')
        notify({
            title: 'Your character is on auto',
            body: 'Take over from the Start menu to act yourself.',
        });
}

export function useOpenDirectChat() {
    const { openWindow } = useDesktopContext();
    return (chatGroupId: string, withName: string) =>
        openWindow('messenger-chat', {
            instanceId: `dm-${chatGroupId}`,
            title: `${withName} — Conversation`,
            props: { chatGroupId, withName },
        });
}

export function useOpenNotification() {
    const { openWindow } = useDesktopContext();
    const openDirectChat = useOpenDirectChat();
    const markRead = useMarkNotificationsRead(WORLD_ID);
    return (n: PlayerNotification) => {
        void markRead({ ids: [n.id] });
        if (n.app === 'messenger' && n.targetId)
            openDirectChat(n.targetId, n.fromName ?? 'Contact');
        else if (n.app === 'board')
            openWindow('board', {
                props: n.targetId ? { threadId: n.targetId } : undefined,
            });
        else openWindow(n.app);
    };
}

export function useCenterItems(): DesktopNotification[] {
    const playerId = usePlayerId();
    const { feed } = useNotifications(WORLD_ID, !!playerId);
    const local = useNotificationCenter((s) => s.local);
    return [...local, ...feed.items.map(fromWorld)].sort((a, b) => b.at - a.at);
}

export function useNotificationActions() {
    const openWorld = useOpenNotification();
    const dismissWorld = useDismissNotifications(WORLD_ID);
    const readWorld = useMarkNotificationsRead(WORLD_ID);
    const center = useNotificationCenter;
    return {
        open(n: DesktopNotification) {
            if (n.world) openWorld(n.world);
            else if (n.onOpen) n.onOpen();
            else center.getState().setOpen(true);
        },
        dismiss(ns: DesktopNotification[]) {
            const world = ns.filter((n) => n.world).map((n) => n.id);
            const local = ns.filter((n) => !n.world).map((n) => n.id);
            if (world.length) void dismissWorld({ ids: world });
            if (local.length) center.getState().dismissLocal(local);
        },
        clearAll() {
            void dismissWorld({ all: true });
            center.getState().dismissLocal('all');
        },
        readAll() {
            void readWorld({ all: true });
            center.getState().readLocal();
        },
    };
}

export function WorldRealtimeBridge() {
    const queryClient = useQueryClient();
    const { connectPartyRealtime, disconnectPartyRealtime } =
        useRealtimeStoreActions();
    const playerId = usePlayerId();
    usePlayerIdle();
    useNotificationWatcher(WORLD_ID, playerId);
    useQuery(
        getGetWorldsPartyIdMessengerQueryOptions(
            WORLD_ID,
            { personaId: playerId ?? undefined },
            { query: { enabled: !!playerId } },
        ),
    );

    useEffect(() => {
        connectPartyRealtime(WORLD_ID);
        return () => disconnectPartyRealtime(WORLD_ID);
    }, [connectPartyRealtime, disconnectPartyRealtime]);

    const invalidatePrefix = (prefix: string) =>
        queryClient.invalidateQueries({
            predicate: (q) =>
                typeof q.queryKey[0] === 'string' &&
                (q.queryKey[0] as string).startsWith(prefix),
        });

    const refreshNotifications = () =>
        queryClient.invalidateQueries({ queryKey: notificationsKey(WORLD_ID) });

    useWorldEvents((envelope) => {
        if (envelope.type === 'party.message.created') {
            const data = envelope.data as {
                chatGroupId?: string;
                message?: { senderId?: string; content?: string | null };
            };
            const dms =
                (playerId
                    ? queryClient.getQueryData<{ data: DirectChatSummary[] }>(
                          getGetWorldsPartyIdMessengerQueryKey(WORLD_ID, {
                              personaId: playerId,
                          }),
                      )?.data
                    : undefined) ?? [];
            invalidatePrefix(worldPrefix('/messenger'));
            if (
                playerId &&
                data.message?.senderId !== playerId &&
                dms.some((d) => d.chatGroupId === data.chatGroupId)
            )
                void refreshNotifications();
            return;
        }

        const { kind, payload } = envelope.data as WorldEventData;
        switch (kind) {
            case 'activity': {
                invalidatePrefix(worldPrefix('/activities'));
                queryClient.invalidateQueries({
                    queryKey: getGetWorldsPartyIdLivesQueryKey(WORLD_ID),
                });
                const a = payload as WorldActivity;
                if (a.app === 'messenger' && a.action === 'dm') {
                    invalidatePrefix(worldPrefix('/messenger'));
                }
                if (playerId && a.withPersonaId === playerId)
                    void refreshNotifications();
                break;
            }
            case 'cdu':
                invalidatePrefix(worldPrefix('/cdu'));
                break;
            case 'body': {
                const body = payload as Body;
                const livesKey = getGetWorldsPartyIdLivesQueryKey(WORLD_ID);
                const life = queryClient
                    .getQueryData<{ data: LifeSummary[] }>(livesKey)
                    ?.data.find((l) => l.personaId === body.participantId);
                if (life && !!life.afk !== !!body.afk)
                    queryClient.invalidateQueries({ queryKey: livesKey });
                const queryKey = getGetWorldsPartyIdPlacesQueryKey(WORLD_ID);
                const snapshot = queryClient.getQueryData<{
                    data: PlacesSnapshot;
                }>(queryKey);
                const known = snapshot?.data.bodies?.find(
                    (b) => b.participantId === body.participantId,
                );
                if (
                    !snapshot ||
                    !known ||
                    !body.appearance ||
                    JSON.stringify(known.appearance) ===
                        JSON.stringify(body.appearance)
                )
                    break;
                queryClient.setQueryData(
                    queryKey,
                    {
                        ...snapshot,
                        data: {
                            ...snapshot.data,
                            bodies: snapshot.data.bodies?.map((b) =>
                                b === known
                                    ? { ...b, appearance: body.appearance }
                                    : b,
                            ),
                        },
                    },
                    {
                        updatedAt:
                            queryClient.getQueryState(queryKey)?.dataUpdatedAt,
                    },
                );
                break;
            }
            case 'clock':
                queryClient.invalidateQueries({
                    queryKey: getGetWorldsPartyIdQueryKey(WORLD_ID),
                });
                queryClient.invalidateQueries({
                    queryKey: getGetWorldsPartyIdLivesQueryKey(WORLD_ID),
                });
                break;
            case 'board': {
                invalidatePrefix(worldPrefix('/board'));
                if (playerId) void refreshNotifications();
                break;
            }
            case 'diary':
                invalidatePrefix(worldPrefix('/diary'));
                break;
            case 'calendar': {
                invalidatePrefix(worldPrefix('/gatherings'));
                const g = payload as Gathering;
                if (playerId && g.attendeeIds?.includes(playerId))
                    void refreshNotifications();
                break;
            }
        }
    });

    return null;
}
