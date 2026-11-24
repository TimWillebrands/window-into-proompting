import {
    Expander,
    Pane,
    ContactRow as Row,
    Stack,
    StatusBar,
    StatusBarField,
    Text,
    TextBox,
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
} from '#api/party-zone';
import type { DirectChatSummary } from '../../api/model/directChatSummary';
import type { LifeSummary } from '../../api/model/lifeSummary';
import type { SelfSummary } from '../../api/model/selfSummary';
import Portrait from '../../components/Portrait';
import { useOpenDirectChat, usePlayerId, WORLD_ID } from '../../lib/world';
import {
    BuddyIcon,
    type BuddyStatus,
    clip,
    DisplayPicture,
    MsnText,
    STATUS_LABEL,
    shortTime,
    statusOf,
    toMillis,
    useMessengerSeen,
} from './msn-ui';

export default function MessengerApp() {
    const queryClient = useQueryClient();
    const playerId = usePlayerId();
    const openDirectChat = useOpenDirectChat();
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
    const [openGroups, setOpenGroups] = useState({
        chats: true,
        online: true,
        busy: true,
        afk: true,
    });
    const toggle = (k: keyof typeof openGroups) =>
        setOpenGroups((g) => ({ ...g, [k]: !g[k] }));

    const openDm = usePostWorldsPartyIdMessenger({
        mutation: {
            onSuccess: () =>
                queryClient.invalidateQueries({
                    queryKey: getGetWorldsPartyIdMessengerQueryKey(WORLD_ID),
                }),
        },
    });

    const startChat = async (personaId: string, name: string) => {
        const existing = chats.find((c) => c.withId === personaId);
        if (existing) {
            openDirectChat(existing.chatGroupId, name);
            return;
        }
        const res = await openDm.mutateAsync({
            partyId: WORLD_ID,
            data: { withPersonaId: personaId, fromPersonaId: playerId },
        });
        if (res.data.id) openDirectChat(res.data.id, name);
    };

    const q = filter.trim().toLowerCase();
    const match = (name: string) => !q || name.toLowerCase().includes(q);
    const online = contacts.filter((c) => !c.afk && !c.busy && match(c.name));
    const busy = contacts.filter((c) => !c.afk && c.busy && match(c.name));
    const afk = contacts.filter((c) => c.afk && match(c.name));
    const recent = chats.filter((c) => match(c.withName));

    const playerName = player?.name ?? 'You';

    const contactRows = (list: LifeSummary[]) =>
        list.map((c) => (
            <ContactRow
                key={c.personaId}
                life={c}
                self={selfById.get(c.personaId)}
                pending={
                    openDm.isPending &&
                    openDm.variables?.data.withPersonaId === c.personaId
                }
                onOpen={() => startChat(c.personaId, c.name)}
            />
        ));

    return (
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
                    <StatusBarField grow>
                        Click a contact to chat
                    </StatusBarField>
                </StatusBar>
            }
        >
            <Stack pad="sm">
                <Pane
                    surface="sunken"
                    pad="xs"
                    role="group"
                    aria-label="Contacts"
                >
                    <Stack gap="sm">
                        {recent.length > 0 && (
                            <Expander
                                title="Conversations"
                                count={recent.length}
                                open={openGroups.chats}
                                onOpenChange={() => toggle('chats')}
                            >
                                {recent.map((c) => (
                                    <ConversationRow
                                        key={c.chatGroupId}
                                        chat={c}
                                        status={statusOf(
                                            lifeById.get(c.withId),
                                            selfById.get(c.withId),
                                        )}
                                        unread={
                                            !!c.lastSenderId &&
                                            c.lastSenderId !== playerId &&
                                            toMillis(c.lastAt) >
                                                (seen[c.chatGroupId] ?? 0)
                                        }
                                        fromMe={
                                            !!playerId &&
                                            c.lastSenderId === playerId
                                        }
                                        onOpen={() =>
                                            openDirectChat(
                                                c.chatGroupId,
                                                c.withName,
                                            )
                                        }
                                    />
                                ))}
                            </Expander>
                        )}

                        <Expander
                            title="Online"
                            count={online.length}
                            open={openGroups.online}
                            onOpenChange={() => toggle('online')}
                        >
                            {contactRows(online)}
                        </Expander>

                        <Expander
                            title="Busy"
                            count={busy.length}
                            open={openGroups.busy}
                            onOpenChange={() => toggle('busy')}
                        >
                            {contactRows(busy)}
                        </Expander>

                        {afk.length > 0 && (
                            <Expander
                                title="AFK"
                                count={afk.length}
                                open={openGroups.afk}
                                onOpenChange={() => toggle('afk')}
                            >
                                {contactRows(afk)}
                            </Expander>
                        )}

                        {contacts.length === 0 && (
                            <Text as="p" tone="muted" className="px-3 py-2">
                                Nobody lives in this World yet. Add Personas to
                                the Party and they'll show up here.
                            </Text>
                        )}
                        {openDm.isError && (
                            <Text as="p" tone="danger" className="px-3 py-1.5">
                                Couldn't start the conversation. Try again.
                            </Text>
                        )}
                    </Stack>
                </Pane>
            </Stack>
        </WindowLayout>
    );
}

function ContactRow({
    life,
    self,
    pending,
    onOpen,
}: {
    life: LifeSummary;
    self?: SelfSummary;
    pending: boolean;
    onOpen: () => void;
}) {
    const status = statusOf(life, self);
    const note = self?.doing ?? life.lastActivity?.summary;
    return (
        <Row
            className="pl-4"
            title={`Send an instant message to ${life.name}`}
            onClick={onOpen}
            disabled={pending}
            aria-busy={pending || undefined}
            picture={<BuddyIcon status={status} />}
            name={
                <>
                    {life.name}
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
            }
            meta={pending ? '…' : undefined}
        />
    );
}

function ConversationRow({
    chat,
    status,
    unread,
    fromMe,
    onOpen,
}: {
    chat: DirectChatSummary;
    status: BuddyStatus;
    unread: boolean;
    fromMe: boolean;
    onOpen: () => void;
}) {
    return (
        <Row
            className="pl-4"
            title={`Open conversation with ${chat.withName}`}
            onClick={onOpen}
            unread={unread}
            picture={<Portrait who={chat.withId} size={32} />}
            name={
                <Stack as="span" direction="row" gap="xs" align="center">
                    <BuddyIcon status={status} size="small" />
                    <Stack.Item truncate>{chat.withName}</Stack.Item>
                    {unread && (
                        <Text tone="caution" strong>
                            new
                        </Text>
                    )}
                </Stack>
            }
            meta={shortTime(toMillis(chat.lastAt))}
            details={
                <>
                    {fromMe && <Text tone="muted">You: </Text>}
                    {chat.lastMessage ? (
                        <MsnText text={clip(chat.lastMessage, 90)} />
                    ) : (
                        <Text italic>No messages yet</Text>
                    )}
                </>
            }
        />
    );
}
