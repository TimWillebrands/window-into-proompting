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
import { useOpenDirectChat, usePlayerId, WORLD_ID } from '../../lib/world';
import {
    BuddyIcon,
    type BuddyStatus,
    clip,
    DisplayPicture,
    GroupHeader,
    MsnText,
    personaAvatar,
    playerAvatar,
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
    const online = contacts.filter((c) => !c.busy && match(c.name));
    const busy = contacts.filter((c) => c.busy && match(c.name));
    const recent = chats.filter((c) => match(c.withName));

    const playerName = player?.name ?? 'You';

    return (
        <div
            className="app-surface flex h-full flex-col"
            style={{
                background:
                    'linear-gradient(180deg, #d7e5fa 0%, #eef4fd 90px, #ffffff 160px)',
            }}
        >
            {/* Me header */}
            <div
                className="flex items-center gap-2 px-2 pt-2 pb-2"
                style={{ borderBottom: '1px solid #b9cdea' }}
            >
                <DisplayPicture src={playerAvatar(playerName, 40)} size={40} />
                <div className="min-w-0 flex-1">
                    <div className="flex items-center gap-1">
                        <BuddyIcon status="online" size={14} />
                        <span
                            className="truncate"
                            style={{
                                fontWeight: 700,
                                fontSize: 12,
                                color: '#000',
                            }}
                        >
                            {playerName}
                        </span>
                        <span style={{ color: '#4a6ea9' }}>(Online)</span>
                    </div>
                    <div
                        className="truncate"
                        style={{
                            color: '#6d7b91',
                            fontStyle: 'italic',
                            marginTop: 2,
                        }}
                    >
                        {chats.length > 0
                            ? `${chats.length} conversation${chats.length === 1 ? '' : 's'}`
                            : '<Type a personal message>'}
                    </div>
                </div>
            </div>

            {/* Search */}
            <div className="px-2 py-[5px]" style={{ background: '#eef4fd' }}>
                <input
                    type="search"
                    value={filter}
                    onChange={(e) => setFilter(e.target.value)}
                    placeholder="Find a contact..."
                    className="w-full"
                    style={{
                        border: '1px solid #9db6dc',
                        borderRadius: 2,
                        padding: '3px 5px',
                    }}
                />
            </div>

            {/* Lists */}
            <div
                className="flex-1 overflow-y-auto pb-2"
                style={{ background: '#fff' }}
            >
                {recent.length > 0 && (
                    <>
                        <GroupHeader
                            label="Conversations"
                            count={recent.length}
                            open={openGroups.chats}
                            onToggle={() => toggle('chats')}
                        />
                        {openGroups.chats &&
                            recent.map((c) => (
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
                    </>
                )}

                <GroupHeader
                    label="Online"
                    count={online.length}
                    open={openGroups.online}
                    onToggle={() => toggle('online')}
                />
                {openGroups.online &&
                    online.map((c) => (
                        <ContactRow
                            key={c.personaId}
                            life={c}
                            self={selfById.get(c.personaId)}
                            pending={
                                openDm.isPending &&
                                openDm.variables?.data.withPersonaId ===
                                    c.personaId
                            }
                            onOpen={() => startChat(c.personaId, c.name)}
                        />
                    ))}

                <GroupHeader
                    label="Busy"
                    count={busy.length}
                    open={openGroups.busy}
                    onToggle={() => toggle('busy')}
                />
                {openGroups.busy &&
                    busy.map((c) => (
                        <ContactRow
                            key={c.personaId}
                            life={c}
                            self={selfById.get(c.personaId)}
                            pending={
                                openDm.isPending &&
                                openDm.variables?.data.withPersonaId ===
                                    c.personaId
                            }
                            onOpen={() => startChat(c.personaId, c.name)}
                        />
                    ))}

                {contacts.length === 0 && (
                    <p style={{ color: '#6d7b91', padding: '8px 12px' }}>
                        Nobody lives in this World yet. Add Personas to the
                        Party and they'll show up here.
                    </p>
                )}
                {openDm.isError && (
                    <p style={{ color: '#b8241a', padding: '6px 12px' }}>
                        Couldn't start the conversation. Try again.
                    </p>
                )}
            </div>

            {/* Footer */}
            <div
                className="flex items-center justify-between px-2 py-[3px]"
                style={{
                    borderTop: '1px solid #b9cdea',
                    background:
                        'linear-gradient(180deg, #f4f8fe 0%, #dbe7fa 100%)',
                    color: '#4a6ea9',
                }}
            >
                <span>
                    {contacts.length} contact{contacts.length === 1 ? '' : 's'}
                </span>
                <span style={{ color: '#7c8aa3' }}>
                    Click a contact to chat
                </span>
            </div>
        </div>
    );
}

const ROW_CLASS =
    'xp-bare mx-1 flex w-[calc(100%-8px)] gap-[6px] pr-1 pl-4 rounded-[3px] border border-transparent bg-transparent text-left cursor-pointer hover:border-[#a9c4ea] hover:bg-[#e6effc] disabled:cursor-wait';

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
    // Their offscreen day is the personal message; World activity when there's no day yet.
    const note = self?.doing ?? life.lastActivity?.summary;
    return (
        <button
            type="button"
            className={`${ROW_CLASS} items-center py-[2px]`}
            title={`Send an instant message to ${life.name}`}
            onClick={onOpen}
            disabled={pending}
        >
            <BuddyIcon status={status} />
            <span className="min-w-0 flex-1 truncate">
                <span style={{ color: '#000' }}>{life.name}</span>
                {status !== 'online' && (
                    <span style={{ color: '#7c7c7c' }}>
                        {' '}
                        ({STATUS_LABEL[status]})
                    </span>
                )}
                {note && (
                    <span style={{ color: '#7c8aa3', fontStyle: 'italic' }}>
                        {' '}
                        - {note}
                    </span>
                )}
            </span>
            {pending && <span style={{ color: '#7c8aa3' }}>…</span>}
        </button>
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
        <button
            type="button"
            className={`${ROW_CLASS} items-start py-[3px]`}
            title={`Open conversation with ${chat.withName}`}
            onClick={onOpen}
        >
            <img
                src={personaAvatar(chat.withId, 28)}
                alt=""
                width={28}
                height={28}
                style={{
                    width: 28,
                    height: 28,
                    borderRadius: 3,
                    border: '1px solid #b5c8e8',
                    background: '#fff',
                    flexShrink: 0,
                }}
            />
            <span className="min-w-0 flex-1">
                <span className="flex items-center gap-1">
                    <BuddyIcon status={status} size={12} />
                    <span
                        className="truncate"
                        style={{
                            fontWeight: unread ? 700 : 400,
                            color: '#000',
                        }}
                    >
                        {chat.withName}
                    </span>
                    {unread && (
                        <span
                            style={{
                                background: '#f5a623',
                                color: '#fff',
                                fontSize: 9,
                                fontWeight: 700,
                                borderRadius: 6,
                                padding: '0 4px',
                                lineHeight: '12px',
                            }}
                        >
                            new
                        </span>
                    )}
                    <span
                        className="ml-auto"
                        style={{ color: '#9aa6b8', fontSize: 10 }}
                    >
                        {shortTime(toMillis(chat.lastAt))}
                    </span>
                </span>
                <span
                    className="block truncate"
                    style={{
                        color: unread ? '#1d3f73' : '#6d7b91',
                        fontWeight: unread ? 700 : 400,
                    }}
                >
                    {fromMe && <span style={{ color: '#9aa6b8' }}>You: </span>}
                    {chat.lastMessage ? (
                        <MsnText text={clip(chat.lastMessage, 90)} />
                    ) : (
                        <em>No messages yet</em>
                    )}
                </span>
            </span>
        </button>
    );
}
