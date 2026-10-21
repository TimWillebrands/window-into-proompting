import { useEffect, useMemo, useRef, useState } from 'react';
import {
    useGetWorldsPartyIdLivesSuspense,
    useGetWorldsPartyIdMessenger,
    usePostPartyIdPrompt,
} from '#api/party-zone';
import {
    type RealtimeChatMessage,
    useActiveGenerationPhases,
    useChatGroupGenerationState,
    useChatGroupMessages,
    useRealtimeConnectionStatus,
    useRealtimeStoreActions,
} from '#lib/realtime-store';
import { NARRATOR_PERSONA_ID } from '../../lib/driver';
import { usePlayerId, WORLD_ID } from '../../lib/world';
import {
    BuddyIcon,
    type BuddyStatus,
    DisplayPicture,
    MsnText,
    personaAvatar,
    playerAvatar,
    STATUS_LABEL,
    useMessengerSeen,
} from './msn-ui';

interface MessengerChatWindowProps {
    chatGroupId?: string;
    withName?: string;
    /** Read-only view of a DM the player isn't part of (opened from World Monitor). */
    spectate?: boolean;
}

const EMOJI = [
    '🙂',
    '😄',
    '😉',
    '😛',
    '😮',
    '😢',
    '😠',
    '😎',
    '🤔',
    '😳',
    '❤️',
    '💔',
    '👍',
    '🌹',
    '☕',
    '🎉',
];

const visible = (m: RealtimeChatMessage) =>
    !!m.content?.trim() || (!!m.error && m.senderType !== 'user');

export default function MessengerChatWindow({
    chatGroupId,
    withName = 'Contact',
    spectate = false,
}: MessengerChatWindowProps) {
    if (!chatGroupId) {
        return (
            <div className="app-surface flex h-full items-center justify-center p-4 text-center text-[#6d7b91]">
                Open a conversation from the Messenger buddy list.
            </div>
        );
    }
    return (
        <Conversation
            chatGroupId={chatGroupId}
            withName={withName}
            spectate={spectate}
        />
    );
}

function Conversation({
    chatGroupId,
    withName,
    spectate,
}: {
    chatGroupId: string;
    withName: string;
    spectate: boolean;
}) {
    const playerId = usePlayerId();
    const livesQuery = useGetWorldsPartyIdLivesSuspense(WORLD_ID);
    const chatsQuery = useGetWorldsPartyIdMessenger(WORLD_ID, {
        personaId: playerId ?? undefined,
    });
    const lives = livesQuery.data.data ?? [];
    const withId = chatsQuery.data?.data.find(
        (c) => c.chatGroupId === chatGroupId,
    )?.withId;
    const them = lives.find((l) => l.personaId === withId);
    const me = lives.find((l) => l.isPlayer || l.personaId === playerId);
    const myName = me?.name ?? 'You';
    const theirStatus: BuddyStatus = them?.busy ? 'busy' : 'online';

    const { connectPartyRealtime, disconnectPartyRealtime } =
        useRealtimeStoreActions();
    useEffect(() => {
        connectPartyRealtime(WORLD_ID);
        return () => disconnectPartyRealtime(WORLD_ID);
    }, [connectPartyRealtime, disconnectPartyRealtime]);
    const connection = useRealtimeConnectionStatus(WORLD_ID);

    const allMessages = useChatGroupMessages(chatGroupId);
    const activeIds = useChatGroupGenerationState(chatGroupId);
    const phases = useActiveGenerationPhases(chatGroupId);
    const messages = useMemo(() => allMessages.filter(visible), [allMessages]);
    const isTyping = phases.some(
        (p) => p.phase === 'typing' || p.phase === 'streaming',
    );
    const isReading = !isTyping && phases.some((p) => p.phase === 'deciding');

    const isMine = (m: RealtimeChatMessage) =>
        !spectate &&
        ((!!playerId && m.senderId === playerId) ||
            (m.senderType === 'user' && m.senderId !== withId));
    const nameOf = (m: RealtimeChatMessage) => {
        if (isMine(m)) return myName;
        if (m.senderId === withId) return withName;
        if (m.senderId === NARRATOR_PERSONA_ID) return 'Narrator';
        return lives.find((l) => l.personaId === m.senderId)?.name ?? withName;
    };

    const lastReceived = [...messages]
        .reverse()
        .find((m) => !isMine(m) && m.sendAt)?.sendAt;

    // Opening the window counts as reading it.
    const markSeen = useMessengerSeen((s) => s.markSeen);
    const lastSendAt = messages[messages.length - 1]?.sendAt ?? 0;
    // biome-ignore lint/correctness/useExhaustiveDependencies: re-mark when a message lands or a stream ends
    useEffect(() => {
        markSeen(chatGroupId, Math.max(Date.now(), lastSendAt));
    }, [chatGroupId, markSeen, lastSendAt, messages.length, activeIds.length]);

    const scrollRef = useRef<HTMLDivElement | null>(null);
    const nearBottom = useRef(true);
    const lastContent = messages[messages.length - 1]?.content;
    // biome-ignore lint/correctness/useExhaustiveDependencies: follow new and streaming content
    useEffect(() => {
        const el = scrollRef.current;
        if (el && nearBottom.current) el.scrollTop = el.scrollHeight;
    }, [messages.length, lastContent, isTyping]);

    const [draft, setDraft] = useState('');
    const [emojiOpen, setEmojiOpen] = useState(false);
    const inputRef = useRef<HTMLTextAreaElement | null>(null);
    useEffect(() => inputRef.current?.focus(), []);

    const prompt = usePostPartyIdPrompt();
    const canSend = !!playerId && !!draft.trim() && !prompt.isPending;

    const send = async () => {
        const text = draft.trim();
        if (!text || !playerId || prompt.isPending) return;
        setDraft('');
        nearBottom.current = true;
        try {
            await prompt.mutateAsync({
                id: WORLD_ID,
                data: { chatGroupId, prompt: text, senderId: playerId },
            });
        } catch {
            setDraft((d) => d || text);
        }
        inputRef.current?.focus();
    };

    const insertEmoji = (e: string) => {
        setDraft((d) => `${d}${d && !d.endsWith(' ') ? ' ' : ''}${e} `);
        setEmojiOpen(false);
        inputRef.current?.focus();
    };

    const statusLine = (() => {
        if (connection !== 'connected') return 'Connecting to the server…';
        if (isTyping) return `${withName} is typing a message.`;
        if (isReading) return `${withName} is reading your message…`;
        if (prompt.isError) return 'Your message could not be delivered.';
        if (lastReceived)
            return `Last message received at ${new Date(lastReceived).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })} on ${new Date(lastReceived).toLocaleDateString()}.`;
        return 'Never give out your password or credit card number in an instant message conversation.';
    })();

    return (
        <div
            className="app-surface flex h-full flex-col"
            style={{
                background:
                    'linear-gradient(180deg, #dbe7fb 0%, #eaf1fc 60px, #f3f7fd 100%)',
            }}
        >
            {/* To: header */}
            <div
                className="flex items-center gap-1 px-2 py-[5px]"
                style={{
                    borderBottom: '1px solid #b9cdea',
                    background:
                        'linear-gradient(180deg, #f5f9ff 0%, #dbe7fb 100%)',
                }}
            >
                <span style={{ color: '#4a6ea9' }}>To:</span>
                <BuddyIcon status={theirStatus} size={14} />
                <span className="truncate" style={{ fontWeight: 700 }}>
                    {withName}
                </span>
                <span style={{ color: '#7c7c7c' }}>
                    ({STATUS_LABEL[theirStatus]})
                </span>
                {them?.lastActivity?.summary && (
                    <span
                        className="min-w-0 flex-1 truncate"
                        style={{ color: '#7c8aa3', fontStyle: 'italic' }}
                        title={them.lastActivity.summary}
                    >
                        - {them.lastActivity.summary}
                    </span>
                )}
            </div>

            <div className="flex min-h-0 flex-1 gap-2 p-2">
                {/* Left: history + compose */}
                <div className="flex min-w-0 flex-1 flex-col gap-[6px]">
                    <div
                        ref={scrollRef}
                        onScroll={(e) => {
                            const el = e.currentTarget;
                            nearBottom.current =
                                el.scrollHeight -
                                    el.scrollTop -
                                    el.clientHeight <
                                40;
                        }}
                        className="min-h-0 flex-1 overflow-y-auto px-2 py-[6px]"
                        style={{
                            background: '#fff',
                            border: '1px solid #9db6dc',
                            borderRadius: 4,
                            fontSize: 12,
                            fontFamily: 'Tahoma, Verdana, sans-serif',
                        }}
                    >
                        {messages.length === 0 && !isTyping && (
                            <div
                                style={{ color: '#9aa6b8', fontSize: 11 }}
                                className="py-1"
                            >
                                Say hi to {withName}!
                            </div>
                        )}
                        {messages.map((m, i) => {
                            const prev = messages[i - 1];
                            const showName =
                                !prev ||
                                prev.senderId !== m.senderId ||
                                prev.kind === 'emote' ||
                                m.kind === 'emote';
                            return (
                                <MessageLine
                                    key={m.messageId}
                                    message={m}
                                    name={nameOf(m)}
                                    mine={isMine(m)}
                                    showName={showName}
                                    streaming={activeIds.includes(m.messageId)}
                                />
                            );
                        })}
                    </div>

                    {/* Compose */}
                    {spectate ? (
                        <div
                            className="px-2 py-2 text-center"
                            style={{
                                border: '1px dashed #9db6dc',
                                borderRadius: 4,
                                background: '#fff',
                                color: '#4a6ea9',
                            }}
                        >
                            👀 You're reading a private conversation. They can't
                            see you.
                        </div>
                    ) : (
                        <div
                            className="relative flex flex-col"
                            style={{
                                border: '1px solid #9db6dc',
                                borderRadius: 4,
                                background: '#fff',
                            }}
                        >
                            <div
                                className="flex items-center gap-1 px-1 py-[2px]"
                                style={{
                                    borderBottom: '1px solid #dbe5f3',
                                    background:
                                        'linear-gradient(180deg, #ffffff 0%, #eef3fb 100%)',
                                    borderRadius: '4px 4px 0 0',
                                }}
                            >
                                <button
                                    type="button"
                                    className="xp-bare rounded-[3px] border border-transparent px-1 hover:border-[#a9c4ea] hover:bg-[#e6effc]"
                                    style={{ fontSize: 13, lineHeight: '16px' }}
                                    title="Select an emoticon"
                                    aria-expanded={emojiOpen}
                                    onClick={() => setEmojiOpen((o) => !o)}
                                >
                                    🙂 <span style={{ fontSize: 8 }}>▼</span>
                                </button>
                            </div>
                            {emojiOpen && (
                                <div
                                    className="absolute bottom-full left-0 z-10 mb-1 grid grid-cols-8 gap-[2px] p-1"
                                    style={{
                                        background: '#fff',
                                        border: '1px solid #7f9db9',
                                        boxShadow:
                                            '2px 2px 4px rgba(0,0,0,0.25)',
                                    }}
                                >
                                    {EMOJI.map((e) => (
                                        <button
                                            key={e}
                                            type="button"
                                            className="xp-bare h-[22px] w-[22px] rounded-[2px] border border-transparent hover:border-[#316ac5] hover:bg-[#c1d2ee]"
                                            style={{ fontSize: 14 }}
                                            onClick={() => insertEmoji(e)}
                                        >
                                            {e}
                                        </button>
                                    ))}
                                </div>
                            )}
                            <div className="flex items-stretch gap-[6px] p-1">
                                <textarea
                                    ref={inputRef}
                                    value={draft}
                                    onChange={(e) => setDraft(e.target.value)}
                                    onKeyDown={(e) => {
                                        if (e.key === 'Enter' && !e.shiftKey) {
                                            e.preventDefault();
                                            void send();
                                        } else if (e.key === 'Escape') {
                                            setEmojiOpen(false);
                                        }
                                    }}
                                    rows={3}
                                    maxLength={4000}
                                    aria-label={`Message to ${withName}`}
                                    className="min-w-0 flex-1 resize-none"
                                    style={{
                                        border: 'none',
                                        outline: 'none',
                                        padding: '2px 3px',
                                        fontSize: 12,
                                        fontFamily:
                                            'Tahoma, Verdana, sans-serif',
                                    }}
                                />
                                <button
                                    type="button"
                                    onClick={() => void send()}
                                    disabled={!canSend}
                                    style={{
                                        width: 64,
                                        fontWeight: 700,
                                        padding: '0 6px',
                                    }}
                                >
                                    Send
                                </button>
                            </div>
                        </div>
                    )}
                </div>

                {/* Right: display pictures */}
                <div className="flex flex-col justify-between">
                    <DisplayPicture
                        src={
                            withId
                                ? personaAvatar(withId, 72)
                                : playerAvatar(withName, 72)
                        }
                        size={72}
                    />
                    <DisplayPicture src={playerAvatar(myName, 72)} size={72} />
                </div>
            </div>

            {/* Status bar */}
            <div
                className="flex items-center gap-1 truncate px-2 py-[3px]"
                style={{
                    borderTop: '1px solid #b9cdea',
                    background:
                        'linear-gradient(180deg, #f4f8fe 0%, #dbe7fa 100%)',
                    color: isTyping ? '#1d3f73' : '#6d7b91',
                    minHeight: 20,
                }}
            >
                {isTyping && <span aria-hidden="true">✏️</span>}
                <span className="truncate">{statusLine}</span>
            </div>
        </div>
    );
}

function MessageLine({
    message,
    name,
    mine,
    showName,
    streaming,
}: {
    message: RealtimeChatMessage;
    name: string;
    mine: boolean;
    showName: boolean;
    streaming: boolean;
}) {
    if (message.kind === 'emote') {
        return (
            <div
                className="my-[3px]"
                style={{ color: '#7c7c7c', fontStyle: 'italic' }}
            >
                * {name} <MsnText text={message.content ?? ''} />
            </div>
        );
    }

    const failed = !message.content?.trim() && message.error;
    return (
        <div className={showName ? 'mt-[6px]' : ''}>
            {showName && (
                <div style={{ color: '#6d6d6d', fontSize: 11 }}>
                    {name} says:
                </div>
            )}
            <div
                className="pl-3 whitespace-pre-wrap break-words"
                style={{
                    color: failed ? '#b8241a' : mine ? '#1d3f73' : '#000',
                    fontStyle: failed ? 'italic' : 'normal',
                }}
            >
                {failed ? (
                    `${name} couldn't reply right now.`
                ) : (
                    <MsnText text={message.content ?? ''} />
                )}
                {streaming && !failed && (
                    <span
                        className="animate-pulse"
                        style={{ color: '#9aa6b8' }}
                    >
                        {' '}
                        ▍
                    </span>
                )}
            </div>
        </div>
    );
}
