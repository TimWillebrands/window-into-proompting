import {
    CommandButton,
    Icon,
    Presence,
    type PresenceStatus,
    ProgressBar,
    Stack,
    StatusBar,
    StatusBarField,
    Text,
    WindowLayout,
} from '@partytown/ui';
import { useQueryClient } from '@tanstack/react-query';
import { useRouter } from '@tanstack/react-router';
import { Suspense, useCallback, useEffect, useMemo, useState } from 'react';
import {
    getGetPartyIdChatGroupsQueryKey,
    useGetPartyIdChatGroupsSuspense,
    usePostPartyIdChatGroups,
} from '#api/party-zone';
import { useDesktopContext, useDesktopStore } from '#lib/desktop-context';
import {
    useRealtimeConnectionStatus,
    useRealtimeStoreActions,
} from '#lib/realtime-store';
import { useWindowInstance } from '../../components/desktop/window-instance-context';
import { useSetWindowTitleBar } from '../../components/desktop/window-titlebar-context';
import { WORLD_ID } from '../../lib/world';
import CreateRoomDialog from './components/CreateRoomDialog';
import DaccordSearchBar from './components/DaccordSearchBar';
import DaccordSidebar from './components/DaccordSidebar';
import DiscoverFeed from './components/DiscoverFeed';
import ProfilePanel from './components/ProfilePanel';
import { ChatView, ScenarioChip } from './GroupChatWindow';

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
    const connectionStatus = useRealtimeConnectionStatus(WORLD_ID);

    useEffect(() => {
        connectPartyRealtime(WORLD_ID);
        return () => disconnectPartyRealtime(WORLD_ID);
    }, [connectPartyRealtime, disconnectPartyRealtime]);

    const chatGroupsQuery = useGetPartyIdChatGroupsSuspense(WORLD_ID);
    const chatGroups = useMemo(() => {
        const raw = chatGroupsQuery.data?.data;
        if (!Array.isArray(raw)) return [];
        const mapped = raw.flatMap((g) =>
            g.id && g.name && g.kind !== 'dm' && g.kind !== 'place'
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
                    queryKey: getGetPartyIdChatGroupsQueryKey(WORLD_ID),
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
                id: WORLD_ID,
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

    const sidebar = (
        <DaccordSidebar
            realRooms={chatGroups.map((g) => ({
                id: g.id,
                name: g.name,
            }))}
            onSelectRoom={(id) => enterRoom(id, view.kind === 'room')}
            activeRoomId={view.kind === 'room' ? view.chatGroupId : undefined}
            activeCategory={activeCategory}
            onSelectCategory={setActiveCategory}
            onCreateRoom={() => setCreateOpen(true)}
        />
    );

    const status = (
        <StatusBar>
            <StatusBarField
                grow
                icon={<ConnectionDot status={connectionStatus} />}
                title={`Realtime connection: ${connectionStatus}`}
            >
                Connection: {connectionStatus}
            </StatusBarField>
        </StatusBar>
    );

    if (view.kind === 'room') {
        const selected = chatGroups.find((g) => g.id === view.chatGroupId);
        return (
            <>
                <WindowLayout
                    toolbar={
                        <Stack direction="row" align="center" gap="md" pad="md">
                            <CommandButton
                                icon={<Icon name="back" />}
                                onClick={exitRoom}
                            >
                                Back to rooms
                            </CommandButton>
                            <Text size="title" className="truncate">
                                {selected?.name ?? 'Room'}
                            </Text>
                            <ScenarioChip
                                partyId={WORLD_ID}
                                chatGroupId={view.chatGroupId}
                                scenario={selected?.scenario ?? null}
                            />
                        </Stack>
                    }
                    taskPaneLabel="D'Accord"
                    taskPaneWidth="wide"
                    taskPaneNarrow="hide"
                    narrowBelow={820}
                    taskPane={sidebar}
                    scroll="none"
                    contentLabel={selected?.name ?? 'Room'}
                    statusBar={status}
                >
                    <Suspense fallback={<ChatLoadingSpinner />}>
                        <ChatView chatGroupId={view.chatGroupId} />
                    </Suspense>
                </WindowLayout>
                {dialog}
            </>
        );
    }

    return (
        <>
            <WindowLayout
                taskPaneLabel="D'Accord"
                taskPaneWidth="wide"
                taskPaneNarrow="hide"
                taskPane={sidebar}
                detailsLabel="Profile"
                detailsPane={<ProfilePanel />}
                detailsNarrow="hide"
                detailsNarrowBelow={1000}
                contentLabel="Explore"
                statusBar={status}
            >
                <DiscoverFeed
                    realRooms={chatGroups}
                    searchQuery={searchQuery}
                    onSelectRealRoom={(id) => enterRoom(id, false)}
                    onOpenCreate={() => setCreateOpen(true)}
                />
            </WindowLayout>
            {dialog}
        </>
    );
}

function ChatLoadingSpinner() {
    return (
        <Stack align="center" justify="center" gap="lg">
            <ProgressBar marquee label="Loading chat" className="w-[160px]" />
            <Text tone="muted">Loading chat...</Text>
        </Stack>
    );
}

const CONNECTION: Record<string, PresenceStatus> = {
    connected: 'online',
    connecting: 'away',
    reconnecting: 'away',
    disconnected: 'busy',
};

function ConnectionDot({ status }: { status: string }) {
    return (
        <Presence
            status={CONNECTION[status] ?? 'busy'}
            active={status === 'connecting' || status === 'reconnecting'}
            label={status}
        />
    );
}
