import { useQueryClient } from '@tanstack/react-query';
import { useRouter } from '@tanstack/react-router';
import { Suspense, useCallback, useEffect, useMemo, useState } from 'react';
import {
    getGetPartyIdChatGroupsQueryKey,
    useGetPartyIdChatGroupsSuspense,
    usePostPartyIdChatGroups,
} from '#api/party-zone';
import {
    useRealtimeConnectionStatus,
    useRealtimeStoreActions,
} from '#lib/realtime-store';
import { useWindowInstance } from '../../components/desktop/window-instance-context';
import { useSetWindowTitleBar } from '../../components/desktop/window-titlebar-context';
import { ROOT_PARTY_ID } from '../../lib/chat-api';
import { useDesktopContext, useDesktopStore } from '../../lib/desktop-context';
import CreateRoomDialog from './components/CreateRoomDialog';
import DaccordSearchBar from './components/DaccordSearchBar';
import DaccordSidebar from './components/DaccordSidebar';
import DiscoverFeed from './components/DiscoverFeed';
import ProfilePanel from './components/ProfilePanel';
import { ChatView } from './GroupChatWindow';

type View = { kind: 'hub' } | { kind: 'room'; chatGroupId: string };

export default function DaccordApp() {
    const queryClient = useQueryClient();
    const router = useRouter();
    const { windowId } = useWindowInstance();
    const { setWindowProps } = useDesktopContext();
    const roomIdFromProps = useDesktopStore((s) => {
        const p = s.desktopState.windows[windowId]?.props;
        const v = p?.roomId;
        return typeof v === 'string' ? v : null;
    });
    const view: View = roomIdFromProps
        ? { kind: 'room', chatGroupId: roomIdFromProps }
        : { kind: 'hub' };

    const enterRoom = useCallback(
        (id: string, replace: boolean) => {
            setWindowProps(windowId, (p) => ({ ...p, roomId: id }), {
                replace,
            });
        },
        [setWindowProps, windowId],
    );
    const exitRoom = useCallback(() => {
        // Prefer consuming the existing browser entry (so forward re-enters).
        // On a deep-link reload there is no entry to consume — fall back to
        // a replace so we don't pollute history.
        if (typeof window !== 'undefined' && window.history.length > 1) {
            router.history.back();
        } else {
            setWindowProps(
                windowId,
                (p) => {
                    const { roomId: _omit, ...rest } = p;
                    return rest;
                },
                { replace: true },
            );
        }
    }, [router, setWindowProps, windowId]);

    const [searchQuery, setSearchQuery] = useState('');
    const [activeCategory, setActiveCategory] = useState('home');
    const [createOpen, setCreateOpen] = useState(false);

    const { connectPartyRealtime, disconnectPartyRealtime } =
        useRealtimeStoreActions();
    const connectionStatus = useRealtimeConnectionStatus(ROOT_PARTY_ID);

    useEffect(() => {
        connectPartyRealtime(ROOT_PARTY_ID);
        return () => disconnectPartyRealtime(ROOT_PARTY_ID);
    }, [connectPartyRealtime, disconnectPartyRealtime]);

    const chatGroupsQuery = useGetPartyIdChatGroupsSuspense(ROOT_PARTY_ID);
    const chatGroups = useMemo(() => {
        const raw = chatGroupsQuery.data?.data;
        if (!Array.isArray(raw)) return [];
        const mapped = raw.flatMap((g) =>
            g.id && g.name
                ? [
                      {
                          id: g.id,
                          name: g.name,
                          scenario: g.scenario ?? null,
                          createdAt: g.createdAt ?? null,
                      },
                  ]
                : [],
        );
        const ts = (v: string | number | null) =>
            v == null ? 0 : new Date(v).getTime() || 0;
        return mapped.sort((a, b) => ts(b.createdAt) - ts(a.createdAt));
    }, [chatGroupsQuery.data?.data]);

    const createMutation = usePostPartyIdChatGroups({
        mutation: {
            onSuccess: (created) => {
                queryClient.invalidateQueries({
                    queryKey: getGetPartyIdChatGroupsQueryKey(ROOT_PARTY_ID),
                });
                setCreateOpen(false);
                if (created.data.id) {
                    enterRoom(created.data.id, false);
                }
            },
        },
    });

    const handleCreateRoom = useCallback(
        (name: string, scenario: string) => {
            createMutation.mutate({
                id: ROOT_PARTY_ID,
                data: { name, scenario: scenario || null },
            });
        },
        [createMutation],
    );

    const createErrorMessage = createMutation.isError
        ? createMutation.error instanceof Error
            ? createMutation.error.message
            : 'Failed to create room.'
        : null;

    // Push search bar into the window title bar (only when on the hub view).
    const titleBarNode = useMemo(() => {
        if (view.kind !== 'hub') return null;
        return (
            <DaccordSearchBar value={searchQuery} onChange={setSearchQuery} />
        );
    }, [view.kind, searchQuery]);
    useSetWindowTitleBar(titleBarNode);

    const dialog = (
        <CreateRoomDialog
            open={createOpen}
            creating={createMutation.isPending}
            errorMessage={createErrorMessage}
            onClose={() => setCreateOpen(false)}
            onSubmit={handleCreateRoom}
        />
    );

    if (view.kind === 'room') {
        const selected = chatGroups.find((g) => g.id === view.chatGroupId);
        return (
            <div className="app-surface no-xp-buttons @container relative flex h-full overflow-hidden bg-white dark:bg-slate-900">
                <DaccordSidebar
                    realRooms={chatGroups.map((g) => ({
                        id: g.id,
                        name: g.name,
                    }))}
                    onSelectRoom={(id) => enterRoom(id, true)}
                    activeRoomId={view.chatGroupId}
                    activeCategory={activeCategory}
                    onSelectCategory={setActiveCategory}
                    onCreateRoom={() => setCreateOpen(true)}
                />
                <main className="flex flex-1 min-w-0 flex-col bg-gradient-to-b from-slate-50 via-white to-blue-50 dark:from-slate-900 dark:via-slate-900 dark:to-slate-950">
                    {/* Glass header — back button + room title */}
                    <div className="flex items-center gap-2 px-4 py-2">
                        <button
                            type="button"
                            onClick={exitRoom}
                            className="relative inline-flex items-center gap-1.5 overflow-hidden rounded-full bg-gradient-to-br from-white to-blue-50 px-3 py-1.5 text-[12px] font-semibold text-slate-700 shadow-[inset_0_1px_0_rgba(255,255,255,0.7),0_4px_10px_-3px_rgba(46,92,202,0.3)] ring-1 ring-white/80 backdrop-blur-md transition-all hover:-translate-y-px hover:shadow-[inset_0_1px_0_rgba(255,255,255,0.8),0_6px_14px_-3px_rgba(46,92,202,0.4)] dark:bg-slate-800 dark:text-slate-100"
                        >
                            <span aria-hidden>←</span>
                            Back to rooms
                        </button>
                        <span className="ml-1 truncate text-[13px] font-semibold text-slate-800 dark:text-slate-200">
                            {selected?.name ?? 'Room'}
                        </span>
                        <span className="ml-auto flex items-center gap-1.5 rounded-full bg-white/80 dark:bg-slate-800/70 px-2.5 py-1 text-[11px] text-slate-600 dark:text-slate-300 shadow-[inset_0_1px_0_rgba(255,255,255,0.7),0_2px_6px_-2px_rgba(31,55,148,0.18)] ring-1 ring-white/80 backdrop-blur-md">
                            <ConnectionDot status={connectionStatus} />
                            <span className="capitalize">
                                {connectionStatus}
                            </span>
                        </span>
                    </div>
                    <div className="flex-1 min-h-0 px-2 pb-2">
                        <div className="relative h-full overflow-hidden rounded-3xl bg-white/85 dark:bg-slate-900/80 shadow-[0_10px_30px_-8px_rgba(31,55,148,0.4)] ring-1 ring-white/70 backdrop-blur-xl">
                            <span
                                aria-hidden
                                className="pointer-events-none absolute inset-x-0 top-0 z-10 h-12 rounded-t-3xl bg-gradient-to-b from-white/60 to-transparent"
                            />
                            <Suspense fallback={<ChatLoadingSpinner />}>
                                <ChatView
                                    chatGroupId={view.chatGroupId}
                                    partyName={selected?.name}
                                    scenario={selected?.scenario ?? null}
                                />
                            </Suspense>
                        </div>
                    </div>
                </main>
                {dialog}
            </div>
        );
    }

    return (
        <div className="app-surface no-xp-buttons @container relative flex h-full overflow-hidden bg-white dark:bg-slate-900">
            <DaccordSidebar
                realRooms={chatGroups.map((g) => ({ id: g.id, name: g.name }))}
                onSelectRoom={(id) => enterRoom(id, false)}
                activeCategory={activeCategory}
                onSelectCategory={setActiveCategory}
                onCreateRoom={() => setCreateOpen(true)}
            />
            <main className="flex flex-1 min-w-0 flex-col bg-gradient-to-b from-slate-50 via-white to-blue-50 dark:from-slate-900 dark:via-slate-900 dark:to-slate-950">
                <div className="flex items-center justify-between px-4 py-2">
                    <span className="text-[11px] font-medium text-slate-500 dark:text-slate-400">
                        Realtime
                    </span>
                    <div className="flex items-center gap-1.5 rounded-full bg-white/80 dark:bg-slate-800/70 px-2.5 py-1 text-[11px] text-slate-600 dark:text-slate-300 shadow-[inset_0_1px_0_rgba(255,255,255,0.7),0_2px_6px_-2px_rgba(31,55,148,0.18)] ring-1 ring-white/80 backdrop-blur-md">
                        <ConnectionDot status={connectionStatus} />
                        <span className="capitalize">{connectionStatus}</span>
                    </div>
                </div>
                <DiscoverFeed
                    realRooms={chatGroups}
                    searchQuery={searchQuery}
                    onSelectRealRoom={(id) => enterRoom(id, false)}
                    onOpenCreate={() => setCreateOpen(true)}
                />
            </main>
            <ProfilePanel />
            {dialog}
        </div>
    );
}

function ChatLoadingSpinner() {
    return (
        <div className="flex h-full flex-col items-center justify-center gap-3 bg-[#ECE9D8] dark:bg-slate-900 text-slate-500 dark:text-slate-400">
            <div
                style={{
                    width: 24,
                    height: 24,
                    border: '3px solid #ACA899',
                    borderTopColor: '#316AC5',
                    borderRadius: '50%',
                    animation: 'spin 0.7s linear infinite',
                }}
            />
            <span className="text-[11px]">Loading chat...</span>
            <style>{`@keyframes spin { to { transform: rotate(360deg); } }`}</style>
        </div>
    );
}

function ConnectionDot({ status }: { status: string }) {
    const color: Record<string, string> = {
        connected: '#00AA00',
        connecting: '#CC8800',
        reconnecting: '#CC8800',
        disconnected: '#CC0000',
    };
    return (
        <span
            title={status}
            style={{
                display: 'inline-block',
                width: 8,
                height: 8,
                borderRadius: '50%',
                background: color[status] ?? '#CC0000',
                border: '1px solid rgba(0,0,0,0.3)',
            }}
        />
    );
}
