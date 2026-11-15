import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useEffect, useRef } from 'react';
import { create } from 'zustand';
import {
    getGetWorldsPartyIdLivesQueryKey,
    getGetWorldsPartyIdMessengerQueryKey,
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
import type { BoardThread } from '../api/model/boardThread';
import type { Body } from '../api/model/body';
import type { DirectChatSummary } from '../api/model/directChatSummary';
import type { Gathering } from '../api/model/gathering';
import type { LifeSummary } from '../api/model/lifeSummary';
import type { PlacesSnapshot } from '../api/model/placesSnapshot';
import type { WorldActivity } from '../api/model/worldActivity';
import { usePlayerIdle } from '../apps/places/use-player-idle';
import { ROOT_PARTY_ID } from './chat-api';
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

export interface WorldToast {
    id: string;
    title: string;
    body: string;
    icon?: string;
    onOpen?: () => void;
}

interface ToastStore {
    toasts: WorldToast[];
    push: (toast: Omit<WorldToast, 'id'>) => void;
    dismiss: (id: string) => void;
}

export const useWorldToasts = create<ToastStore>((set) => ({
    toasts: [],
    push: (toast) => {
        const id = crypto.randomUUID();
        set((s) => ({ toasts: [...s.toasts.slice(-3), { ...toast, id }] }));
        window.setTimeout(
            () => set((s) => ({ toasts: s.toasts.filter((t) => t.id !== id) })),
            9000,
        );
    },
    dismiss: (id) =>
        set((s) => ({ toasts: s.toasts.filter((t) => t.id !== id) })),
}));

export function toastOnAuto(error: unknown) {
    if (!(error instanceof ProblemDetailsError)) return;
    if (error.title === 'This World is stopped.')
        useWorldToasts.getState().push({
            title: 'This World is stopped',
            body: 'You can look around, but nothing here changes any more. Switch World from the Start menu.',
            icon: '🏚️',
        });
    else if (error.title === 'Your character is on auto.')
        useWorldToasts.getState().push({
            title: 'Your character is on auto',
            body: 'Take over from the Start menu to act yourself.',
            icon: '🤖',
        });
}

const toastedRespect = new Set<string>();

function freshRespectFor(t: BoardThread, playerId: string | null) {
    if (!playerId) return [];
    const lines = [
        { id: t.id, authorId: t.authorId, body: t.body, respect: t.respect },
        ...(t.replies ?? []),
    ];
    return lines
        .filter((l) => l.authorId === playerId)
        .flatMap((l) =>
            (l.respect ?? []).map((r) => ({
                key: `${l.id}:${r.giverId}:${r.at}`,
                giverId: r.giverId,
                giverName: r.giverName ?? 'Someone',
                at: Number(r.at ?? 0),
                on: l.body,
            })),
        )
        .filter(
            (r) =>
                r.giverId !== playerId &&
                Date.now() - r.at < 30_000 &&
                !toastedRespect.has(r.key) &&
                toastedRespect.add(r.key),
        );
}

const clip = (s: string | null | undefined, n: number) =>
    !s ? '' : s.length > n ? `${s.slice(0, n)}…` : s;

export function useOpenDirectChat() {
    const { openWindow } = useDesktopContext();
    return (chatGroupId: string, withName: string) =>
        openWindow('messenger-chat', {
            instanceId: `dm-${chatGroupId}`,
            title: `${withName} — Conversation`,
            props: { chatGroupId, withName },
        });
}

export function WorldRealtimeBridge() {
    const queryClient = useQueryClient();
    const { connectPartyRealtime, disconnectPartyRealtime } =
        useRealtimeStoreActions();
    const playerId = usePlayerId();
    const pushToast = useWorldToasts((s) => s.push);
    const openDirectChat = useOpenDirectChat();
    const { openWindow, desktopState } = useDesktopContext();
    usePlayerIdle();

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
            const dm = dms.find((d) => d.chatGroupId === data.chatGroupId);
            const content = data.message?.content;
            if (
                playerId &&
                content &&
                data.message?.senderId !== playerId &&
                (dm || !data.chatGroupId)
            ) {
                const focused = desktopState.focusedId;
                const dmFocused =
                    dm &&
                    focused === `dm-${dm.chatGroupId}` &&
                    !desktopState.windows[focused]?.minimized;
                if (dm && !dmFocused) {
                    pushToast({
                        title: `${dm.withName} says:`,
                        body: clip(content, 140),
                        icon: '💬',
                        onOpen: () =>
                            openDirectChat(dm.chatGroupId, dm.withName),
                    });
                }
            }
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
                const t = payload as BoardThread;
                const last = t.replies?.[t.replies.length - 1];
                const playerInThread =
                    t.authorId === playerId ||
                    t.replies?.some((r) => r.authorId === playerId);
                if (
                    playerId &&
                    last &&
                    last.authorId !== playerId &&
                    playerInThread
                ) {
                    pushToast({
                        title: `${last.authorName} replied on Partyboard`,
                        body: clip(last.body, 140),
                        icon: '📌',
                        onOpen: () =>
                            openWindow('board', { props: { threadId: t.id } }),
                    });
                }
                for (const r of freshRespectFor(t, playerId)) {
                    pushToast({
                        title: `${r.giverName} gave you Respect!`,
                        body: clip(r.on, 140),
                        icon: '★',
                        onOpen: () =>
                            openWindow('board', { props: { threadId: t.id } }),
                    });
                }
                break;
            }
            case 'diary':
                invalidatePrefix(worldPrefix('/diary'));
                break;
            case 'calendar': {
                invalidatePrefix(worldPrefix('/gatherings'));
                const g = payload as Gathering;
                if (
                    g.status === 'Started' &&
                    playerId &&
                    g.attendeeIds?.includes(playerId)
                ) {
                    pushToast({
                        title: `“${g.title}” is starting`,
                        body: g.placeId
                            ? `At ${g.roomName}. You said you'd be there.`
                            : `In #${g.roomName} — you said you'd be there.`,
                        icon: '📅',
                        onOpen: () =>
                            openWindow(g.placeId ? 'places' : 'open-party'),
                    });
                }
                break;
            }
        }
    });

    return null;
}
