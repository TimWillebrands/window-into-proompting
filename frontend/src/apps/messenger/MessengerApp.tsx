import {
    BuddyList,
    type BuddyListGroup,
    type BuddyListItem,
    ContextMenu,
    Icon,
    type MenuEntry,
    Pane,
    PictureStack,
    Stack,
    StatusBar,
    StatusBarField,
    Text,
    TextBox,
    Toolbar,
    ToolbarButton,
    useContextMenu,
    WindowLayout,
} from '@partytown/ui';
import { useQueryClient } from '@tanstack/react-query';
import { useMemo, useState } from 'react';
import {
    getGetWorldsPartyIdMessengerQueryKey,
    useGetWorldsPartyIdLivesSuspense,
    useGetWorldsPartyIdMessengerSuspense,
    useGetWorldsPartyIdSelves,
    usePostWorldsPartyIdMessenger,
    usePostWorldsPartyIdMessengerChatGroupIdLeave,
    usePostWorldsPartyIdMessengerGroups,
} from '#api/party-zone';
import { useDesktopContext } from '#lib/desktop-context';
import type { DirectChatSummary } from '../../api/model/directChatSummary';
import type { LifeSummary } from '../../api/model/lifeSummary';
import Portrait from '../../components/Portrait';
import {
    notifyOnRefusal,
    useOpenConversation,
    usePlayerId,
    WORLD_ID,
} from '../../lib/world';
import {
    conversationName,
    groupOf,
    isGroup,
    MAX_MEMBERS,
    names,
    othersIn,
    refusal,
} from './conversations';
import {
    BuddyIcon,
    clip,
    DisplayPicture,
    MsnText,
    STATUS_LABEL,
    shortTime,
    statusOf,
    toMillis,
    useMessengerSeen,
} from './msn-ui';

const CHAT = 'chat:';

export default function MessengerApp() {
    const queryClient = useQueryClient();
    const playerId = usePlayerId();
    const openConversation = useOpenConversation();
    const { closeWindow } = useDesktopContext();
    const seen = useMessengerSeen((s) => s.seen);

    const livesQuery = useGetWorldsPartyIdLivesSuspense(WORLD_ID);
    const chatsQuery = useGetWorldsPartyIdMessengerSuspense(WORLD_ID, {
        personaId: playerId ?? undefined,
    });
    const lives = livesQuery.data.data ?? [];
    const chats = chatsQuery.data.data ?? [];
    const selvesQuery = useGetWorldsPartyIdSelves(WORLD_ID, {
        query: { refetchInterval: 60_000 },
    });
    const selfById = useMemo(
        () =>
            new Map(
                (selvesQuery.data?.data ?? []).map((s) => [s.personaId, s]),
            ),
        [selvesQuery.data],
    );

    const player = lives.find((l) => l.personaId === playerId);
    const contacts = useMemo(
        () =>
            lives
                .filter((l) => l.personaId !== playerId)
                .sort(
                    (a, b) =>
                        Number(a.afk) - Number(b.afk) ||
                        Number(a.busy) - Number(b.busy) ||
                        a.name.localeCompare(b.name),
                ),
        [lives, playerId],
    );
    const lifeById = useMemo(
        () => new Map(lives.map((l) => [l.personaId, l])),
        [lives],
    );

    const [filter, setFilter] = useState('');
    const [selected, setSelected] = useState<string[]>([]);
    const [collapsed, setCollapsed] = useState<string[]>([]);
    const [problem, setProblem] = useState<string | null>(null);
    const [menuFor, setMenuFor] = useState<string | null>(null);
    const menu = useContextMenu();

    const refresh = () =>
        queryClient.invalidateQueries({
            queryKey: getGetWorldsPartyIdMessengerQueryKey(WORLD_ID),
        });
    const openDm = usePostWorldsPartyIdMessenger({
        mutation: { onSuccess: refresh },
    });
    const startGroup = usePostWorldsPartyIdMessengerGroups({
        mutation: { onSuccess: refresh },
    });
    const leave = usePostWorldsPartyIdMessengerChatGroupIdLeave({
        mutation: { onSuccess: refresh },
    });

    const picked = contacts.filter((c) => selected.includes(c.personaId));

    const openChat = (chat: DirectChatSummary) =>
        openConversation(chat.chatGroupId, {
            name: conversationName(chat, playerId),
            group: isGroup(chat),
        });

    const startChat = async (personaId: string, name: string) => {
        setProblem(null);
        const existing = chats.find(
            (c) => !isGroup(c) && c.withId === personaId,
        );
        if (existing) {
            openChat(existing);
            return;
        }
        try {
            const res = await openDm.mutateAsync({
                partyId: WORLD_ID,
                data: { withPersonaId: personaId, fromPersonaId: playerId },
            });
            if (res.data.id) openConversation(res.data.id, { name });
        } catch (e) {
            notifyOnRefusal(e);
            setProblem(refusal(e, "Couldn't start the conversation."));
        }
    };

    const startConversation = async (people: LifeSummary[]) => {
        if (people.length === 0) return;
        const [first] = people;
        if (people.length === 1) {
            await startChat(first.personaId, first.name);
            return;
        }
        if (people.length + 1 > MAX_MEMBERS) {
            setProblem(
                `A conversation holds at most ${MAX_MEMBERS} people, you included.`,
            );
            return;
        }
        setProblem(null);
        try {
            const res = await startGroup.mutateAsync({
                partyId: WORLD_ID,
                data: {
                    participantIds: people.map((p) => p.personaId),
                    fromPersonaId: playerId,
                },
            });
            const started = groupOf(res);
            if (!started.id) return;
            setSelected([]);
            openConversation(started.id, {
                name: people.map((p) => p.name).join(', '),
                group: true,
            });
        } catch (e) {
            notifyOnRefusal(e);
            setProblem(refusal(e, "Couldn't start the conversation."));
        }
    };

    const leaveChat = async (chat: DirectChatSummary) => {
        setProblem(null);
        try {
            await leave.mutateAsync({
                partyId: WORLD_ID,
                chatGroupId: chat.chatGroupId,
                data: { fromPersonaId: playerId },
            });
            closeWindow(`dm-${chat.chatGroupId}`);
        } catch (e) {
            notifyOnRefusal(e);
            setProblem(refusal(e, "Couldn't leave the conversation."));
        }
    };

    const q = filter.trim().toLowerCase();
    const match = (name: string) => !q || name.toLowerCase().includes(q);
    const online = contacts.filter((c) => !c.afk && !c.busy && match(c.name));
    const busy = contacts.filter((c) => !c.afk && c.busy && match(c.name));
    const afk = contacts.filter((c) => c.afk && match(c.name));
    const recent = chats.filter(
        (c) =>
            match(conversationName(c, playerId)) ||
            (isGroup(c) && othersIn(c, playerId).some((m) => match(m.name))),
    );

    const playerName = player?.name ?? 'You';
    const busyWith = (id: string) =>
        (openDm.isPending && openDm.variables?.data.withPersonaId === id) ||
        (startGroup.isPending &&
            !!startGroup.variables?.data.participantIds.includes(id));

    const contactItem = (c: LifeSummary): BuddyListItem => {
        const self = selfById.get(c.personaId);
        const status = statusOf(c, self);
        const note = self?.doing ?? c.lastActivity?.summary;
        const pending = busyWith(c.personaId);
        return {
            id: c.personaId,
            title: `Send an instant message to ${c.name}`,
            disabled: pending,
            picture: <BuddyIcon status={status} />,
            name: (
                <>
                    {c.name}
                    {status !== 'online' && (
                        <Text tone="muted"> ({STATUS_LABEL[status]})</Text>
                    )}
                    {note && (
                        <Text tone="muted" italic>
                            {' '}
                            - {note}
                        </Text>
                    )}
                </>
            ),
            meta: pending ? '…' : undefined,
        };
    };

    const chatItem = (c: DirectChatSummary): BuddyListItem => {
        const group = isGroup(c);
        const name = conversationName(c, playerId);
        const others = othersIn(c, playerId);
        const unread =
            !!c.lastSenderId &&
            c.lastSenderId !== playerId &&
            toMillis(c.lastAt) > (seen[c.chatGroupId] ?? 0);
        const fromMe = !!playerId && c.lastSenderId === playerId;
        const sender =
            group && !fromMe && c.lastSenderId
                ? c.members.find((m) => m.id === c.lastSenderId)?.name
                : undefined;
        const everyone = names(others.map((m) => m.name));
        return {
            id: `${CHAT}${c.chatGroupId}`,
            title: group
                ? `Open the conversation with ${everyone}`
                : `Open conversation with ${c.withName}`,
            unread,
            picture: group ? (
                <PictureStack
                    label={everyone}
                    pictures={others.map((m) => ({
                        key: m.id,
                        picture: <Portrait who={m.id} size={24} />,
                    }))}
                />
            ) : (
                <Portrait who={c.withId} size={32} />
            ),
            name: (
                <Stack as="span" direction="row" gap="xs" align="center">
                    {!group && (
                        <BuddyIcon
                            status={statusOf(
                                lifeById.get(c.withId),
                                selfById.get(c.withId),
                            )}
                            size="small"
                        />
                    )}
                    <Stack.Item truncate>{name}</Stack.Item>
                    {unread && (
                        <Text tone="caution" strong>
                            new
                        </Text>
                    )}
                </Stack>
            ),
            meta: shortTime(toMillis(c.lastAt)),
            details: (
                <>
                    {fromMe && <Text tone="muted">You: </Text>}
                    {sender && <Text tone="muted">{sender}: </Text>}
                    {c.lastMessage ? (
                        <MsnText text={clip(c.lastMessage, 90)} />
                    ) : (
                        <Text italic>No messages yet</Text>
                    )}
                </>
            ),
        };
    };

    const groups: BuddyListGroup[] = [
        ...(recent.length > 0
            ? [
                  {
                      id: 'chats',
                      title: 'Conversations',
                      items: recent.map(chatItem),
                  },
              ]
            : []),
        { id: 'online', title: 'Online', items: online.map(contactItem) },
        { id: 'busy', title: 'Busy', items: busy.map(contactItem) },
        ...(afk.length > 0
            ? [{ id: 'afk', title: 'AFK', items: afk.map(contactItem) }]
            : []),
    ];

    const chatOf = (id: string) =>
        id.startsWith(CHAT)
            ? chats.find((c) => c.chatGroupId === id.slice(CHAT.length))
            : undefined;

    const activate = (id: string) => {
        const chat = chatOf(id);
        if (chat) {
            openChat(chat);
            return;
        }
        const life = lifeById.get(id);
        if (life) void startChat(life.personaId, life.name);
    };

    const menuItems = (): MenuEntry[] => {
        if (!menuFor) return [];
        const chat = chatOf(menuFor);
        if (chat)
            return [
                {
                    id: 'open',
                    label: 'Open conversation',
                    icon: <Icon name="chat" />,
                    onSelect: () => openChat(chat),
                },
                ...(isGroup(chat)
                    ? [
                          { type: 'separator' as const },
                          {
                              id: 'leave',
                              label: 'Leave conversation',
                              icon: <Icon name="door" />,
                              tone: 'caution' as const,
                              onSelect: () => void leaveChat(chat),
                          },
                      ]
                    : []),
            ];
        const life = lifeById.get(menuFor);
        if (!life) return [];
        const im = {
            id: 'im',
            label:
                picked.length > 1
                    ? `Send an instant message to ${life.name}`
                    : 'Send an instant message',
            icon: <Icon name="chat" />,
            onSelect: () => void startChat(life.personaId, life.name),
        };
        if (picked.length < 2) return [im];
        return [
            {
                id: 'start',
                label: `Start a conversation with ${names(picked.map((p) => p.name))}`,
                icon: <Icon name="chat-rooms" />,
                onSelect: () => void startConversation(picked),
            },
            im,
        ];
    };

    const starting = openDm.isPending || startGroup.isPending;
    const hint =
        picked.length > 1
            ? `${picked.length} selected: start a conversation with them`
            : 'Double-click to chat, Ctrl-click for a group';

    return (
        <>
            <WindowLayout
                scroll="none"
                contentLabel="Contacts"
                toolbar={[
                    <Stack
                        key="me"
                        direction="row"
                        gap="md"
                        pad="md"
                        align="center"
                    >
                        <DisplayPicture who={playerName} size={48} />
                        <Stack.Item grow>
                            <Stack gap="xxs">
                                <Stack direction="row" gap="xs" align="center">
                                    <BuddyIcon status="online" />
                                    <Stack.Item truncate>
                                        <Text strong>{playerName}</Text>
                                    </Stack.Item>
                                    <Text tone="muted">(Online)</Text>
                                </Stack>
                                <Stack.Item truncate>
                                    <Text tone="muted" italic>
                                        {chats.length > 0
                                            ? `${chats.length} conversation${chats.length === 1 ? '' : 's'}`
                                            : '<Type a personal message>'}
                                    </Text>
                                </Stack.Item>
                            </Stack>
                        </Stack.Item>
                    </Stack>,
                    <Toolbar key="actions" aria-label="Messenger">
                        <ToolbarButton
                            icon={
                                <Icon
                                    name={
                                        picked.length > 1
                                            ? 'chat-rooms'
                                            : 'chat'
                                    }
                                />
                            }
                            title={
                                picked.length > 0
                                    ? `Start a conversation with ${names(picked.map((p) => p.name))}`
                                    : 'Pick one or more contacts first'
                            }
                            disabled={picked.length === 0 || starting}
                            onClick={() => void startConversation(picked)}
                        >
                            Start a conversation
                        </ToolbarButton>
                    </Toolbar>,
                    <Stack key="find" pad="sm">
                        <TextBox
                            type="search"
                            value={filter}
                            onChange={(e) => setFilter(e.target.value)}
                            placeholder="Find a contact..."
                            aria-label="Find a contact"
                        />
                    </Stack>,
                ]}
                statusBar={
                    <StatusBar>
                        <StatusBarField>
                            {contacts.length} contact
                            {contacts.length === 1 ? '' : 's'}
                        </StatusBarField>
                        <StatusBarField grow>{hint}</StatusBarField>
                    </StatusBar>
                }
            >
                <Stack pad="sm">
                    <Pane surface="sunken" pad="xs">
                        <BuddyList
                            aria-label="Contacts"
                            groups={groups}
                            selectionMode="multiple"
                            selected={selected}
                            onSelectionChange={(next) =>
                                setSelected(
                                    next.some((id) => !id.startsWith(CHAT))
                                        ? next.filter(
                                              (id) => !id.startsWith(CHAT),
                                          )
                                        : next,
                                )
                            }
                            collapsed={collapsed}
                            onCollapsedChange={setCollapsed}
                            onActivate={activate}
                            onItemContextMenu={(id, at, target) => {
                                setMenuFor(id);
                                menu.openAt(at, target);
                            }}
                        >
                            {contacts.length === 0 && (
                                <Text as="p" tone="muted" className="px-3 py-2">
                                    Nobody lives in this World yet. Add Personas
                                    to the Party and they'll show up here.
                                </Text>
                            )}
                            {problem && (
                                <Text
                                    as="p"
                                    tone="danger"
                                    className="px-3 py-1.5"
                                >
                                    {problem}
                                </Text>
                            )}
                        </BuddyList>
                    </Pane>
                </Stack>
            </WindowLayout>
            <ContextMenu
                {...menu.menuProps}
                label="Contact"
                items={menuItems()}
            />
        </>
    );
}
