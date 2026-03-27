import { useHotkey } from '@tanstack/react-hotkeys';
import { useQueryClient } from '@tanstack/react-query';
import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import DOMPurify from 'dompurify';
import * as smd from 'streaming-markdown';
import type { Persona } from '../../api/model';
import {
    useDeletePartyIdChatGroupsChatGroupIdMessagesAfterMessageId,
    useDeletePartyIdChatGroupsChatGroupIdMessagesMessageId,
    useGetPartyIdSuspense,
    usePostPartyIdCancel,
    usePostPartyIdProceed,
    usePostPartyIdPrompt,
    usePostPartyIdRepromptMessageId,
    usePutPartyIdParticipants,
} from '../../api/party-zone';
import PeoplePicker from '../../components/chat/PeoplePicker';
import { ROOT_PARTY_ID } from '../../lib/chat-api';
import {
    type GenerationPhase,
    type RealtimeChatMessage,
    type RealtimeConnectionStatus,
    useActiveGenerationInfo,
    useChatGroupGenerationState,
    useRealtimeConnectionStatus,
    useRealtimeStoreActions,
} from '../../lib/realtime-store';

export interface ChatViewProps {
    chatGroupId: string;
    partyName?: string;
}

interface GroupChatWindowProps {
    partyId?: string;
    chatGroupId?: string;
    partyName?: string;
}

const DEFAULT_PROVIDER = 'openrouter';
const DEFAULT_MODEL = 'openai/gpt-4o-mini';

export default function GroupChatWindow({
    chatGroupId,
    partyName,
}: GroupChatWindowProps) {
    if (!chatGroupId) {
        return (
            <div
                className="app-surface h-full flex items-center justify-center"
                style={{ background: '#ECE9D8', color: '#808080' }}
            >
                Open a chat group from the Chat Launcher to start messaging.
            </div>
        );
    }

    return <ChatView chatGroupId={chatGroupId} partyName={partyName} />;
}

export function ChatView({ chatGroupId, partyName }: ChatViewProps) {
    const apiPartyId = ROOT_PARTY_ID;
    const queryClient = useQueryClient();
    const [messages, setMessages] = useState<RealtimeChatMessage[]>([]);
    const [inputValue, setInputValue] = useState('');
    const [selectedPersonaId, setSelectedPersonaId] = useState('');
    const [provider, setProvider] = useState(DEFAULT_PROVIDER);
    const [model, setModel] = useState(DEFAULT_MODEL);

    // Participant management – backed by actual party state
    const [participantPersonaIds, setParticipantPersonaIds] = useState<
        string[]
    >([]);
    const [savedParticipantPersonaIds, setSavedParticipantPersonaIds] =
        useState<string[]>([]);
    const participantsInitialized = useRef(false);
    const lastSavedUserPersonaId = useRef<string | null>(null);

    const scrollRef = useRef<HTMLDivElement | null>(null);
    const [isNearBottom, setIsNearBottom] = useState(true);
    const [unreadCount, setUnreadCount] = useState(0);

    const activeGenerations = useChatGroupGenerationState(chatGroupId ?? '');
    const connectionStatus = useRealtimeConnectionStatus(apiPartyId);
    const generationInfo = useActiveGenerationInfo(chatGroupId ?? '');
    const {
        connectPartyRealtime,
        disconnectPartyRealtime,
        subscribeToChatGroup,
    } = useRealtimeStoreActions();

    const partyDetailsQuery = useGetPartyIdSuspense(apiPartyId);

    // Seed participant state from server data on first load
    useEffect(() => {
        if (participantsInitialized.current) return;
        if (partyDetailsQuery.data.status !== 200) return;
        const serverIds = (partyDetailsQuery.data.data.party.participants ?? [])
            .filter((p) => !p.isUser && p.id)
            .map((p) => p.id as string);
        setParticipantPersonaIds(serverIds);
        setSavedParticipantPersonaIds(serverIds);
        participantsInitialized.current = true;
    }, [partyDetailsQuery.data]);

    const promptParty = usePostPartyIdPrompt();
    const repromptParty = usePostPartyIdRepromptMessageId();
    const proceedParty = usePostPartyIdProceed();
    const cancelGenerations = usePostPartyIdCancel();
    const truncatePartyMessagesAfter =
        useDeletePartyIdChatGroupsChatGroupIdMessagesAfterMessageId();
    const deletePartyMessage =
        useDeletePartyIdChatGroupsChatGroupIdMessagesMessageId();

    const busy = promptParty.isPending || proceedParty.isPending;

    const textareaRef = useRef<HTMLTextAreaElement | null>(null);

    const handleSubmit = useCallback(async () => {
        const trimmed = inputValue.trim();
        if (!trimmed || busy) {
            return;
        }
        await promptParty.mutateAsync({
            id: apiPartyId,
            data: {
                chatGroupId,
                prompt: trimmed,
                provider,
                model,
                personaId: selectedPersonaId || null,
            },
        });
        setInputValue('');
    }, [inputValue, busy, promptParty, apiPartyId, chatGroupId, provider, model, selectedPersonaId]);

    useHotkey('Mod+Enter', handleSubmit, {
        target: textareaRef,
    });

    const saveParticipantsMutation = usePutPartyIdParticipants({
        mutation: {
            onSuccess: (_data, variables) => {
                const savedPersonaIds = (variables.data.participants ?? [])
                    .filter((p) => !p.isUser && p.id !== null)
                    .map(
                        (p) => p.id ?? 'unreachable but tsc is being annoying',
                    );
                setSavedParticipantPersonaIds(savedPersonaIds);
                queryClient.invalidateQueries({
                    queryKey: ['chat', 'party', apiPartyId],
                });
            },
        },
    });

    useEffect(() => {
        if (!chatGroupId) {
            return;
        }

        connectPartyRealtime(apiPartyId);
        const unsubscribe = subscribeToChatGroup(chatGroupId, (state) => {
            setMessages(state.messages);
        });

        return () => {
            unsubscribe();
            disconnectPartyRealtime(apiPartyId);
        };
    }, [
        apiPartyId,
        chatGroupId,
        connectPartyRealtime,
        disconnectPartyRealtime,
        subscribeToChatGroup,
    ]);

    useEffect(() => {
        if (messages.length === 0) {
            return;
        }

        if (!isNearBottom) {
            setUnreadCount((count) => count + 1);
            return;
        }

        const container = scrollRef.current;
        if (!container) {
            return;
        }
        container.scrollTop = container.scrollHeight;
    }, [isNearBottom, messages]);

    useEffect(() => {
        if (isNearBottom && unreadCount > 0) {
            setUnreadCount(0);
        }
    }, [isNearBottom, unreadCount]);

    // Keep selected persona in sync: auto-select first promptable persona if current selection is unavailable
    useEffect(() => {
        if (partyDetailsQuery.data.status !== 200) return;
        const personas = partyDetailsQuery.data.data.personaParticipants;
        const promptable = personas.filter(
            (p) => !participantPersonaIds.includes(p.id ?? ''),
        );
        const isValid =
            selectedPersonaId &&
            promptable.some((p) => p.id === selectedPersonaId);
        if (!isValid) {
            setSelectedPersonaId(promptable[0]?.id ?? '');
        }
    }, [partyDetailsQuery.data, participantPersonaIds, selectedPersonaId]);

    // Auto-save user persona to backend whenever selection changes so the overseer knows who the human is
    useEffect(() => {
        if (lastSavedUserPersonaId.current === selectedPersonaId) return;
        if (partyDetailsQuery.data.status !== 200) return;
        if (!participantsInitialized.current) return;

        lastSavedUserPersonaId.current = selectedPersonaId;

        const personas = partyDetailsQuery.data.data.personaParticipants;
        const personaNameMap = new Map(personas.map((p) => [p.id, p.name]));
        const aiParticipants = savedParticipantPersonaIds.map((id) => ({
            id,
            name: personaNameMap.get(id) ?? id,
            isUser: false,
        }));
        const participants = selectedPersonaId
            ? [
                  ...aiParticipants,
                  {
                      id: selectedPersonaId,
                      name:
                          personaNameMap.get(selectedPersonaId) ??
                          selectedPersonaId,
                      isUser: true,
                  },
              ]
            : aiParticipants;
        saveParticipantsMutation.mutate({
            id: apiPartyId,
            data: { participants },
        });
    }, [
        selectedPersonaId,
        partyDetailsQuery.data,
        savedParticipantPersonaIds,
        apiPartyId,
        saveParticipantsMutation,
    ]);

    const hasParticipantChanges = useMemo(() => {
        const a = new Set(participantPersonaIds);
        const b = new Set(savedParticipantPersonaIds);
        return (
            a.size !== b.size || participantPersonaIds.some((id) => !b.has(id))
        );
    }, [participantPersonaIds, savedParticipantPersonaIds]);

    const isStreaming = activeGenerations.length > 0;
    const activeGenerationSet = useMemo(
        () => new Set(activeGenerations),
        [activeGenerations],
    );

    const activeOverseerText = useMemo(() => {
        if (
            generationInfo.phase !== 'overseer' ||
            activeGenerations.length === 0
        )
            return null;
        const activeId = activeGenerations[activeGenerations.length - 1];
        return messages.find((m) => m.messageId === activeId)?.overseer ?? null;
    }, [generationInfo.phase, activeGenerations, messages]);

    const uniqueMessages = useMemo(
        () =>
            Array.from(
                new Map(
                    messages.map((message) => [message.messageId, message]),
                ).values(),
            ),
        [messages],
    );

    // Detect overseer stop directed at the user's persona that hasn't been responded to yet
    const pendingInstruction = useMemo(() => {
        if (!selectedPersonaId) return null;
        let lastStopIdx = -1;
        let lastInstruction: { instruction?: string; reason?: string } | null =
            null;
        for (let i = 0; i < uniqueMessages.length; i++) {
            const msg = uniqueMessages[i];
            if (!msg.overseer) continue;
            try {
                const o = JSON.parse(msg.overseer);
                const personaId = o.personaId ?? o.PersonaId;
                const stop = o.stop ?? o.Stop;
                if (stop && personaId === selectedPersonaId) {
                    lastStopIdx = i;
                    lastInstruction = {
                        instruction: o.instruction ?? o.Instruction,
                        reason: o.reason ?? o.Reason,
                    };
                }
            } catch {
                /* ignore */
            }
        }
        if (lastStopIdx === -1 || !lastInstruction) return null;
        const respondedAfter = uniqueMessages
            .slice(lastStopIdx + 1)
            .some((m) => m.senderType === 'user');
        return respondedAfter ? null : lastInstruction;
    }, [uniqueMessages, selectedPersonaId]);

    const handleSaveParticipants = useCallback(() => {
        if (partyDetailsQuery.data.status !== 200) return;

        const personas = partyDetailsQuery.data.data.personaParticipants;
        const personaNameMap = new Map(personas.map((p) => [p.id, p.name]));
        const userParticipants = (
            partyDetailsQuery.data.data.party.participants ?? []
        ).filter((p) => p.isUser);
        const personasToSave = participantPersonaIds.map((id) => ({
            id,
            name: personaNameMap.get(id) ?? id,
            isUser: false,
        }));
        saveParticipantsMutation.mutate({
            id: apiPartyId,
            data: { participants: [...personasToSave, ...userParticipants] },
        });
    }, [
        apiPartyId,
        participantPersonaIds,
        partyDetailsQuery.data,
        saveParticipantsMutation,
    ]);

    const modelsByProvider = partyDetailsQuery.data.data.models;
    const partyPersonas = partyDetailsQuery.data.data.personaParticipants;
    const peoplePicker = partyPersonas.map((persona) => ({
        id: persona.id ?? '',
        name: persona.name ?? '',
    }));
    const promptablePersonas = partyPersonas.filter(
        (p) => !participantPersonaIds.includes(p.id ?? ''),
    );
    const selectedPersonaName =
        partyPersonas.find((p) => p.id === selectedPersonaId)?.name ??
        selectedPersonaId;

    return (
        <div
            className="app-surface flex flex-col h-full"
            style={{ background: '#ECE9D8' }}
        >
            {/* Header toolbar */}
            <div
                className="p-2 space-y-2"
                style={{ borderBottom: '1px solid #ACA899' }}
            >
                <div className="flex items-center justify-between">
                    <span className="flex items-center gap-1.5">
                        <ConnectionDot status={connectionStatus} />
                        <span style={{ fontWeight: 600, color: '#000' }}>
                            {partyName ?? apiPartyId}
                        </span>
                    </span>
                    <span className="flex items-center gap-2">
                        {isStreaming && (
                            <button
                                type="button"
                                onClick={() =>
                                    cancelGenerations.mutateAsync({
                                        id: apiPartyId,
                                    })
                                }
                                style={{
                                    fontSize: 10,
                                    color: '#CC0000',
                                    padding: '1px 6px',
                                    background: '#FFF0F0',
                                    border: '1px solid #CC0000',
                                }}
                            >
                                ■ Stop
                            </button>
                        )}
                    </span>
                </div>

                <div className="flex gap-2">
                    <select
                        className="flex-1 text-[11px]"
                        value={model}
                        onChange={(event) => {
                            const option = event.target.selectedOptions[0];
                            const nextModel = option.dataset.model;
                            const nextProvider = option.dataset.provider;
                            if (
                                nextModel === undefined ||
                                nextProvider === undefined
                            )
                                return;
                            setModel(nextModel);
                            setProvider(nextProvider);
                        }}
                    >
                        {modelsByProvider.length === 0 ? (
                            <option value={model}>{model}</option>
                        ) : (
                            modelsByProvider
                                .flatMap((entry) => entry.models)
                                .map((entry) => ({
                                    id: entry.id,
                                    provider: entry.provider,
                                    displayName: `${entry.provider} / ${entry.name || entry.id}`,
                                }))
                                .map((entry) => (
                                    <option
                                        key={entry.id}
                                        data-model={entry.id}
                                        data-provider={entry.provider}
                                    >
                                        {entry.displayName}
                                    </option>
                                ))
                        )}
                    </select>
                </div>

                {modelsByProvider.length === 0 ? (
                    <p className="text-[10px]" style={{ color: '#996600' }}>
                        No models available from backend yet.
                    </p>
                ) : null}
            </div>

            {/* Party participant management */}
            <div
                className="p-1 space-y-1"
                style={{
                    borderBottom: '1px solid #ACA899',
                    background: '#F0EDD4',
                }}
            >
                <PeoplePicker
                    people={peoplePicker}
                    selectedIds={participantPersonaIds}
                    onChange={setParticipantPersonaIds}
                    compact
                />
                <div className="flex items-center justify-between gap-2">
                    {participantPersonaIds.length === 0 &&
                        peoplePicker.length > 0 && (
                            <span style={{ fontSize: 10, color: '#996600' }}>
                                No AI participants — AI won't respond.
                            </span>
                        )}
                    {hasParticipantChanges && (
                        <div className="flex items-center gap-1 ml-auto">
                            {saveParticipantsMutation.isError && (
                                <span style={{ fontSize: 10, color: '#c00' }}>
                                    {saveParticipantsMutation.error instanceof
                                    Error
                                        ? saveParticipantsMutation.error.message
                                        : 'Failed to save'}
                                </span>
                            )}
                            <button
                                type="button"
                                disabled={saveParticipantsMutation.isPending}
                                style={{ fontSize: 11 }}
                                onClick={() =>
                                    setParticipantPersonaIds(
                                        savedParticipantPersonaIds,
                                    )
                                }
                            >
                                Reset
                            </button>
                            <button
                                type="button"
                                disabled={saveParticipantsMutation.isPending}
                                style={{ fontSize: 11 }}
                                onClick={handleSaveParticipants}
                            >
                                {saveParticipantsMutation.isPending
                                    ? 'Saving...'
                                    : 'Save'}
                            </button>
                        </div>
                    )}
                </div>
            </div>

            {/* Message area */}
            <div className="flex-1 flex flex-col min-h-0">
                <div
                    ref={scrollRef}
                    onScroll={(event) => {
                        const target = event.currentTarget;
                        const distanceFromBottom =
                            target.scrollHeight -
                            target.scrollTop -
                            target.clientHeight;
                        setIsNearBottom(distanceFromBottom < 48);
                    }}
                    className="xp-sunken flex-1 overflow-y-auto p-2 space-y-2 m-1"
                >
                    {uniqueMessages.map((message) => (
                        <ChatBubble
                            key={`${message.chatGroupId}:${message.messageId}`}
                            message={message}
                            busy={busy}
                            personas={partyPersonas}
                            isGenerating={activeGenerationSet.has(
                                message.messageId,
                            )}
                            onDelete={() =>
                                deletePartyMessage.mutateAsync({
                                    id: apiPartyId,
                                    chatGroupId: chatGroupId,
                                    messageId: message.messageId,
                                })
                            }
                            onTruncate={() =>
                                truncatePartyMessagesAfter.mutateAsync({
                                    id: apiPartyId,
                                    chatGroupId: chatGroupId,
                                    messageId: message.messageId,
                                })
                            }
                            onReprompt={() =>
                                repromptParty.mutateAsync({
                                    id: apiPartyId,
                                    messageId: message.messageId,
                                    data: {
                                        chatGroupId,
                                        model,
                                        provider,
                                    },
                                })
                            }
                        />
                    ))}
                    {isStreaming && (
                        <StreamingIndicator
                            info={generationInfo}
                            overseerText={activeOverseerText}
                        />
                    )}
                </div>

                {/* Unread indicator */}
                {!isNearBottom && unreadCount > 0 ? (
                    <div
                        className="flex items-center justify-between px-2 py-1"
                        style={{
                            borderTop: '1px solid #ACA899',
                            background: '#FFFDD5',
                            color: '#000',
                        }}
                    >
                        <span>{unreadCount} new message(s)</span>
                        <button
                            type="button"
                            onClick={() => {
                                const node = scrollRef.current;
                                if (!node) {
                                    return;
                                }
                                node.scrollTop = node.scrollHeight;
                                setUnreadCount(0);
                                setIsNearBottom(true);
                            }}
                        >
                            Jump to latest
                        </button>
                    </div>
                ) : null}

                {/* Input area */}
                <form
                    className="p-2 space-y-1"
                    style={{
                        borderTop: '1px solid #ACA899',
                        background: '#F5F5ED',
                    }}
                    onSubmit={async (event) => {
                        event.preventDefault();
                        await handleSubmit();
                    }}
                >
                    {pendingInstruction && (
                        <div
                            style={{
                                background: '#FFFBEA',
                                border: '1px solid #E6D87A',
                                padding: '4px 8px',
                                fontSize: 11,
                            }}
                        >
                            <span style={{ fontWeight: 600, color: '#806600' }}>
                                ⚖️ As {selectedPersonaName}:
                            </span>{' '}
                            <span style={{ color: '#555' }}>
                                {pendingInstruction.instruction}
                            </span>
                        </div>
                    )}
                    <textarea
                        ref={textareaRef}
                        rows={3}
                        className="w-full text-[11px]"
                        style={{ padding: '4px' }}
                        placeholder="Type a message to the chat group..."
                        value={inputValue}
                        onChange={(event) =>
                            setInputValue(event.currentTarget.value)
                        }
                    />
                    {promptParty.isError ? (
                        <p className="text-[10px]" style={{ color: '#c00' }}>
                            {promptParty.error instanceof Error
                                ? promptParty.error.message
                                : 'Failed to send'}
                        </p>
                    ) : null}
                    <div className="flex items-center gap-1">
                        <select
                            className="flex-1 text-[11px]"
                            value={selectedPersonaId}
                            onChange={(event) =>
                                setSelectedPersonaId(event.currentTarget.value)
                            }
                        >
                            {promptablePersonas.map((persona) => (
                                <option key={persona.id} value={persona.id}>
                                    {persona.name}
                                </option>
                            ))}
                        </select>
                        <button
                            type="button"
                            disabled={busy}
                            className="text-[11px]"
                            style={{
                                padding: '2px 10px',
                                background: busy ? '#D4D0C8' : '#ECE9D8',
                            }}
                            onClick={() =>
                                proceedParty.mutateAsync({
                                    id: apiPartyId,
                                    data: {
                                        chatGroupId,
                                        provider,
                                        model,
                                        personaId: selectedPersonaId || null,
                                    },
                                })
                            }
                        >
                            {busy ? '...' : 'Proceed'}
                        </button>
                        <button
                            type="submit"
                            disabled={busy}
                            className="text-[11px]"
                            style={{
                                padding: '2px 16px',
                                background: busy ? '#D4D0C8' : '#ECE9D8',
                            }}
                        >
                            {busy ? '...' : 'Send'}
                        </button>
                    </div>
                </form>
            </div>
        </div>
    );
}

function ChatBubble({
    message,
    onDelete,
    onTruncate,
    onReprompt,
    busy,
    personas,
    isGenerating,
}: {
    message: RealtimeChatMessage;
    onDelete: () => void;
    onTruncate: () => void;
    onReprompt: () => void;
    busy: boolean;
    personas: Persona[];
    isGenerating: boolean;
}) {
    const isUser = message.senderType === 'user';
    const [showDetails, setShowDetails] = useState(false);

    const senderName = useMemo(() => {
        if (message.senderName) {
            return message.senderName;
        }
        if (isUser) {
            return 'You';
        }
        const persona = personas.find((p) => p.id === message.senderId);
        return persona?.name ?? message.senderId.slice(0, 8);
    }, [message.senderName, message.senderId, isUser, personas]);

    const hasDetails = !!(
        message.reasoning || (message.generationEvents?.length ?? 0) > 0
    );

    const overseerData = useMemo(() => {
        if (!message.overseer) return null;
        try {
            const raw = JSON.parse(message.overseer);
            // Normalise PascalCase keys from older messages to camelCase
            return {
                personaId: raw.personaId ?? raw.PersonaId,
                reason: raw.reason ?? raw.Reason,
                instruction: raw.instruction ?? raw.Instruction,
                stop: raw.stop ?? raw.Stop,
            };
        } catch {
            return null;
        }
    }, [message.overseer]);

    const isOverseerStop = overseerData?.stop === true;

    return (
        <div
            className="p-2"
            style={{
                borderBottom: '1px solid #D4D0C8',
                background: isOverseerStop
                    ? '#FFFBEA'
                    : isUser
                      ? '#FFFEF5'
                      : '#F8F8F8',
                borderLeft: isGenerating
                    ? '3px solid #316AC5'
                    : isOverseerStop
                      ? '3px solid #CC8800'
                      : '3px solid transparent',
            }}
        >
            <div className="flex items-start justify-between gap-2 mb-1">
                <div className="flex items-center gap-2">
                    {isOverseerStop ? (
                        <span
                            title="Overseer"
                            style={{
                                width: 22,
                                height: 22,
                                flexShrink: 0,
                                display: 'inline-flex',
                                alignItems: 'center',
                                justifyContent: 'center',
                                fontSize: 14,
                            }}
                        >
                            ⚖️
                        </span>
                    ) : (
                        <img
                            src={`https://robohash.org/${isUser ? encodeURIComponent(senderName) : message.senderId}?size=32x32${isUser ? '&set=set5' : ''}`}
                            alt={senderName}
                            style={{
                                width: 22,
                                height: 22,
                                flexShrink: 0,
                                imageRendering: 'pixelated',
                            }}
                        />
                    )}
                    <span
                        style={{
                            fontWeight: 600,
                            color: isOverseerStop
                                ? '#806600'
                                : isUser
                                  ? '#003399'
                                  : '#006600',
                        }}
                    >
                        {isOverseerStop ? 'Overseer' : senderName}
                    </span>
                    {message.modelEndpointStub ? (
                        <span
                            className="text-[10px]"
                            style={{ color: '#808080' }}
                        >
                            {message.modelEndpointStub.split('/').pop()}
                        </span>
                    ) : null}
                    {formatTime(message.sendAt) ? (
                        <span style={{ color: '#ACA899', fontSize: 10 }}>
                            {formatTime(message.sendAt)}
                        </span>
                    ) : null}
                </div>
                <span className="flex items-center gap-1">
                    {hasDetails && (
                        <button
                            type="button"
                            onClick={() => setShowDetails((v) => !v)}
                            style={{ fontSize: '10px', color: '#316AC5' }}
                        >
                            {showDetails ? 'Hide' : 'Details'}
                        </button>
                    )}
                    <button
                        type="button"
                        disabled={busy}
                        onClick={onReprompt}
                        style={{ fontSize: '10px' }}
                    >
                        redo
                    </button>
                    <button
                        type="button"
                        disabled={busy}
                        onClick={onTruncate}
                        style={{ fontSize: '10px' }}
                    >
                        cut
                    </button>
                    <button
                        type="button"
                        disabled={busy}
                        onClick={onDelete}
                        style={{ fontSize: '10px' }}
                    >
                        del
                    </button>
                </span>
            </div>

            {message.content ? (
                <MarkdownContent
                    content={message.content}
                    isStreaming={isGenerating}
                />
            ) : null}

            {message.error ? (
                <div style={{ color: '#c00', marginTop: '4px' }}>
                    {message.error}
                </div>
            ) : null}

            {overseerData && (
                <details className="mt-1" style={{ fontSize: 10 }}>
                    <summary
                        style={{
                            color: '#806600',
                            cursor: 'pointer',
                            userSelect: 'none',
                        }}
                    >
                        Overseer →{' '}
                        {personas.find((p) => p.id === overseerData.personaId)
                            ?.name ??
                            overseerData.personaId?.slice(0, 8) ??
                            'none'}
                        {overseerData.stop ? ' (stop)' : ''}
                    </summary>
                    <div
                        className="mt-1 p-1.5"
                        style={{
                            background: '#FFFBEA',
                            border: '1px solid #E6D87A',
                            color: '#555',
                        }}
                    >
                        {overseerData.reason && (
                            <div>
                                <span style={{ fontWeight: 600 }}>Reason:</span>{' '}
                                {overseerData.reason}
                            </div>
                        )}
                        {overseerData.instruction && (
                            <div>
                                <span style={{ fontWeight: 600 }}>
                                    Instruction:
                                </span>{' '}
                                {overseerData.instruction}
                            </div>
                        )}
                    </div>
                </details>
            )}

            {showDetails && hasDetails && (
                <div
                    className="mt-2 p-2"
                    style={{
                        background: '#FFF',
                        border: '1px solid #D4D0C8',
                    }}
                >
                    {message.reasoning && (
                        <div className="mb-2">
                            <div
                                className="text-[10px] font-semibold"
                                style={{ color: '#316AC5' }}
                            >
                                Reasoning
                            </div>
                            <div
                                className="text-[11px]"
                                style={{
                                    color: '#666',
                                    whiteSpace: 'pre-wrap',
                                }}
                            >
                                {message.reasoning}
                            </div>
                        </div>
                    )}

                    {(message.generationEvents?.length ?? 0) > 0 && (
                        <div>
                            <div
                                className="text-[10px] font-semibold"
                                style={{ color: '#316AC5' }}
                            >
                                Generation Events
                            </div>
                            <div
                                className="text-[10px]"
                                style={{ color: '#808080' }}
                            >
                                {message.generationEvents
                                    ?.filter(
                                        (e) =>
                                            ![
                                                'message',
                                                'reasoning',
                                                'overseer',
                                            ].includes(e.event),
                                    )
                                    .map((e, i) => (
                                        <div
                                            key={`${e.event}-${e.at}-${i}`}
                                            className="flex gap-2 mt-1"
                                        >
                                            <span
                                                style={{
                                                    fontWeight: 600,
                                                    minWidth: '80px',
                                                }}
                                            >
                                                {e.event}:
                                            </span>
                                            <span style={{ color: '#000' }}>
                                                {e.event === 'overseerStop'
                                                    ? '(stopped)'
                                                    : e.event ===
                                                        'personaChange'
                                                      ? (personas.find(
                                                            (p) =>
                                                                p.id === e.data,
                                                        )?.name ??
                                                        e.data.slice(0, 8))
                                                      : e.data.slice(0, 50)}
                                                {e.event !== 'overseerStop' &&
                                                e.event !== 'personaChange' &&
                                                e.data.length > 50
                                                    ? '...'
                                                    : ''}
                                            </span>
                                        </div>
                                    ))}
                            </div>
                        </div>
                    )}
                </div>
            )}
        </div>
    );
}

function MarkdownContent({
    content,
    isStreaming,
}: {
    content: string;
    isStreaming: boolean;
}) {
    type ParserState = {
        parser: ReturnType<typeof smd.parser>;
        writtenLength: number;
    };
    const containerRef = useRef<HTMLDivElement>(null);
    const stateRef = useRef<ParserState | null>(null);

    useEffect(() => {
        const container = containerRef.current;
        if (!container) return;

        if (!stateRef.current) {
            container.innerHTML = '';
            stateRef.current = {
                parser: smd.parser(smd.default_renderer(container)),
                writtenLength: 0,
            };
        }

        const state = stateRef.current;
        const newChunk = content.slice(state.writtenLength);

        if (newChunk) {
            DOMPurify.sanitize(content);
            if (DOMPurify.removed.length > 0) {
                smd.parser_end(state.parser);
                stateRef.current = null;
                return;
            }
            smd.parser_write(state.parser, newChunk);
            state.writtenLength = content.length;
        }

        if (!isStreaming) {
            smd.parser_end(state.parser);
            stateRef.current = null;
        }
    }, [content, isStreaming]);

    useEffect(() => {
        return () => {
            if (stateRef.current) {
                smd.parser_end(stateRef.current.parser);
                stateRef.current = null;
            }
        };
    }, []);

    return (
        <div
            ref={containerRef}
            className="markdown-content"
            style={{ color: '#000' }}
        />
    );
}

function StreamingIndicator({
    info,
    overseerText,
}: {
    info: GenerationPhase;
    overseerText?: string | null;
}) {
    if (info.phase === 'overseer') {
        return (
            <div
                style={{
                    color: '#806600',
                    background: '#FFF8DC',
                    padding: '4px 8px',
                    border: '1px solid #E6D87A',
                }}
            >
                <span className="animate-pulse">
                    Overseer is choosing who speaks...
                </span>
                {overseerText && (
                    <div
                        className="mt-1"
                        style={{
                            fontFamily: 'monospace',
                            fontSize: 9,
                            color: '#5a4800',
                            whiteSpace: 'pre-wrap',
                            wordBreak: 'break-all',
                            maxHeight: 48,
                            overflow: 'hidden',
                        }}
                    >
                        {overseerText}
                    </div>
                )}
            </div>
        );
    }
    if (info.phase === 'typing') {
        return (
            <div
                className="flex items-center gap-2"
                style={{
                    color: '#004499',
                    background: '#F0F8FF',
                    padding: '4px 8px',
                    border: '1px solid #BDD7EE',
                }}
            >
                <span>{info.personaName} is typing...</span>
            </div>
        );
    }
    if (info.phase === 'streaming') {
        return (
            <div
                className="flex items-center gap-2"
                style={{
                    color: '#004499',
                    background: '#F0F8FF',
                    padding: '4px 8px',
                    border: '1px solid #BDD7EE',
                }}
            >
                <span>{info.personaName}</span>
                <span className="animate-pulse">▌</span>
                <span style={{ color: '#808080', fontSize: 10 }}>
                    ({info.charCount} chars)
                </span>
            </div>
        );
    }
    return (
        <div
            className="flex items-center gap-2 animate-pulse"
            style={{ color: '#808080', padding: '4px' }}
        >
            <span>Working...</span>
        </div>
    );
}

function ConnectionDot({ status }: { status: RealtimeConnectionStatus }) {
    const color = {
        connected: '#00AA00',
        connecting: '#CC8800',
        reconnecting: '#CC8800',
        disconnected: '#CC0000',
    }[status];
    const title = {
        connected: 'Connected',
        connecting: 'Connecting...',
        reconnecting: 'Reconnecting...',
        disconnected: 'Disconnected',
    }[status];
    return (
        <span
            title={title}
            className={
                status === 'connecting' || status === 'reconnecting'
                    ? 'animate-pulse'
                    : ''
            }
            style={{
                display: 'inline-block',
                width: 8,
                height: 8,
                borderRadius: '50%',
                background: color,
                border: '1px solid rgba(0,0,0,0.3)',
            }}
        />
    );
}

function formatTime(t: number | null) {
    return t
        ? new Date(t).toLocaleTimeString([], {
              hour: '2-digit',
              minute: '2-digit',
          })
        : null;
}
