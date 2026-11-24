import {
    ChatLog,
    ChatMessage,
    ComboBox,
    CommandButton,
    ContactRow,
    Dialog,
    FieldRow,
    Icon,
    Presence,
    RichText,
    StatusText,
    TaskPane,
    TaskPaneLink,
    TaskPaneSection,
    Text,
    TextArea,
    type Tone,
    Toolbar,
    ToolbarButton,
} from '@partytown/ui';
import { useHotkey } from '@tanstack/react-hotkeys';
import { useQueryClient } from '@tanstack/react-query';
import DOMPurify from 'dompurify';
import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import * as smd from 'streaming-markdown';
import {
    getGetPartyIdChatGroupsChatGroupIdDriverOverridesQueryKey,
    getGetPartyIdChatGroupsChatGroupIdParticipantsQueryKey,
    getGetPartyIdChatGroupsQueryKey,
    useDeletePartyIdChatGroupsChatGroupIdMessagesAfterMessageId,
    useDeletePartyIdChatGroupsChatGroupIdMessagesMessageId,
    useGetPartyIdChatGroupsChatGroupIdDriverOverridesSuspense,
    useGetPartyIdChatGroupsChatGroupIdParticipantsSuspense,
    useGetPartyIdSuspense,
    usePostPartyIdCancel,
    usePostPartyIdChatGroupsChatGroupIdMessagesMessageIdRemember,
    usePostPartyIdProceed,
    usePostPartyIdPrompt,
    usePostPartyIdRepromptMessageId,
    usePutPartyIdChatGroupsChatGroupIdDriverOverrides,
    usePutPartyIdChatGroupsChatGroupIdParticipants,
    usePutPartyIdChatGroupsChatGroupIdScenario,
} from '#api/party-zone';
import {
    type ActiveGenerationPhase,
    type DeclinedDecision,
    type GenerationPhase,
    type RaceEvaluation,
    type RealtimeChatMessage,
    useDeclinedDecisions,
    useRaceEvaluations,
} from '#lib/realtime-store';
import type { Persona } from '../../api/model';
import { DriverKind } from '../../api/model';
import Portrait from '../../components/Portrait';
import { NARRATOR_PERSONA_ID } from '../../lib/driver';
import {
    isShownInRoom,
    parseAppraisal,
    pendingInstructionFor,
} from '../../lib/room-conversation';
import { useRoomConversation } from '../../lib/use-room-conversation';
import { WORLD_ID } from '../../lib/world';
import { speakAsOverrides } from './room-cast';

export interface ChatViewProps {
    chatGroupId: string;
}

interface GroupChatWindowProps {
    partyId?: string;
    chatGroupId?: string;
}

export default function GroupChatWindow({ chatGroupId }: GroupChatWindowProps) {
    if (!chatGroupId) {
        return (
            <div className="flex h-full items-center justify-center">
                <Text tone="muted">
                    Open a chat group from the Chat Launcher to start messaging.
                </Text>
            </div>
        );
    }

    return <ChatView chatGroupId={chatGroupId} />;
}

export function ChatView({ chatGroupId }: ChatViewProps) {
    const apiPartyId = WORLD_ID;
    const queryClient = useQueryClient();
    const [messages, setMessages] = useState<RealtimeChatMessage[]>([]);
    const [inputValue, setInputValue] = useState('');
    const [selectedPersonaId, setSelectedPersonaId] = useState('');

    const [participantPersonaIds, setParticipantPersonaIds] = useState<
        string[]
    >([]);
    const [savedParticipantPersonaIds, setSavedParticipantPersonaIds] =
        useState<string[]>([]);
    const initializedForChatGroupId = useRef<string | null>(null);
    const lastSavedUserPersonaId = useRef<string | null>(null);

    const scrollRef = useRef<HTMLDivElement | null>(null);
    const [isNearBottom, setIsNearBottom] = useState(true);
    const [unreadCount, setUnreadCount] = useState(0);

    const {
        messages: realtimeMessages,
        activeIds: activeGenerations,
        phases: generationPhases,
        phaseByPersona: personaPhases,
    } = useRoomConversation(apiPartyId, chatGroupId ?? '');
    const declinedDecisions = useDeclinedDecisions(chatGroupId ?? '');
    const raceEvaluations = useRaceEvaluations(chatGroupId ?? '');

    const partyDetailsQuery = useGetPartyIdSuspense(apiPartyId);
    const chatGroupParticipantsQuery =
        useGetPartyIdChatGroupsChatGroupIdParticipantsSuspense(
            apiPartyId,
            chatGroupId,
        );
    const chatGroupDriverOverridesQuery =
        useGetPartyIdChatGroupsChatGroupIdDriverOverridesSuspense(
            apiPartyId,
            chatGroupId,
        );

    useEffect(() => {
        if (initializedForChatGroupId.current === chatGroupId) return;
        if (chatGroupParticipantsQuery.data.status !== 200) return;
        if (chatGroupDriverOverridesQuery.data.status !== 200) return;
        const ids = chatGroupParticipantsQuery.data.data ?? [];
        const overrides = chatGroupDriverOverridesQuery.data.data ?? [];
        const userOverride = overrides.find((o) => o.kind === DriverKind.User);
        const userId = userOverride?.personaId ?? '';
        const aiIds = ids.filter(
            (id) => id !== userId && id !== NARRATOR_PERSONA_ID,
        );
        setParticipantPersonaIds(aiIds);
        setSavedParticipantPersonaIds(aiIds);
        setSelectedPersonaId(userId);
        lastSavedUserPersonaId.current = userId;
        initializedForChatGroupId.current = chatGroupId;
    }, [
        chatGroupId,
        chatGroupParticipantsQuery.data,
        chatGroupDriverOverridesQuery.data,
    ]);

    const promptParty = usePostPartyIdPrompt();
    const repromptParty = usePostPartyIdRepromptMessageId();
    const proceedParty = usePostPartyIdProceed();
    const cancelGenerations = usePostPartyIdCancel();
    const truncatePartyMessagesAfter =
        useDeletePartyIdChatGroupsChatGroupIdMessagesAfterMessageId();
    const deletePartyMessage =
        useDeletePartyIdChatGroupsChatGroupIdMessagesMessageId();
    const rememberMessage =
        usePostPartyIdChatGroupsChatGroupIdMessagesMessageIdRemember();

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
                senderId: selectedPersonaId || null,
            },
        });
        setInputValue('');
    }, [
        inputValue,
        busy,
        promptParty,
        apiPartyId,
        chatGroupId,
        selectedPersonaId,
    ]);

    useHotkey('Mod+Enter', handleSubmit, {
        target: textareaRef,
    });

    const saveParticipantsMutation =
        usePutPartyIdChatGroupsChatGroupIdParticipants({
            mutation: {
                onSuccess: (_data, variables) => {
                    const userId = lastSavedUserPersonaId.current ?? '';
                    const ids = (variables.data.participantIds ?? []).filter(
                        (id): id is string =>
                            !!id && id !== userId && id !== NARRATOR_PERSONA_ID,
                    );
                    setSavedParticipantPersonaIds(ids);
                    queryClient.invalidateQueries({
                        queryKey:
                            getGetPartyIdChatGroupsChatGroupIdParticipantsQueryKey(
                                variables.id,
                                variables.chatGroupId,
                            ),
                    });
                },
            },
        });

    const saveDriverOverridesMutation =
        usePutPartyIdChatGroupsChatGroupIdDriverOverrides({
            mutation: {
                onSuccess: (_data, variables) => {
                    queryClient.invalidateQueries({
                        queryKey:
                            getGetPartyIdChatGroupsChatGroupIdDriverOverridesQueryKey(
                                variables.id,
                                variables.chatGroupId,
                            ),
                    });
                },
            },
        });

    useEffect(() => {
        setMessages(realtimeMessages);
    }, [realtimeMessages]);

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

    useEffect(() => {
        if (initializedForChatGroupId.current !== chatGroupId) return;
        if (partyDetailsQuery.data.status !== 200) return;
        const personas = partyDetailsQuery.data.data.personaParticipants;
        const promptable = personas.filter(
            (p) =>
                p.id !== NARRATOR_PERSONA_ID &&
                !participantPersonaIds.includes(p.id ?? ''),
        );
        const isValid =
            selectedPersonaId &&
            promptable.some((p) => p.id === selectedPersonaId);
        if (!isValid) {
            setSelectedPersonaId(promptable[0]?.id ?? '');
        }
    }, [
        partyDetailsQuery.data,
        participantPersonaIds,
        selectedPersonaId,
        chatGroupId,
    ]);

    const saveRoomCast = useCallback(
        (aiPersonaIds: string[], userPersonaId: string) => {
            const participantIds = userPersonaId
                ? [...aiPersonaIds, userPersonaId]
                : aiPersonaIds;
            saveParticipantsMutation.mutate({
                id: apiPartyId,
                chatGroupId,
                data: { participantIds },
            });

            const current =
                chatGroupDriverOverridesQuery.data.status === 200
                    ? (chatGroupDriverOverridesQuery.data.data ?? [])
                    : [];
            const overrides = speakAsOverrides(current, userPersonaId);
            saveDriverOverridesMutation.mutate({
                id: apiPartyId,
                chatGroupId,
                data: { overrides },
            });
        },
        [
            apiPartyId,
            chatGroupId,
            chatGroupDriverOverridesQuery.data,
            saveParticipantsMutation.mutate,
            saveDriverOverridesMutation.mutate,
        ],
    );

    useEffect(() => {
        if (lastSavedUserPersonaId.current === selectedPersonaId) return;
        if (partyDetailsQuery.data.status !== 200) return;
        if (initializedForChatGroupId.current !== chatGroupId) return;

        lastSavedUserPersonaId.current = selectedPersonaId;
        saveRoomCast(savedParticipantPersonaIds, selectedPersonaId);
    }, [
        selectedPersonaId,
        partyDetailsQuery.data,
        savedParticipantPersonaIds,
        chatGroupId,
        saveRoomCast,
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

    const uniqueMessages = useMemo(
        () =>
            Array.from(
                new Map(
                    messages.map((message) => [message.messageId, message]),
                ).values(),
            ),
        [messages],
    );

    const pendingInstruction = useMemo(
        () => pendingInstructionFor(uniqueMessages, selectedPersonaId),
        [uniqueMessages, selectedPersonaId],
    );

    const handleSaveParticipants = useCallback(() => {
        if (partyDetailsQuery.data.status !== 200) return;
        saveRoomCast(participantPersonaIds, selectedPersonaId);
    }, [
        participantPersonaIds,
        selectedPersonaId,
        partyDetailsQuery.data,
        saveRoomCast,
    ]);

    const partyPersonas = partyDetailsQuery.data.data.personaParticipants;
    const promptablePersonas = partyPersonas.filter(
        (p) =>
            p.id !== NARRATOR_PERSONA_ID &&
            !participantPersonaIds.includes(p.id ?? ''),
    );
    const selectedPersonaName =
        partyPersonas.find((p) => p.id === selectedPersonaId)?.name ??
        selectedPersonaId;

    const activePersonaIds = useMemo(
        () => new Set(personaPhases.keys()),
        [personaPhases],
    );

    return (
        <div className="flex h-full min-h-0 gap-2">
            <div className="flex h-full min-w-0 flex-1 flex-col gap-1.5">
                <ChatLog
                    ref={scrollRef}
                    aria-label="Room conversation"
                    onScroll={(event) => {
                        const target = event.currentTarget;
                        const distanceFromBottom =
                            target.scrollHeight -
                            target.scrollTop -
                            target.clientHeight;
                        setIsNearBottom(distanceFromBottom < 48);
                    }}
                    className="flex-1 p-0"
                >
                    {uniqueMessages.length === 0 &&
                    generationPhases.length === 0 ? (
                        <div className="flex h-full flex-col items-center justify-center gap-1 p-4 text-center">
                            <Icon name="chat" size={32} />
                            <Text as="p" strong>
                                The room is quiet
                            </Text>
                            <Text as="p" tone="muted" className="max-w-[320px]">
                                Type a message below to kick things off, or
                                press Proceed to let a persona speak first.
                            </Text>
                        </div>
                    ) : null}
                    {uniqueMessages.map((message) => {
                        if (
                            !isShownInRoom(
                                message,
                                activeGenerationSet.has(message.messageId),
                                selectedPersonaId,
                            )
                        )
                            return null;
                        return (
                            <ChatBubble
                                key={`${message.chatGroupId}:${message.messageId}`}
                                message={message}
                                busy={busy}
                                personas={partyPersonas}
                                userPersonaId={selectedPersonaId}
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
                                            senderId: selectedPersonaId || null,
                                        },
                                    })
                                }
                                onRemember={() =>
                                    rememberMessage.mutateAsync({
                                        id: apiPartyId,
                                        chatGroupId,
                                        messageId: message.messageId,
                                    })
                                }
                                rememberPending={rememberMessage.isPending}
                            />
                        );
                    })}
                    {generationPhases.length > 0
                        ? generationPhases
                              .filter((p) => p.phase !== 'deciding')
                              .map((phase) => (
                                  <StreamingIndicator
                                      key={phase.messageId ?? 'unknown'}
                                      info={phase}
                                      personas={partyPersonas}
                                  />
                              ))
                        : null}
                </ChatLog>

                {!isNearBottom && unreadCount > 0 ? (
                    <StatusText
                        variant="bar"
                        icon={false}
                        actions={
                            <CommandButton
                                size="small"
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
                            </CommandButton>
                        }
                    >
                        {unreadCount} new message(s)
                    </StatusText>
                ) : null}

                <form
                    className="flex flex-col gap-1"
                    onSubmit={async (event) => {
                        event.preventDefault();
                        await handleSubmit();
                    }}
                >
                    {pendingInstruction && (
                        <StatusText
                            variant="bar"
                            tone="caution"
                            icon={<Icon name="scales" />}
                        >
                            <Text strong>As {selectedPersonaName}:</Text>{' '}
                            {pendingInstruction.instruction}
                        </StatusText>
                    )}
                    <TextArea
                        ref={textareaRef}
                        rows={3}
                        aria-label="Message"
                        className="w-full"
                        placeholder="Type a message to the chat group..."
                        value={inputValue}
                        onChange={(event) =>
                            setInputValue(event.currentTarget.value)
                        }
                    />
                    {promptParty.isError ? (
                        <Text as="p" tone="danger">
                            {promptParty.error instanceof Error
                                ? promptParty.error.message
                                : 'Failed to send'}
                        </Text>
                    ) : null}
                    <div className="flex flex-wrap items-center gap-1.5">
                        <FieldRow
                            label="Speak as"
                            labelWidth="auto"
                            className="min-w-0 flex-1"
                        >
                            <ComboBox
                                className="w-full"
                                title="The participant your message is sent as"
                                value={selectedPersonaId}
                                onChange={(event) =>
                                    setSelectedPersonaId(
                                        event.currentTarget.value,
                                    )
                                }
                                options={promptablePersonas.map((persona) => ({
                                    value: persona.id ?? '',
                                    label: persona.name,
                                }))}
                            />
                        </FieldRow>
                        <CommandButton
                            disabled={busy}
                            title="Skip your turn — a persona decides who speaks next"
                            onClick={() =>
                                proceedParty.mutateAsync({
                                    id: apiPartyId,
                                    data: {
                                        chatGroupId,
                                        senderId: selectedPersonaId || null,
                                    },
                                })
                            }
                        >
                            {busy ? '...' : 'Proceed'}
                        </CommandButton>
                        <CommandButton type="submit" isDefault disabled={busy}>
                            {busy ? '...' : 'Send'}
                        </CommandButton>
                        {isStreaming && (
                            <CommandButton
                                tone="danger"
                                icon={<Icon name="stop" />}
                                title="Cancel the replies being generated right now"
                                onClick={() =>
                                    cancelGenerations.mutateAsync({
                                        id: apiPartyId,
                                    })
                                }
                            >
                                Stop
                            </CommandButton>
                        )}
                    </div>
                </form>
            </div>

            <ParticipantsSidebar
                personas={partyPersonas}
                participantPersonaIds={participantPersonaIds}
                selectedPersonaId={selectedPersonaId}
                activePersonaIds={activePersonaIds}
                personaPhases={personaPhases}
                livePhases={generationPhases}
                allMessages={uniqueMessages}
                declinedDecisions={declinedDecisions}
                raceEvaluations={raceEvaluations}
                hasChanges={hasParticipantChanges}
                isSaving={saveParticipantsMutation.isPending}
                saveError={
                    saveParticipantsMutation.isError
                        ? saveParticipantsMutation.error instanceof Error
                            ? saveParticipantsMutation.error.message
                            : 'Failed to save'
                        : null
                }
                onToggleParticipant={(id) => {
                    setParticipantPersonaIds((prev) =>
                        prev.includes(id)
                            ? prev.filter((pid) => pid !== id)
                            : [...prev, id],
                    );
                }}
                onSave={handleSaveParticipants}
                onReset={() =>
                    setParticipantPersonaIds(savedParticipantPersonaIds)
                }
            />
        </div>
    );
}

function ParticipantsSidebar({
    personas,
    participantPersonaIds,
    selectedPersonaId,
    activePersonaIds,
    personaPhases,
    livePhases,
    allMessages,
    declinedDecisions,
    raceEvaluations,
    hasChanges,
    isSaving,
    saveError,
    onToggleParticipant,
    onSave,
    onReset,
}: {
    personas: Persona[];
    participantPersonaIds: string[];
    selectedPersonaId: string;
    activePersonaIds: Set<string>;
    personaPhases: Map<string, ActiveGenerationPhase>;
    livePhases: ActiveGenerationPhase[];
    allMessages: RealtimeChatMessage[];
    declinedDecisions: DeclinedDecision[];
    raceEvaluations: RaceEvaluation[];
    hasChanges: boolean;
    isSaving: boolean;
    saveError: string | null;
    onToggleParticipant: (id: string) => void;
    onSave: () => void;
    onReset: () => void;
}) {
    const participantSet = new Set(participantPersonaIds);
    const aiPersonas = personas;
    const userPersona = personas.find((p) => p.id === selectedPersonaId);
    const totalActive =
        participantPersonaIds.length + (selectedPersonaId ? 1 : 0);

    const [libraryOpen, setLibraryOpen] = useState(false);
    const showLibrary = libraryOpen || participantPersonaIds.length === 0;
    const visibleAiPersonas = showLibrary
        ? aiPersonas
        : aiPersonas.filter((p) => {
              const id = p.id ?? '';
              return participantSet.has(id) || activePersonaIds.has(id);
          });
    const hiddenCount = aiPersonas.length - visibleAiPersonas.length;

    const [thoughtsOpen, setThoughtsOpen] = useState(false);

    const decidingPhases = livePhases.filter((p) => p.phase === 'deciding');

    const thoughtLog = useMemo(() => {
        type LogEntry = {
            key: string;
            personaId: string | null;
            personaName: string;
            reason: string | null;
            instruction: string | null;
            kind:
                | 'go'
                | 'decline'
                | 'skip-obvious'
                | 'emote'
                | 'race-cancel-decision'
                | 'race-cancel-generation'
                | 'race-past-pnr'
                | 'race-continue';
            salience: number | null;
            cancelScore: number | null;
            sortKey: number;
        };

        const entries: LogEntry[] = [];

        for (const msg of allMessages) {
            const raw = parseAppraisal(msg.appraisal);
            if (!raw) continue;
            const personaId = raw.personaId;
            const persona = personas.find((p) => p.id === personaId);
            const isStop = raw.stop;
            const isRaceCancelled = raw.raceCancelled;
            const kind: LogEntry['kind'] = isRaceCancelled
                ? 'emote'
                : isStop
                  ? 'decline'
                  : 'go';
            entries.push({
                key: `msg-${msg.messageId}`,
                personaId,
                personaName:
                    persona?.name ?? personaId?.slice(0, 8) ?? 'Unknown',
                reason: raw.reason,
                instruction: raw.instruction,
                kind,
                salience: null,
                cancelScore: null,
                sortKey: msg.messageId,
            });
        }

        for (const d of declinedDecisions) {
            const persona = personas.find((p) => p.id === d.personaId);
            entries.push({
                key: `declined-${d.messageId}`,
                personaId: d.personaId,
                personaName:
                    persona?.name ?? d.personaId?.slice(0, 8) ?? 'Unknown',
                reason: d.reason,
                instruction: null,
                kind: 'skip-obvious',
                salience: null,
                cancelScore: null,
                sortKey: d.messageId,
            });
        }

        for (const r of raceEvaluations) {
            const persona = personas.find((p) => p.id === r.personaId);
            const kind: LogEntry['kind'] =
                r.outcome === 'cancel-decision'
                    ? 'race-cancel-decision'
                    : r.outcome === 'cancel-generation'
                      ? 'race-cancel-generation'
                      : r.outcome === 'past-pnr'
                        ? 'race-past-pnr'
                        : 'race-continue';
            entries.push({
                key: `race-${r.personaId}-${r.triggeredByMessageId}-${r.sendAt}`,
                personaId: r.personaId,
                personaName:
                    persona?.name ??
                    r.personaName ??
                    r.personaId?.slice(0, 8) ??
                    'Unknown',
                reason: null,
                instruction: null,
                kind,
                salience: r.salience,
                cancelScore: r.cancelScore,
                sortKey: r.triggeredByMessageId + r.sendAt / 1e13,
            });
        }

        return entries.sort((a, b) => b.sortKey - a.sortKey);
    }, [allMessages, declinedDecisions, raceEvaluations, personas]);

    const kindLook = (
        kind: (typeof thoughtLog)[number]['kind'],
    ): { tone: Tone; label: string } => {
        switch (kind) {
            case 'go':
                return { tone: 'go', label: 'go' };
            case 'decline':
                return { tone: 'caution', label: 'decline' };
            case 'skip-obvious':
                return { tone: 'caution', label: 'skip' };
            case 'race-cancel-decision':
                return { tone: 'danger', label: 'race·cancel·dec' };
            case 'race-cancel-generation':
                return { tone: 'danger', label: 'race·cancel·gen' };
            case 'race-past-pnr':
                return { tone: 'neutral', label: 'race·past-pnr' };
            case 'race-continue':
                return { tone: 'neutral', label: 'race·continue' };
            case 'emote':
                return { tone: 'neutral', label: 'emote' };
        }
    };

    const thoughtCount = decidingPhases.length + thoughtLog.length;

    return (
        <TaskPane className="w-[224px] shrink-0 overflow-y-auto">
            <TaskPaneSection
                title="Participants"
                aside={totalActive}
                icon={<Icon name="users" size={32} />}
                special
                collapsible={false}
            >
                <div className="flex flex-col gap-0.5">
                    {visibleAiPersonas.map((persona) => {
                        const id = persona.id ?? '';
                        const isActive = participantSet.has(id);
                        const isWorking = activePersonaIds.has(id);
                        const phase = personaPhases.get(id);
                        const isDeciding = phase?.phase === 'deciding';

                        return (
                            <ContactRow
                                key={id}
                                selected={isActive}
                                dimmed={!isActive}
                                aria-pressed={isActive}
                                onClick={() => onToggleParticipant(id)}
                                title={
                                    isActive
                                        ? 'Click to remove from chat'
                                        : 'Click to add to chat'
                                }
                                picture={
                                    <Portrait
                                        who={id}
                                        alt={persona.name ?? id}
                                        size={24}
                                    />
                                }
                                name={persona.name ?? id.slice(0, 8)}
                                meta={
                                    isDeciding ? (
                                        <Presence
                                            status="away"
                                            size="small"
                                            active
                                            label="Deciding whether to respond"
                                        />
                                    ) : (
                                        <Presence
                                            status={
                                                isActive ? 'online' : 'offline'
                                            }
                                            size="small"
                                            active={isWorking}
                                        />
                                    )
                                }
                            />
                        );
                    })}
                </div>

                {(hiddenCount > 0 || libraryOpen) &&
                    participantPersonaIds.length > 0 && (
                        <TaskPaneLink
                            icon={<Icon name={showLibrary ? 'up' : 'add'} />}
                            onClick={() => setLibraryOpen((v) => !v)}
                        >
                            {showLibrary
                                ? 'Hide personas not in this room'
                                : `Add personas… (${hiddenCount} more)`}
                        </TaskPaneLink>
                    )}

                {userPersona && (
                    <ContactRow
                        selected
                        picture={
                            <Portrait
                                who={userPersona.name ?? selectedPersonaId}
                                alt={userPersona.name ?? 'You'}
                                size={24}
                            />
                        }
                        name={userPersona.name ?? 'You'}
                        meta="you"
                    />
                )}

                {participantPersonaIds.length === 0 &&
                    aiPersonas.length > 0 && (
                        <StatusText variant="bar" tone="caution">
                            No AI participants — AI won't respond.
                        </StatusText>
                    )}
                {saveError && (
                    <Text as="p" tone="danger">
                        {saveError}
                    </Text>
                )}
                {hasChanges && (
                    <div className="flex gap-1">
                        <CommandButton
                            size="small"
                            disabled={isSaving}
                            className="flex-1"
                            onClick={onReset}
                        >
                            Reset
                        </CommandButton>
                        <CommandButton
                            size="small"
                            isDefault
                            type="button"
                            disabled={isSaving}
                            className="flex-1"
                            onClick={onSave}
                        >
                            {isSaving ? 'Saving...' : 'Save'}
                        </CommandButton>
                    </div>
                )}
            </TaskPaneSection>

            <TaskPaneSection
                title={
                    <span
                        className="flex items-center gap-1"
                        title="Why each persona chose to speak, stay silent, or got cut off"
                    >
                        Thought Log
                        {decidingPhases.length > 0 ? (
                            <Presence
                                status="away"
                                size="small"
                                active
                                label="A persona is deciding right now"
                            />
                        ) : null}
                    </span>
                }
                aside={thoughtCount > 0 ? thoughtCount : undefined}
                open={thoughtsOpen}
                onOpenChange={setThoughtsOpen}
            >
                <div className="flex flex-col gap-1">
                    {decidingPhases.map((phase) => {
                        const personaId = phase.personaName;
                        const persona = personas.find(
                            (p) => p.id === personaId,
                        );
                        const name = persona?.name ?? personaId?.slice(0, 8);
                        const decisionText =
                            phase.phase === 'deciding'
                                ? phase.decisionText
                                : '';
                        return (
                            <ChatMessage
                                key={phase.messageId ?? personaId}
                                tone="caution"
                                live
                                sender={`${name}…`}
                                picture={
                                    <Portrait
                                        who={personaId}
                                        alt={name}
                                        size={16}
                                    />
                                }
                            >
                                {decisionText ? (
                                    <Text
                                        as="div"
                                        mono
                                        size="chrome"
                                        tone="muted"
                                        className="max-h-[100px] overflow-y-auto whitespace-pre-wrap break-all"
                                    >
                                        {decisionText}
                                    </Text>
                                ) : null}
                            </ChatMessage>
                        );
                    })}

                    {thoughtLog.length === 0 && decidingPhases.length === 0 ? (
                        <Text as="p" tone="muted" className="py-2 text-center">
                            No decisions yet
                        </Text>
                    ) : (
                        thoughtLog.map((entry) => {
                            const look = kindLook(entry.kind);
                            return (
                                <ChatMessage
                                    key={entry.key}
                                    tone={look.tone}
                                    sender={entry.personaName}
                                    picture={
                                        entry.personaId ? (
                                            <Portrait
                                                who={entry.personaId}
                                                alt={entry.personaName}
                                                size={16}
                                            />
                                        ) : undefined
                                    }
                                    time={
                                        <Text tone={look.tone} strong>
                                            {look.label}
                                        </Text>
                                    }
                                >
                                    {entry.reason ? (
                                        <Text as="div" italic tone="muted">
                                            {entry.reason}
                                        </Text>
                                    ) : null}
                                    {entry.salience !== null ? (
                                        <Text
                                            as="div"
                                            mono
                                            size="chrome"
                                            tone="caution"
                                        >
                                            salience=
                                            {entry.salience.toFixed(2)}
                                            {entry.cancelScore !== null
                                                ? ` cancel=${entry.cancelScore.toFixed(2)}`
                                                : ''}
                                        </Text>
                                    ) : null}
                                    {entry.instruction &&
                                    (entry.kind === 'decline' ||
                                        entry.kind === 'skip-obvious') ? (
                                        <Text as="div" tone="caution">
                                            → {entry.instruction}
                                        </Text>
                                    ) : null}
                                </ChatMessage>
                            );
                        })
                    )}
                </div>
            </TaskPaneSection>
        </TaskPane>
    );
}

function ChatBubble({
    message,
    onDelete,
    onTruncate,
    onReprompt,
    onRemember,
    rememberPending,
    busy,
    personas,
    userPersonaId,
    isGenerating,
}: {
    message: RealtimeChatMessage;
    onDelete: () => void;
    onTruncate: () => void;
    onReprompt: () => void;
    onRemember: () => void;
    rememberPending: boolean;
    busy: boolean;
    personas: Persona[];
    userPersonaId: string;
    isGenerating: boolean;
}) {
    const isUser = message.senderType === 'user';
    const [showDetails, setShowDetails] = useState(false);

    const senderName = useMemo(() => {
        const persona = personas.find((p) => p.id === message.senderId);
        if (persona?.name) {
            return isUser ? `${persona.name} (you)` : persona.name;
        }
        return isUser ? 'You' : message.senderId.slice(0, 8);
    }, [message.senderId, isUser, personas]);

    const hasDetails = !!(
        message.reasoning ||
        (message.generationEvents?.length ?? 0) > 0 ||
        message.metadata?.modelName ||
        message.metadata?.provider
    );

    const appraisalData = useMemo(
        () => parseAppraisal(message.appraisal),
        [message.appraisal],
    );

    const isAppraisalStop = appraisalData?.stop === true;
    const isDirectedAtUser =
        !!appraisalData &&
        !!userPersonaId &&
        appraisalData.personaId === userPersonaId;

    const userPersonaName =
        personas.find((p) => p.id === userPersonaId)?.name ?? 'you';

    const actions = (withDetails: boolean) => (
        <Toolbar variant="bare" labels="none" aria-label="Message actions">
            {withDetails && hasDetails && (
                <ToolbarButton
                    icon={<Icon name="info" />}
                    pressed={showDetails}
                    title={showDetails ? 'Hide details' : 'Details'}
                    aria-label={showDetails ? 'Hide' : 'Details'}
                    onClick={() => setShowDetails((v) => !v)}
                />
            )}
            <ToolbarButton
                icon={<Icon name="redo" />}
                disabled={busy}
                title="Redo"
                aria-label="redo"
                onClick={onReprompt}
            />
            <ToolbarButton
                icon={<Icon name="scissors" />}
                disabled={busy}
                title="Cut the conversation after this message"
                aria-label="cut"
                onClick={onTruncate}
            />
            <ToolbarButton
                icon={<Icon name="delete" />}
                disabled={busy}
                title="Delete"
                aria-label="del"
                onClick={onDelete}
            />
            {withDetails && (
                <ToolbarButton
                    icon={<Icon name="memory-graph" />}
                    disabled={busy || rememberPending}
                    title="Capture as memory for personas present"
                    aria-label={rememberPending ? '...' : 'remember'}
                    onClick={onRemember}
                />
            )}
        </Toolbar>
    );

    if (message.kind === 'emote') {
        return (
            <ChatMessage kind="emote" sender={senderName}>
                {message.content}
            </ChatMessage>
        );
    }

    if (isDirectedAtUser && appraisalData) {
        return (
            <ChatMessage
                tone="neutral"
                picture={<Icon name="film" />}
                sender={`Your turn, ${userPersonaName}`}
                actions={actions(false)}
            >
                {appraisalData.instruction}
                {appraisalData.reason && (
                    <Text as="div" size="chrome" tone="muted" italic>
                        {appraisalData.reason}
                    </Text>
                )}
            </ChatMessage>
        );
    }

    const time = formatTime(message.sendAt);

    return (
        <ChatMessage
            own={isUser}
            tone={isAppraisalStop ? 'caution' : undefined}
            live={isGenerating}
            picture={
                isAppraisalStop ? (
                    <Icon name="scales" label="Appraisal" />
                ) : (
                    <Portrait
                        who={isUser ? senderName : message.senderId}
                        alt={senderName}
                        size={24}
                    />
                )
            }
            sender={isAppraisalStop ? 'Appraisal' : senderName}
            time={time || undefined}
            actions={actions(true)}
            error={message.error || undefined}
            details={
                showDetails && hasDetails ? (
                    <>
                        {(message.metadata?.modelName ||
                            message.metadata?.provider) && (
                            <div>
                                <Text as="div" tone="neutral" strong>
                                    Model
                                </Text>
                                <Text as="div">
                                    {message.metadata?.provider
                                        ? `${message.metadata.provider} / ${message.metadata.modelName ?? '?'}`
                                        : (message.metadata?.modelName ?? '')}
                                </Text>
                            </div>
                        )}

                        {message.reasoning && (
                            <div>
                                <Text as="div" tone="neutral" strong>
                                    Reasoning
                                </Text>
                                <Text
                                    as="div"
                                    tone="muted"
                                    className="whitespace-pre-wrap"
                                >
                                    {message.reasoning}
                                </Text>
                            </div>
                        )}

                        {(message.generationEvents?.length ?? 0) > 0 && (
                            <div>
                                <Text as="div" tone="neutral" strong>
                                    Generation Events
                                </Text>
                                {message.generationEvents
                                    ?.filter(
                                        (e) =>
                                            ![
                                                'message',
                                                'reasoning',
                                                'appraisal',
                                            ].includes(e.event),
                                    )
                                    .map((e, i) => (
                                        <div
                                            key={`${e.event}-${e.at}-${i}`}
                                            className="mt-1 flex gap-2"
                                        >
                                            <Text
                                                tone="muted"
                                                strong
                                                className="min-w-[80px]"
                                            >
                                                {e.event}:
                                            </Text>
                                            <Text>
                                                {e.event === 'overseerStop'
                                                    ? '(stopped)'
                                                    : e.event === 'attend'
                                                      ? (personas.find(
                                                            (p) =>
                                                                p.id === e.data,
                                                        )?.name ??
                                                        e.data.slice(0, 8))
                                                      : e.data.slice(0, 50)}
                                                {e.event !== 'overseerStop' &&
                                                e.event !== 'attend' &&
                                                e.data.length > 50
                                                    ? '...'
                                                    : ''}
                                            </Text>
                                        </div>
                                    ))}
                            </div>
                        )}
                    </>
                ) : undefined
            }
        >
            {message.content ? (
                <MarkdownContent
                    content={message.content}
                    isStreaming={isGenerating}
                />
            ) : null}
        </ChatMessage>
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

    return <RichText ref={containerRef} />;
}

function StreamingIndicator({
    info,
    personas,
}: {
    info: GenerationPhase;
    personas?: Persona[];
}) {
    if (info.phase === 'waiting') {
        return (
            <div className="flex items-center gap-2 px-2 py-1">
                <Icon name="hourglass" />
                <Text tone="muted">Waiting for responses...</Text>
            </div>
        );
    }
    if (info.phase === 'deciding') {
        return null;
    }
    if (info.phase === 'typing' || info.phase === 'streaming') {
        const personaId = info.personaName;
        const resolvedName =
            personas?.find((p) => p.id === personaId)?.name ??
            personaId.slice(0, 8);
        return (
            <ChatMessage
                live
                picture={
                    <Portrait who={personaId} alt={resolvedName} size={24} />
                }
                sender={resolvedName}
                time={
                    info.phase === 'streaming'
                        ? `(${info.charCount} chars)`
                        : undefined
                }
            >
                <Text tone="muted">typing…</Text>
            </ChatMessage>
        );
    }
    return (
        <div className="flex items-center gap-2 p-1">
            <Icon name="hourglass" />
            <Text tone="muted">Working...</Text>
        </div>
    );
}

function formatTime(at: number | string | null | undefined): string {
    if (!at) return '';
    try {
        const d = new Date(at);
        return d.toLocaleTimeString([], {
            hour: '2-digit',
            minute: '2-digit',
        });
    } catch {
        return '';
    }
}

export function ScenarioChip({
    partyId,
    chatGroupId,
    scenario,
}: {
    partyId: string;
    chatGroupId: string;
    scenario: string | null;
}) {
    const queryClient = useQueryClient();
    const [isEditing, setIsEditing] = useState(false);
    const [draft, setDraft] = useState(scenario ?? '');

    useEffect(() => {
        if (!isEditing) setDraft(scenario ?? '');
    }, [scenario, isEditing]);

    const updateScenario = usePutPartyIdChatGroupsChatGroupIdScenario({
        mutation: {
            onSuccess: () => {
                queryClient.invalidateQueries({
                    queryKey: getGetPartyIdChatGroupsQueryKey(partyId),
                });
                setIsEditing(false);
            },
        },
    });

    const handleSave = () => {
        const trimmed = draft.trim();
        updateScenario.mutate({
            id: partyId,
            chatGroupId,
            data: { scenario: trimmed === '' ? null : trimmed },
        });
    };

    const handleCancel = () => {
        setDraft(scenario ?? '');
        setIsEditing(false);
    };

    const truncated =
        scenario && scenario.length > 40
            ? `${scenario.slice(0, 40)}…`
            : scenario;

    return (
        <>
            <CommandButton
                size="small"
                icon={<Icon name={scenario ? 'film' : 'add'} />}
                className="min-w-0 max-w-[240px]"
                pressed={isEditing}
                onClick={() => setIsEditing((v) => !v)}
                title={scenario ?? 'Set the in-fiction setting / scenario'}
            >
                <span className="truncate">
                    {scenario ? truncated : 'Add scenario'}
                </span>
            </CommandButton>
            {isEditing && (
                <Dialog
                    title="Scenario"
                    icon={<Icon name="film" />}
                    contained
                    className="w-[320px] max-w-[92%]"
                    onClose={handleCancel}
                    buttons={
                        <>
                            <CommandButton
                                onClick={handleCancel}
                                disabled={updateScenario.isPending}
                            >
                                Cancel
                            </CommandButton>
                            <CommandButton
                                isDefault
                                type="button"
                                onClick={handleSave}
                                disabled={updateScenario.isPending}
                            >
                                {updateScenario.isPending ? '...' : 'Save'}
                            </CommandButton>
                        </>
                    }
                >
                    <FieldRow
                        label="Scenario"
                        layout="stacked"
                        error={
                            updateScenario.isError
                                ? 'Failed to save scenario.'
                                : undefined
                        }
                    >
                        <TextArea
                            rows={4}
                            maxLength={500}
                            className="w-full"
                            placeholder="In-fiction setting (optional). Leave blank to clear."
                            value={draft}
                            onChange={(e) => setDraft(e.currentTarget.value)}
                        />
                    </FieldRow>
                </Dialog>
            )}
        </>
    );
}
