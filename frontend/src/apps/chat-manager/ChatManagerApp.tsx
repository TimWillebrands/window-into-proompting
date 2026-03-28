import { useQueryClient } from '@tanstack/react-query';
import { Suspense, useState } from 'react';
import { ChatView } from './GroupChatWindow';
import { ROOT_PARTY_ID } from '../../lib/chat-api';
import {
    useRealtimeConnectionStatus,
    useRealtimeStoreActions,
} from '../../lib/realtime-store';
import { useEffect } from 'react';
import {
    getGetPartyIdChatGroupsQueryKey,
    useGetPartyIdChatGroupsSuspense,
    usePostPartyIdChatGroups,
} from '../../api/party-zone';

export default function ChatManagerApp() {
    const queryClient = useQueryClient();
    const [selectedChatGroupId, setSelectedChatGroupId] = useState<string>();
    const [newChatName, setNewChatName] = useState('');

    const { connectPartyRealtime, disconnectPartyRealtime } =
        useRealtimeStoreActions();
    const connectionStatus = useRealtimeConnectionStatus(ROOT_PARTY_ID);

    // Single WebSocket connection to the default party
    useEffect(() => {
        connectPartyRealtime(ROOT_PARTY_ID);
        return () => disconnectPartyRealtime(ROOT_PARTY_ID);
    }, [connectPartyRealtime, disconnectPartyRealtime]);

    const chatGroupsQuery = useGetPartyIdChatGroupsSuspense(ROOT_PARTY_ID);

    const createMutation = usePostPartyIdChatGroups({
        mutation: {
            onSuccess: (created) => {
                queryClient.invalidateQueries({
                    queryKey: getGetPartyIdChatGroupsQueryKey(ROOT_PARTY_ID),
                });
                setSelectedChatGroupId(created.data.id);
                setNewChatName('');
            },
        },
    });

    const chatGroups = chatGroupsQuery.data.data ?? [];
    const selectedGroup = chatGroups.find((g) => g.id === selectedChatGroupId);

    return (
        <div
            className="app-surface flex h-full"
            style={{ background: '#ECE9D8' }}
        >
            {/* Left sidebar */}
            <div
                className="flex flex-col"
                style={{
                    width: 220,
                    minWidth: 220,
                    borderRight: '1px solid #ACA899',
                    background: '#F5F5ED',
                }}
            >
                <div
                    className="p-2 flex items-center justify-between"
                    style={{ borderBottom: '1px solid #ACA899' }}
                >
                    <span style={{ fontWeight: 600, color: '#000' }}>
                        Group Chats
                    </span>
                    <ConnectionDot status={connectionStatus} />
                </div>

                {/* New chat form */}
                <form
                    className="p-2 flex gap-1"
                    style={{ borderBottom: '1px solid #ACA899' }}
                    onSubmit={(e) => {
                        e.preventDefault();
                        const trimmed = newChatName.trim();
                        if (!trimmed || createMutation.isPending) return;
                        createMutation.mutate({
                            id: ROOT_PARTY_ID,
                            data: {
                                name: trimmed,
                            },
                        });
                    }}
                >
                    <input
                        type="text"
                        className="flex-1 text-[11px]"
                        style={{ padding: '2px 4px' }}
                        placeholder="Chat name..."
                        value={newChatName}
                        onChange={(e) => setNewChatName(e.currentTarget.value)}
                    />
                    <button
                        type="submit"
                        disabled={createMutation.isPending}
                        className="text-[11px]"
                        style={{ padding: '2px 8px' }}
                    >
                        {createMutation.isPending ? '...' : '+ New'}
                    </button>
                </form>

                {createMutation.isError && (
                    <div
                        className="px-2 py-1 text-[10px]"
                        style={{ color: '#c00' }}
                    >
                        {createMutation.error instanceof Error
                            ? createMutation.error.message
                            : 'Failed to create'}
                    </div>
                )}

                {/* Chat group list */}
                <div className="flex-1 overflow-y-auto">
                    {chatGroupsQuery.isLoading && (
                        <p
                            className="p-2 text-[11px]"
                            style={{ color: '#808080' }}
                        >
                            Loading...
                        </p>
                    )}
                    {chatGroupsQuery.isError && (
                        <p
                            className="p-2 text-[11px]"
                            style={{ color: '#c00' }}
                        >
                            Failed to load chat groups.
                        </p>
                    )}
                    {chatGroups.length === 0 && !chatGroupsQuery.isLoading && (
                        <p
                            className="p-3 text-[11px]"
                            style={{ color: '#808080' }}
                        >
                            No chats yet. Create one above.
                        </p>
                    )}
                    {chatGroups.map((group) => (
                        <button
                            key={group.id}
                            type="button"
                            onClick={() => setSelectedChatGroupId(group.id)}
                            className="w-full text-left px-2 py-1.5 text-[11px] block"
                            style={{
                                background:
                                    selectedChatGroupId === group.id
                                        ? '#316AC5'
                                        : 'transparent',
                                color:
                                    selectedChatGroupId === group.id
                                        ? '#fff'
                                        : '#000',
                                borderBottom: '1px solid #E8E8E0',
                            }}
                        >
                            <div style={{ fontWeight: 600 }}>{group.name}</div>
                            <div
                                style={{
                                    fontSize: 10,
                                    color:
                                        selectedChatGroupId === group.id
                                            ? '#ccc'
                                            : '#808080',
                                }}
                            >
                                {group.createdAt != null
                                    ? new Date(
                                          group.createdAt,
                                      ).toLocaleDateString()
                                    : ''}
                            </div>
                        </button>
                    ))}
                </div>
            </div>

            {/* Right panel: chat view */}
            <div className="flex-1 min-w-0">
                {selectedChatGroupId ? (
                    <Suspense fallback={<ChatLoadingSpinner />}>
                        <ChatView
                            chatGroupId={selectedChatGroupId}
                            partyName={selectedGroup?.name}
                        />
                    </Suspense>
                ) : (
                    <div
                        className="h-full flex items-center justify-center"
                        style={{ color: '#808080' }}
                    >
                        Select or create a chat group to start messaging.
                    </div>
                )}
            </div>
        </div>
    );
}

function ChatLoadingSpinner() {
    return (
        <div
            className="h-full flex flex-col items-center justify-center gap-3"
            style={{ background: '#ECE9D8', color: '#808080' }}
        >
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
            <span style={{ fontSize: 11 }}>Loading chat...</span>
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
