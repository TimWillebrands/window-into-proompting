import {
    Icon,
    TaskPane,
    TaskPaneLink,
    TaskPaneSection,
    Text,
    Toolbar,
    ToolbarButton,
} from '@partytown/ui';
import { useState } from 'react';
import Portrait from '../../../components/Portrait';
import {
    NAV_CATEGORIES,
    PROFILE_STUB,
    paletteFor,
    SERVER_RAIL,
} from './stub-data';

interface RealRoom {
    id: string;
    name: string;
    online?: number | null;
}

interface DaccordSidebarProps {
    realRooms: RealRoom[];
    onSelectRoom: (id: string) => void;
    activeRoomId?: string;
    activeCategory: string;
    onSelectCategory: (id: string) => void;
    onCreateRoom: () => void;
}

export default function DaccordSidebar({
    realRooms,
    onSelectRoom,
    activeRoomId,
    activeCategory,
    onSelectCategory,
    onCreateRoom,
}: DaccordSidebarProps) {
    const [yourRoomsOpen, setYourRoomsOpen] = useState(true);
    const presence = PROFILE_STUB.online ? 'online' : 'offline';

    return (
        <aside className="flex h-full shrink-0">
            <div className="flex w-[56px] shrink-0 flex-col items-center justify-between gap-2 py-2">
                <Toolbar
                    variant="bare"
                    labels="none"
                    orientation="vertical"
                    aria-label="Communities"
                >
                    {SERVER_RAIL.map((s) => (
                        <ToolbarButton
                            key={s.id}
                            title={s.label}
                            aria-label={s.label}
                            icon={<Icon name={s.icon} size={32} />}
                        />
                    ))}
                </Toolbar>
                <span className="@[640px]:hidden">
                    <Portrait
                        alt={PROFILE_STUB.name}
                        who={PROFILE_STUB.avatarSeed}
                        size={32}
                        presence={presence}
                    />
                </span>
            </div>

            <TaskPane className="hidden w-[240px] overflow-y-auto @[640px]:block">
                <TaskPaneSection
                    title="Explore"
                    icon={<Icon name="internet" size={32} />}
                    special
                    collapsible={false}
                >
                    {NAV_CATEGORIES.map((c) => (
                        <TaskPaneLink
                            key={c.id}
                            icon={<Icon name={c.icon} />}
                            aria-current={
                                c.id === activeCategory ? 'page' : undefined
                            }
                            onClick={() => onSelectCategory(c.id)}
                        >
                            {c.id === activeCategory ? (
                                <Text strong>{c.label}</Text>
                            ) : (
                                c.label
                            )}
                        </TaskPaneLink>
                    ))}
                    <TaskPaneLink
                        icon={<Icon name="add" />}
                        title="Create Room"
                        aria-label="Create Room"
                        onClick={onCreateRoom}
                    >
                        Create a Room
                    </TaskPaneLink>
                </TaskPaneSection>

                <TaskPaneSection
                    title="Your Rooms"
                    open={yourRoomsOpen}
                    onOpenChange={setYourRoomsOpen}
                >
                    {realRooms.length === 0 ? (
                        <Text as="p">
                            No rooms yet. Click + above to create one.
                        </Text>
                    ) : (
                        realRooms.map((r) => (
                            <TaskPaneLink
                                key={r.id}
                                icon={<Icon name={paletteFor(r.id).icon} />}
                                title={
                                    r.online != null
                                        ? `${r.online} online`
                                        : 'idle'
                                }
                                aria-current={
                                    r.id === activeRoomId ? 'page' : undefined
                                }
                                onClick={() => onSelectRoom(r.id)}
                            >
                                {r.id === activeRoomId ? (
                                    <Text strong>{r.name}</Text>
                                ) : (
                                    r.name
                                )}
                            </TaskPaneLink>
                        ))
                    )}
                </TaskPaneSection>

                <TaskPaneSection title="Signed in" collapsible={false}>
                    <div className="flex items-center gap-2">
                        <Portrait
                            alt={PROFILE_STUB.name}
                            who={PROFILE_STUB.avatarSeed}
                            size={32}
                            presence={presence}
                        />
                        <div className="min-w-0 flex-1">
                            <Text as="div" strong className="truncate">
                                {PROFILE_STUB.name}
                            </Text>
                            <Text as="div">
                                {PROFILE_STUB.online ? 'Online' : 'Offline'}
                            </Text>
                        </div>
                    </div>
                    <Toolbar
                        variant="bare"
                        labels="none"
                        aria-label="Voice and settings"
                    >
                        <ToolbarButton
                            title="Microphone"
                            aria-label="Microphone"
                            icon={<Icon name="microphone" />}
                        />
                        <ToolbarButton
                            title="Headphones"
                            aria-label="Headphones"
                            icon={<Icon name="headphones" />}
                        />
                        <ToolbarButton
                            title="Settings"
                            aria-label="Settings"
                            icon={<Icon name="settings" />}
                        />
                    </Toolbar>
                </TaskPaneSection>
            </TaskPane>
        </aside>
    );
}
