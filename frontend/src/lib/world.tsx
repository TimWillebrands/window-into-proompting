import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useEffect, useRef } from 'react';
import { create } from 'zustand';
import {
    getGetWorldsPartyIdLivesQueryKey,
    getGetWorldsPartyIdMessengerQueryKey,
    getGetWorldsPartyIdQueryKey,
    postWorldsPartyIdPlayer,
} from '#api/party-zone';
import { useDesktopContext } from '#lib/desktop-context';
import { useRealtimeStoreActions } from '#lib/realtime-store';
import type { BoardThread } from '../api/model/boardThread';
import type { DirectChatSummary } from '../api/model/directChatSummary';
import type { Gathering } from '../api/model/gathering';
import type { WorldActivity } from '../api/model/worldActivity';
import { ROOT_PARTY_ID } from './chat-api';
import { type WorldEnvelope, type WorldEventData, worldBus } from './world-bus';

/** The World the desktop shows. One Party today; the API is multi-world. */
export const WORLD_ID = ROOT_PARTY_ID;

const worldPrefix = (suffix = '') => `/api/worlds/${WORLD_ID}${suffix}`;

/** Subscribe to raw World envelopes for the lifetime of the component. */
export function useWorldEvents(handler: (envelope: WorldEnvelope) => void) {
    const ref = useRef(handler);
    ref.current = handler;
    useEffect(() => worldBus.subscribe((e) => ref.current(e)), []);
}

const PLAYER_KEY = 'proompting.player';

interface PlayerSession {
    name: string | null;
    /** False until the browser's stored sign-in is read; SSR and first paint agree on "unknown". */
    hydrated: boolean;
    hydrate: () => void;
    signIn: (name: string) => void;
    signOut: () => void;
}

/** Which human sits at this browser. Several humans can share a World, each signed in by name. */
export const usePlayerSession = create<PlayerSession>((set) => ({
    name: null,
    hydrated: false,
    hydrate: () =>
        set({ name: window.localStorage.getItem(PLAYER_KEY), hydrated: true }),
    signIn: (name) => {
        window.localStorage.setItem(PLAYER_KEY, name);
        set({ name });
    },
    signOut: () => {
        window.localStorage.removeItem(PLAYER_KEY);
        set({ name: null });
    },
}));

/** The signed-in human's Participant id in this World, created on first use. */
export function usePlayerId(): string | null {
    const name = usePlayerSession((s) => s.name);
    const { data } = useQuery({
        queryKey: ['world-player', WORLD_ID, name],
        queryFn: async () => {
            const res = await postWorldsPartyIdPlayer(WORLD_ID, { name });
            return res.data.playerId as string;
        },
        enabled: !!name,
        staleTime: Number.POSITIVE_INFINITY,
    });
    return data ?? null;
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

const clip = (s: string | null | undefined, n: number) =>
    !s ? '' : s.length > n ? `${s.slice(0, n)}…` : s;

/** Opens a Messenger conversation window for a DM thread. */
export function useOpenDirectChat() {
    const { openWindow } = useDesktopContext();
    return (chatGroupId: string, withName: string) =>
        openWindow('messenger-chat', {
            instanceId: `dm-${chatGroupId}`,
            title: `${withName} — Conversation`,
            props: { chatGroupId, withName },
        });
}

/**
 * Mounted once on the desktop: keeps the realtime socket open, turns World envelopes into
 * query invalidations, and raises MSN-style toasts when someone reaches the Player.
 */
export function WorldRealtimeBridge() {
    const queryClient = useQueryClient();
    const { connectPartyRealtime, disconnectPartyRealtime } =
        useRealtimeStoreActions();
    const playerId = usePlayerId();
    const pushToast = useWorldToasts((s) => s.push);
    const openDirectChat = useOpenDirectChat();
    const { openWindow, desktopState } = useDesktopContext();

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
                    // A brand-new DM thread to the Player isn't in the cached list yet.
                    invalidatePrefix(worldPrefix('/messenger'));
                }
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
                        title: `${last.authorName} replied on the Board`,
                        body: clip(last.body, 140),
                        icon: '📌',
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
                        body: `In #${g.roomName} — you said you'd be there.`,
                        icon: '📅',
                        onOpen: () => openWindow('open-party'),
                    });
                }
                break;
            }
        }
    });

    return null;
}
