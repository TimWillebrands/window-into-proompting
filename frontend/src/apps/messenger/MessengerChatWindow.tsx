import {
    ChatLog,
    ChatMessage,
    CommandButton,
    ContextMenu,
    Icon,
    Stack,
    StatusBar,
    StatusBarField,
    StatusText,
    Text,
    TextArea,
    Toolbar,
    ToolbarButton,
    useContextMenu,
    useWindowMinimised,
    WindowLayout,
} from '@partytown/ui';
import { useEffect, useMemo, useRef, useState } from 'react';
import {
    useGetWorldsPartyIdLivesSuspense,
    useGetWorldsPartyIdMessenger,
    useGetWorldsPartyIdSelves,
    usePostPartyIdPrompt,
} from '#api/party-zone';
import type { RealtimeChatMessage } from '#lib/realtime-store';
import { NARRATOR_PERSONA_ID } from '../../lib/driver';
import {
    heldBy,
    isShownInMessenger,
    typingState,
} from '../../lib/room-conversation';
import { useRoomConversation } from '../../lib/use-room-conversation';
import { usePlayerId, usePlayerOnAuto, WORLD_ID } from '../../lib/world';
import {
    BuddyIcon,
    type BuddyStatus,
    DisplayPicture,
    MsnText,
    STATUS_LABEL,
    statusOf,
    useMessengerSeen,
} from './msn-ui';

interface MessengerChatWindowProps {
    chatGroupId?: string;
    withName?: string;
    spectate?: boolean;
}

const EMOTICONS: Array<[string, string]> = [
    ['🙂', 'Smile'],
    ['😄', 'Open-mouthed'],
    ['😉', 'Wink'],
    ['😛', 'Tongue out'],
    ['😮', 'Surprised'],
    ['😢', 'Crying'],
    ['😠', 'Angry'],
    ['😎', 'Hot'],
    ['🤔', 'Thinking'],
    ['😳', 'Embarrassed'],
    ['❤️', 'Red heart'],
    ['💔', 'Broken heart'],
    ['👍', 'Thumbs up'],
    ['🌹', 'Red rose'],
    ['☕', 'Coffee cup'],
    ['🎉', 'Party'],
];

export default function MessengerChatWindow({
    chatGroupId,
    withName = 'Contact',
    spectate = false,
}: MessengerChatWindowProps) {
    if (!chatGroupId) {
        return (
            <Stack align="center" justify="center" pad="xl" className="h-full">
                <Text tone="muted">
                    Open a conversation from the Messenger buddy list.
                </Text>
            </Stack>
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
    const chatsQuery = useGetWorldsPartyIdMessenger(
        WORLD_ID,
        { personaId: playerId ?? undefined },
        { query: { enabled: !!playerId } },
    );
    const lives = livesQuery.data.data ?? [];
    const withId = chatsQuery.data?.data.find(
        (c) => c.chatGroupId === chatGroupId,
    )?.withId;
    const them = lives.find((l) => l.personaId === withId);
    const selvesQuery = useGetWorldsPartyIdSelves(WORLD_ID, {
        query: { refetchInterval: 60_000 },
    });
    const theirSelf = selvesQuery.data?.data.find(
        (s) => s.personaId === withId,
    );
    const theirNote = theirSelf?.doing ?? them?.lastActivity?.summary;
    const me = lives.find((l) => l.isPlayer || l.personaId === playerId);
    const myName = me?.name ?? 'You';
    const theirStatus: BuddyStatus = statusOf(them, theirSelf);

    const {
        messages: allMessages,
        activeIds,
        phases,
        declined,
        connection,
    } = useRoomConversation(WORLD_ID, chatGroupId);
    const messages = useMemo(
        () => allMessages.filter(isShownInMessenger),
        [allMessages],
    );
    const spectators = useMemo(
        () => [...new Set(messages.map((m) => m.senderId))],
        [messages],
    );
    const otherSide = typingState(phases);
    const isTyping = otherSide === 'typing';
    const isReading = otherSide === 'reading';

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

    const markSeen = useMessengerSeen((s) => s.markSeen);
    const minimised = useWindowMinimised();
    const lastSendAt = messages[messages.length - 1]?.sendAt ?? 0;
    // biome-ignore lint/correctness/useExhaustiveDependencies: re-mark when a message lands, a stream ends or the window comes back
    useEffect(() => {
        if (minimised) return;
        markSeen(chatGroupId, Math.max(Date.now(), lastSendAt));
    }, [
        chatGroupId,
        markSeen,
        lastSendAt,
        messages.length,
        activeIds.length,
        minimised,
    ]);

    const scrollRef = useRef<HTMLDivElement | null>(null);
    const nearBottom = useRef(true);
    const lastContent = messages[messages.length - 1]?.content;
    // biome-ignore lint/correctness/useExhaustiveDependencies: follow new and streaming content
    useEffect(() => {
        const el = scrollRef.current;
        if (el && nearBottom.current) el.scrollTop = el.scrollHeight;
    }, [messages.length, lastContent, isTyping]);

    const [draft, setDraft] = useState('');
    const emoticonMenu = useContextMenu();
    const emojiOpen = emoticonMenu.menuProps.open;
    const setEmojiOpen = (open: boolean) => {
        if (!open) emoticonMenu.menuProps.onClose();
    };
    const inputRef = useRef<HTMLTextAreaElement | null>(null);
    useEffect(() => inputRef.current?.focus(), []);

    const prompt = usePostPartyIdPrompt();
    const onAuto = usePlayerOnAuto();
    const canSend =
        !!playerId && !onAuto && !!draft.trim() && !prompt.isPending;

    const send = async () => {
        const text = draft.trim();
        if (!text || !playerId || onAuto || prompt.isPending) return;
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
        requestAnimationFrame(() => inputRef.current?.focus());
    };

    const statusLine = (() => {
        if (connection !== 'connected') return 'Connecting to the server…';
        if (onAuto) return 'Your character is on auto and answers for you.';
        if (isTyping) return `${withName} is typing a message.`;
        if (isReading) return `${withName} is reading your message…`;
        if (theirStatus === 'afk')
            return `${withName} is away from the keyboard and may not see this for a while.`;
        if (withId && heldBy(withId, messages, declined))
            return `${withName} saw this and will get back to you.`;
        if (prompt.isError) return 'Your message could not be delivered.';
        if (lastReceived)
            return `Last message received at ${new Date(lastReceived).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })} on ${new Date(lastReceived).toLocaleDateString()}.`;
        return 'Never give out your password or credit card number in an instant message conversation.';
    })();

    const composer = spectate ? (
        <StatusText
            variant="bar"
            icon={<Icon name="watching" />}
            className="justify-center text-center"
        >
            You're reading a private conversation. They can't see you.
        </StatusText>
    ) : (
        <Stack gap="xs">
            <Toolbar variant="bare" labels="none" aria-label="Formatting">
                <ToolbarButton
                    icon={<Icon name="emoticon" />}
                    hasMenu
                    title="Select an emoticon"
                    aria-label="Select an emoticon"
                    aria-expanded={emojiOpen}
                    onClick={(e) => {
                        if (emojiOpen) {
                            setEmojiOpen(false);
                            return;
                        }
                        const r = e.currentTarget.getBoundingClientRect();
                        emoticonMenu.openAt(
                            { x: r.left, y: r.bottom },
                            e.currentTarget,
                        );
                    }}
                />
            </Toolbar>
            <ContextMenu
                {...emoticonMenu.menuProps}
                label="Emoticons"
                items={EMOTICONS.map(([e, name]) => ({
                    id: name,
                    label: `${e}  ${name}`,
                    onSelect: () => insertEmoji(e),
                }))}
            />
            <Stack direction="row" gap="sm">
                <Stack.Item as="div" grow>
                    <TextArea
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
                        className="w-full resize-none"
                    />
                </Stack.Item>
                <CommandButton
                    onClick={() => void send()}
                    disabled={!canSend}
                    isDefault
                    size="large"
                    type="button"
                >
                    Send
                </CommandButton>
            </Stack>
        </Stack>
    );

    return (
        <WindowLayout
            scroll="none"
            contentLabel={`Conversation with ${withName}`}
            toolbar={
                <Stack direction="row" gap="xs" pad="sm" align="center">
                    <Text tone="muted">To:</Text>
                    <BuddyIcon status={theirStatus} />
                    <Stack.Item truncate>
                        <Text strong>{withName}</Text>
                    </Stack.Item>
                    <Text tone="muted">({STATUS_LABEL[theirStatus]})</Text>
                    {theirNote && (
                        <Stack.Item grow truncate title={theirNote}>
                            <Text tone="muted" italic>
                                - {theirNote}
                            </Text>
                        </Stack.Item>
                    )}
                </Stack>
            }
            statusBar={
                <StatusBar>
                    <StatusBarField
                        grow
                        icon={isTyping ? <Icon name="edit" /> : undefined}
                    >
                        {statusLine}
                    </StatusBarField>
                </StatusBar>
            }
        >
            <Stack direction="row" gap="md" pad="md">
                <Stack gap="sm" grow>
                    <ChatLog
                        ref={scrollRef}
                        aria-label={`Conversation with ${withName}`}
                        onScroll={(e) => {
                            const el = e.currentTarget;
                            nearBottom.current =
                                el.scrollHeight -
                                    el.scrollTop -
                                    el.clientHeight <
                                40;
                        }}
                        className="flex-1 px-2 py-1.5"
                    >
                        {messages.length === 0 && !isTyping && (
                            <Text tone="muted" className="py-1">
                                Say hi to {withName}!
                            </Text>
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
                    </ChatLog>
                    {composer}
                </Stack>
                <Stack justify="between" className="@max-sm:hidden">
                    <DisplayPicture
                        who={withId ?? spectators[0] ?? withName}
                        size={96}
                    />
                    <DisplayPicture
                        who={spectate && spectators[1] ? spectators[1] : myName}
                        size={96}
                    />
                </Stack>
            </Stack>
        </WindowLayout>
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
            <ChatMessage layout="transcript" kind="emote" sender={`* ${name}`}>
                <MsnText text={message.content ?? ''} />
            </ChatMessage>
        );
    }

    const failed = !message.content?.trim() && message.error;
    return (
        <ChatMessage
            layout="transcript"
            sender={`${name} says:`}
            own={mine}
            continued={!showName}
            live={streaming && !failed}
            error={failed ? `${name} couldn't reply right now.` : undefined}
        >
            {failed ? null : <MsnText text={message.content ?? ''} />}
        </ChatMessage>
    );
}
