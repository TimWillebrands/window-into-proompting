import { CommandButton, GroupBox, Icon, Stack, Text } from '@partytown/ui';
import type { ReactNode } from 'react';
import RoomCard from './RoomCard';
import {
    ALL_STUB_ROOMS,
    FEATURED_ROOMS,
    POPULAR_ROOMS,
    paletteFor,
    RECENT_ROOMS,
} from './stub-data';

interface RealRoom {
    id: string;
    name: string;
    scenario?: string | null;
    createdAt?: string | number | null;
}

interface DiscoverFeedProps {
    realRooms: RealRoom[];
    searchQuery: string;
    onSelectRealRoom: (id: string) => void;
    onOpenCreate: () => void;
}

const GRID = 'grid grid-cols-1 gap-4 @[520px]:grid-cols-2';
const WIDE_GRID = `${GRID} @[800px]:grid-cols-3`;

export default function DiscoverFeed({
    realRooms,
    searchQuery,
    onSelectRealRoom,
    onOpenCreate,
}: DiscoverFeedProps) {
    const q = searchQuery.trim().toLowerCase();
    if (q) {
        const matchedStubs = ALL_STUB_ROOMS.filter((r) =>
            r.name.toLowerCase().includes(q),
        );
        const matchedReal = realRooms.filter((r) =>
            r.name.toLowerCase().includes(q),
        );
        return (
            <Stack gap="lg" pad="xl">
                <Text as="h2" size="title">
                    Search results for &ldquo;{searchQuery}&rdquo;
                </Text>
                {matchedReal.length === 0 && matchedStubs.length === 0 ? (
                    <Text as="p" tone="muted">
                        No rooms match. Try another query.
                    </Text>
                ) : (
                    <div className={GRID}>
                        {matchedReal.map((r) => (
                            <RoomCard
                                key={r.id}
                                name={r.name}
                                description={
                                    r.scenario || 'A room you have joined.'
                                }
                                icon={paletteFor(r.id).icon}
                                online={0}
                                members={1}
                                size="md"
                                onClick={() => onSelectRealRoom(r.id)}
                            />
                        ))}
                        {matchedStubs.map((r) => (
                            <RoomCard
                                key={r.id}
                                name={r.name}
                                description={r.description}
                                icon={r.icon}
                                online={r.online}
                                members={r.members}
                                size="md"
                                disabled
                            />
                        ))}
                    </div>
                )}
            </Stack>
        );
    }

    return (
        <Stack gap="xl" pad="xl">
            <Stack as="section" direction="row" align="center" gap="xl">
                <Icon name="chat-rooms" size={48} />
                <Stack.Item as="div">
                    <Text as="h1" size="display" className="block">
                        Find Your Community on D'Accord
                    </Text>
                    <Text as="p" size="reading" tone="muted">
                        Discover, connect, and create with people who share your
                        passions.
                    </Text>
                </Stack.Item>
            </Stack>

            <Section title="Featured Community">
                <div className={GRID}>
                    {FEATURED_ROOMS.map((r) => (
                        <RoomCard
                            key={r.id}
                            name={r.name}
                            description={r.description}
                            icon={r.icon}
                            online={r.online}
                            members={r.members}
                            size="lg"
                            disabled
                        />
                    ))}
                </div>
            </Section>

            <Section title="Popular Right Now">
                <div className={GRID}>
                    {POPULAR_ROOMS.map((r) => (
                        <RoomCard
                            key={r.id}
                            name={r.name}
                            description={r.description}
                            icon={r.icon}
                            online={r.online}
                            members={r.members}
                            size="md"
                            disabled
                        />
                    ))}
                </div>
            </Section>

            <Section title="Recently Added">
                <div className={WIDE_GRID}>
                    {RECENT_ROOMS.map((r) => (
                        <RoomCard
                            key={r.id}
                            name={r.name}
                            description={r.description}
                            icon={r.icon}
                            online={r.online}
                            members={r.members}
                            size="md"
                            disabled
                        />
                    ))}
                </div>
            </Section>

            {realRooms.length > 0 && (
                <Section title="Rooms You've Joined">
                    <div className={WIDE_GRID}>
                        {realRooms.map((r) => (
                            <RoomCard
                                key={r.id}
                                name={r.name}
                                description={
                                    r.scenario ||
                                    'A room you have joined. Click to enter.'
                                }
                                icon={paletteFor(r.id).icon}
                                online={0}
                                members={1}
                                size="md"
                                onClick={() => onSelectRealRoom(r.id)}
                            />
                        ))}
                    </div>
                </Section>
            )}

            <GroupBox
                title="Start Your Own Room"
                icon={<Icon name="add" />}
                className="mb-4 justify-items-start"
            >
                <Text as="p">
                    Spin up a fresh room with a title and an optional scenario
                    to set the scene.
                </Text>
                <CommandButton
                    tone="go"
                    icon={<Icon name="add" />}
                    onClick={onOpenCreate}
                >
                    Create Room
                </CommandButton>
            </GroupBox>
        </Stack>
    );
}

function Section({ title, children }: { title: string; children: ReactNode }) {
    return (
        <Stack as="section" gap="lg">
            <Text as="h2" size="title">
                {title}
            </Text>
            {children}
        </Stack>
    );
}
