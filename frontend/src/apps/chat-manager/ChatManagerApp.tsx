import { useQueryClient } from '@tanstack/react-query';
import { Suspense, useEffect, useMemo, useState } from 'react';
import {
    getGetPartyIdChatGroupsQueryKey,
    useGetPartyIdChatGroupsSuspense,
    usePostPartyIdChatGroups,
} from '#api/party-zone';
import {
    useRealtimeConnectionStatus,
    useRealtimeStoreActions,
} from '#lib/realtime-store';
import { useSetWindowTitleBar } from '../../components/desktop/window-titlebar-context';
import { ROOT_PARTY_ID } from '../../lib/chat-api';
import DaccordSearchBar from './components/DaccordSearchBar';
import DaccordSidebar from './components/DaccordSidebar';
import DiscoverFeed from './components/DiscoverFeed';
import ProfilePanel from './components/ProfilePanel';
import { ChatView } from './GroupChatWindow';

type View = { kind: 'hub' } | { kind: 'room'; chatGroupId: string };

export default function ChatManagerApp() {
    const queryClient = useQueryClient();
    const [view, setView] = useState<View>({ kind: 'hub' });
    const [searchQuery, setSearchQuery] = useState('');
    const [activeCategory, setActiveCategory] = useState('home');
    const [newChatName, setNewChatName] = useState('');
    const [newChatScenario, setNewChatScenario] = useState('');

    const { connectPartyRealtime, disconnectPartyRealtime } =
        useRealtimeStoreActions();
    const connectionStatus = useRealtimeConnectionStatus(ROOT_PARTY_ID);

    useEffect(() => {
        connectPartyRealtime(ROOT_PARTY_ID);
        return () => disconnectPartyRealtime(ROOT_PARTY_ID);
    }, [connectPartyRealtime, disconnectPartyRealtime]);

    const chatGroupsQuery = useGetPartyIdChatGroupsSuspense(ROOT_PARTY_ID);
    const chatGroups = useMemo(
        () =>
            (chatGroupsQuery.data.data ?? []).flatMap((g) =>
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
            ),
        [chatGroupsQuery.data.data],
    );

    const createMutation = usePostPartyIdChatGroups({
        mutation: {
            onSuccess: (created) => {
                queryClient.invalidateQueries({
                    queryKey: getGetPartyIdChatGroupsQueryKey(ROOT_PARTY_ID),
                });
                if (created.data.id) {
                    setView({
                        kind: 'room',
                        chatGroupId: created.data.id,
                    });
                }
                setNewChatName('');
                setNewChatScenario('');
            },
        },
    });

    // Push search bar into the window title bar (only when on the hub view).
    const titleBarNode = useMemo(() => {
        if (view.kind !== 'hub') return null;
        return (
            <DaccordSearchBar value={searchQuery} onChange={setSearchQuery} />
        );
    }, [view.kind, searchQuery]);
    useSetWindowTitleBar(titleBarNode);

    if (view.kind === 'room') {
        const selected = chatGroups.find((g) => g.id === view.chatGroupId);
        return (
            <div className="flex h-full flex-col bg-[#ECE9D8] dark:bg-slate-900">
                <div className="flex items-center gap-2 border-b border-slate-300 dark:border-slate-700 bg-gradient-to-r from-indigo-100 to-sky-100 dark:from-indigo-950 dark:to-slate-900 px-3 py-1.5">
                    <button
                        type="button"
                        onClick={() => setView({ kind: 'hub' })}
                        className="rounded-lg bg-white/70 dark:bg-slate-800 px-2.5 py-1 text-[12px] font-semibold text-slate-700 dark:text-slate-100 shadow-sm hover:bg-white dark:hover:bg-slate-700"
                    >
                        ← Back to rooms
                    </button>
                    <span className="text-[12px] font-semibold text-slate-700 dark:text-slate-200">
                        {selected?.name ?? 'Room'}
                    </span>
                    <ConnectionDot status={connectionStatus} />
                </div>
                <div className="flex-1 min-h-0">
                    <Suspense fallback={<ChatLoadingSpinner />}>
                        <ChatView
                            chatGroupId={view.chatGroupId}
                            partyName={selected?.name}
                            scenario={selected?.scenario ?? null}
                        />
                    </Suspense>
                </div>
            </div>
        );
    }

    return (
        <div className="app-surface flex h-full overflow-hidden bg-white dark:bg-slate-900">
            <DaccordSidebar
                realRooms={chatGroups.map((g) => ({ id: g.id, name: g.name }))}
                onSelectRoom={(id) =>
                    setView({ kind: 'room', chatGroupId: id })
                }
                activeCategory={activeCategory}
                onSelectCategory={setActiveCategory}
            />
            <main className="flex flex-1 min-w-0 flex-col bg-gradient-to-b from-white to-slate-50 dark:from-slate-900 dark:to-slate-950">
                <div className="flex items-center justify-between border-b border-slate-200 dark:border-slate-700 bg-white/70 dark:bg-slate-900/70 px-4 py-1.5 backdrop-blur-sm">
                    <span className="text-[11px] text-slate-500 dark:text-slate-400">
                        Realtime:
                    </span>
                    <div className="flex items-center gap-1.5 text-[11px] text-slate-500 dark:text-slate-400">
                        <ConnectionDot status={connectionStatus} />
                        <span>{connectionStatus}</span>
                    </div>
                </div>
                {createMutation.isError && (
                    <div className="bg-red-100 dark:bg-red-950 px-4 py-1.5 text-xs text-red-700 dark:text-red-300">
                        {createMutation.error instanceof Error
                            ? createMutation.error.message
                            : 'Failed to create room.'}
                    </div>
                )}
                <DiscoverFeed
                    realRooms={chatGroups}
                    onCreateRoom={(name, scenario) =>
                        createMutation.mutate({
                            id: ROOT_PARTY_ID,
                            data: { name, scenario: scenario || null },
                        })
                    }
                    creating={createMutation.isPending}
                    searchQuery={searchQuery}
                    onSelectRealRoom={(id) =>
                        setView({ kind: 'room', chatGroupId: id })
                    }
                    inputName={newChatName}
                    inputScenario={newChatScenario}
                    onInputName={setNewChatName}
                    onInputScenario={setNewChatScenario}
                />
            </main>
            <ProfilePanel />
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
