import { useHotkey } from '@tanstack/react-hotkeys';
import { useQueryClient } from '@tanstack/react-query';
import DOMPurify from 'dompurify';
import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import * as smd from 'streaming-markdown';
import {
    getGetPartyIdChatGroupsChatGroupIdParticipantsQueryKey,
    getGetPartyIdChatGroupsQueryKey,
    useDeletePartyIdChatGroupsChatGroupIdMessagesAfterMessageId,
    useDeletePartyIdChatGroupsChatGroupIdMessagesMessageId,
    useGetPartyIdChatGroupsChatGroupIdParticipantsSuspense,
    useGetPartyIdSuspense,
    usePostPartyIdCancel,
    usePostPartyIdProceed,
    usePostPartyIdPrompt,
    usePostPartyIdRepromptMessageId,
    usePutPartyIdChatGroupsChatGroupIdParticipants,
    usePutPartyIdChatGroupsChatGroupIdScenario,
} from '#api/party-zone';
import {
    type ActiveGenerationPhase,
    type DeclinedDecision,
    type GenerationPhase,
    type RaceEvaluation,
    type RealtimeChatMessage,
    type RealtimeConnectionStatus,
    useActiveGenerationPhases,
    useChatGroupGenerationState,
    useChatGroupMessages,
    useDeclinedDecisions,
    useRaceEvaluations,
    useRealtimeConnectionStatus,
    useRealtimeStoreActions,
} from '#lib/realtime-store';
import type { Persona } from '../../api/model';
import { ROOT_PARTY_ID } from '../../lib/chat-api';

export interface ChatViewProps {
    chatGroupId: string;
    partyName?: string;
    scenario?: string | null;
}

interface GroupChatWindowProps {
    partyId?: string;
    chatGroupId?: string;
    partyName?: string;
    scenario?: string | null;
}

export default function GroupChatWindow({
    chatGroupId,
    partyName,
    scenario,
}: GroupChatWindowProps) {
    if (!chatGroupId) {
        return (
            <div
                className="app-surface h-full flex items-center justify-center"
                style={{ background: 'transparent', color: '#808080' }}
            >
                Open a chat group from the Chat Launcher to start messaging.
            </div>
        );
    }

    return (
        <ChatView
            chatGroupId={chatGroupId}
            partyName={partyName}
            scenario={scenario}
        />
    );
}

export function ChatView({ chatGroupId, partyName, scenario }: ChatViewProps) {
    const apiPartyId = ROOT_PARTY_ID;
    const queryClient = useQueryClient();
    const [messages, setMessages] = useState<RealtimeChatMessage[]>([]);
    const [inputValue, setInputValue] = useState('');
    const [selectedPersonaId, setSelectedPersonaId] = useState('');

    // Participant management – backed by actual party state
    const [participantPersonaIds, setParticipantPersonaIds] = useState<
        string[]
    >([]);
    const [savedParticipantPersonaIds, setSavedParticipantPersonaIds] =
        useState<string[]>([]);
    // Tracks which chatGroupId we've already seeded participant state for —
    // resets when the user switches rooms so per-room cast loads correctly.
    const initializedForChatGroupId = useRef<string | null>(null);
    const lastSavedUserPersonaId = useRef<string | null>(null);

    const scrollRef = useRef<HTMLDivElement | null>(null);
    const [isNearBottom, setIsNearBottom] = useState(true);
    const [unreadCount, setUnreadCount] = useState(0);

    const activeGenerations = useChatGroupGenerationState(chatGroupId ?? '');
    const connectionStatus = useRealtimeConnectionStatus(apiPartyId);
    const generationPhases = useActiveGenerationPhases(chatGroupId ?? '');
    const declinedDecisions = useDeclinedDecisions(chatGroupId ?? '');
    const raceEvaluations = useRaceEvaluations(chatGroupId ?? '');
    const realtimeMessages = useChatGroupMessages(chatGroupId ?? '');
    const { connectPartyRealtime, disconnectPartyRealtime } =
        useRealtimeStoreActions();

    const partyDetailsQuery = useGetPartyIdSuspense(apiPartyId);
    const chatGroupParticipantsQuery =
        useGetPartyIdChatGroupsChatGroupIdParticipantsSuspense(
            apiPartyId,
            chatGroupId,
        );

    // Seed participant state from per-chat-group server data on first load.
    // Each chat group owns its own cast (AI participants + the user-persona
    // marked with isUser:true), so reload from this room, not the party-level list.
    useEffect(() => {
        if (initializedForChatGroupId.current === chatGroupId) return;
        if (chatGroupParticipantsQuery.data.status !== 200) return;
        const roomParticipants = chatGroupParticipantsQuery.data.data ?? [];
        const aiIds = roomParticipants
            .filter((p) => !p.isUser && p.id)
            .map((p) => p.id as string);
        const userId = roomParticipants.find((p) => p.isUser && p.id)?.id ?? '';
        setParticipantPersonaIds(aiIds);
        setSavedParticipantPersonaIds(aiIds);
        setSelectedPersonaId(userId);
        lastSavedUserPersonaId.current = userId;
        initializedForChatGroupId.current = chatGroupId;
    }, [chatGroupId, chatGroupParticipantsQuery.data]);

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
                    const savedPersonaIds = (variables.data.participants ?? [])
                        .filter((p) => !p.isUser && p.id !== null)
                        .map(
                            (p) =>
                                p.id ?? 'unreachable but tsc is being annoying',
                        );
                    setSavedParticipantPersonaIds(savedPersonaIds);
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

    useEffect(() => {
        if (!chatGroupId) {
            return;
        }

        connectPartyRealtime(apiPartyId);

        return () => {
            disconnectPartyRealtime(apiPartyId);
        };
    }, [
        apiPartyId,
        chatGroupId,
        connectPartyRealtime,
        disconnectPartyRealtime,
    ]);

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

    // Keep selected persona in sync: auto-select first promptable persona if current selection is unavailable.
    // Wait for the per-chat-group seed to land first so we don't race over a freshly-loaded user-persona.
    useEffect(() => {
        if (initializedForChatGroupId.current !== chatGroupId) return;
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
    }, [
        partyDetailsQuery.data,
        participantPersonaIds,
        selectedPersonaId,
        chatGroupId,
    ]);

    // Auto-save user persona to backend whenever selection changes
    useEffect(() => {
        if (lastSavedUserPersonaId.current === selectedPersonaId) return;
        if (partyDetailsQuery.data.status !== 200) return;
        if (initializedForChatGroupId.current !== chatGroupId) return;

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
            chatGroupId,
            data: { participants },
        });
    }, [
        selectedPersonaId,
        partyDetailsQuery.data,
        savedParticipantPersonaIds,
        apiPartyId,
        chatGroupId,
        saveParticipantsMutation.mutate,
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

    // Detect appraisal stop directed at the user's persona that hasn't been responded to yet
    const pendingInstruction = useMemo(() => {
        if (!selectedPersonaId) return null;
        let lastStopIdx = -1;
        let lastInstruction: { instruction?: string; reason?: string } | null =
            null;
        for (let i = 0; i < uniqueMessages.length; i++) {
            const msg = uniqueMessages[i];
            if (!msg.appraisal) continue;
            try {
                const o = JSON.parse(msg.appraisal);
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
        const personasToSave = participantPersonaIds.map((id) => ({
            id,
            name: personaNameMap.get(id) ?? id,
            isUser: false,
        }));
        const userParticipant = selectedPersonaId
            ? [
                  {
                      id: selectedPersonaId,
                      name:
                          personaNameMap.get(selectedPersonaId) ??
                          selectedPersonaId,
                      isUser: true,
                  },
              ]
            : [];
        saveParticipantsMutation.mutate({
            id: apiPartyId,
            chatGroupId,
            data: { participants: [...personasToSave, ...userParticipant] },
        });
    }, [
        apiPartyId,
        chatGroupId,
        participantPersonaIds,
        selectedPersonaId,
        partyDetailsQuery.data,
        saveParticipantsMutation.mutate,
    ]);

    const partyPersonas = partyDetailsQuery.data.data.personaParticipants;
    const promptablePersonas = partyPersonas.filter(
        (p) => !participantPersonaIds.includes(p.id ?? ''),
    );
    const selectedPersonaName =
        partyPersonas.find((p) => p.id === selectedPersonaId)?.name ??
        selectedPersonaId;

    const activePersonaIds = useMemo(() => {
        const ids = new Set<string>();
        for (const phase of generationPhases) {
            if (
                (phase.phase === 'deciding' ||
                    phase.phase === 'typing' ||
                    phase.phase === 'streaming') &&
                phase.personaName
            ) {
                ids.add(phase.personaName);
            }
        }
        return ids;
    }, [generationPhases]);

    const personaPhases = useMemo(() => {
        const map = new Map<string, ActiveGenerationPhase>();
        for (const phase of generationPhases) {
            if (
                (phase.phase === 'deciding' ||
                    phase.phase === 'typing' ||
                    phase.phase === 'streaming') &&
                phase.personaName
            ) {
                map.set(phase.personaName, phase);
            }
        }
        return map;
    }, [generationPhases]);

    return (
        <div
            className="app-surface flex h-full"
            style={{ background: 'transparent' }}
        >
            {/* Main chat column */}
            <div className="flex-1 flex flex-col min-w-0 h-full">
                {/* Header toolbar */}
                <div
                    className="p-2"
                    style={{
                        borderBottom: '1px solid rgba(255,255,255,0.7)',
                        background:
                            'linear-gradient(180deg, rgba(255,255,255,0.7) 0%, rgba(241,245,249,0.55) 100%)',
                        backdropFilter: 'blur(8px)',
                        WebkitBackdropFilter: 'blur(8px)',
                    }}
                >
                    <div className="flex items-center justify-between">
                        <span className="flex items-center gap-1.5">
                            <ConnectionDot status={connectionStatus} />
                            <span style={{ fontWeight: 600, color: '#000' }}>
                                {partyName ?? apiPartyId}
                            </span>
                            <ScenarioChip
                                partyId={apiPartyId}
                                chatGroupId={chatGroupId}
                                scenario={scenario ?? null}
                            />
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
                        className="flex-1 overflow-y-auto p-2 space-y-2 m-1 bg-white border border-xp-sunken-edge border-r-white border-b-white"
                    >
                        {uniqueMessages.map((message) => {
                            const generating = activeGenerationSet.has(
                                message.messageId,
                            );
                            // Hide while generating with nothing to show yet
                            if (
                                generating &&
                                !message.content &&
                                !message.error
                            )
                                return null;
                            // Hide appraisal-only messages — they live in the sidebar Thought Log,
                            // unless this appraisal is a "stop" directed at the user (shows "Your turn" card)
                            if (
                                !message.content &&
                                !message.error &&
                                message.appraisal
                            ) {
                                try {
                                    const a = JSON.parse(message.appraisal);
                                    const stop = a.stop ?? a.Stop;
                                    const pid = a.personaId ?? a.PersonaId;
                                    if (stop && pid === selectedPersonaId) {
                                        // keep — "Your turn" card
                                    } else {
                                        return null;
                                    }
                                } catch {
                                    return null;
                                }
                            }
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
                                                senderId:
                                                    selectedPersonaId || null,
                                            },
                                        })
                                    }
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
                            background:
                                'linear-gradient(180deg, #F5F5ED 0%, #ECE9D8 100%)',
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
                                <span
                                    style={{
                                        fontWeight: 600,
                                        color: '#806600',
                                    }}
                                >
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
                            <p
                                className="text-[10px]"
                                style={{ color: '#c00' }}
                            >
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
                                    setSelectedPersonaId(
                                        event.currentTarget.value,
                                    )
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
                                            senderId: selectedPersonaId || null,
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

            {/* Right sidebar: participants + thoughts */}
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
    const aiPersonas = personas.filter((p) => !p.isUser);
    const userPersona = personas.find((p) => p.id === selectedPersonaId);
    const totalActive =
        participantPersonaIds.length + (selectedPersonaId ? 1 : 0);

    const decidingPhases = livePhases.filter((p) => p.phase === 'deciding');

    // Combined thought log: "go" decisions from persisted messages + "skip" decisions
    // captured at runtime + stop-signal "race" outcomes streamed live.
    //
    // Each entry carries a `kind` discriminator so the renderer can colour-code variants:
    //   go               — persona decided to speak (LLM said yes)
    //   decline          — persona decided NOT to speak (LLM said no)
    //   skip-obvious     — pre-gate skip (math decisive, no LLM call)
    //   race-cancel-decision   — race killed an in-flight decision phase
    //   race-cancel-generation — race killed an in-flight generation
    //   race-past-pnr          — past PNR; ship-as-is, repair queued
    //   race-continue          — race let it ride
    //   emote            — race-cancelled message that shipped as an in-character emote
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

        // Decisions persisted on messages: appraisal.stop discriminates go vs decline.
        // Race-cancelled messages get kind=emote (the appraisal carries raceCancelled=true).
        for (const msg of allMessages) {
            if (!msg.appraisal) continue;
            try {
                const raw = JSON.parse(msg.appraisal);
                const personaId = raw.personaId ?? raw.PersonaId ?? null;
                const persona = personas.find((p) => p.id === personaId);
                const isStop = !!(raw.stop ?? raw.Stop);
                const isRaceCancelled = raw.raceCancelled === true;
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
                    reason: raw.reason ?? raw.Reason ?? null,
                    instruction: raw.instruction ?? raw.Instruction ?? null,
                    kind,
                    salience: null,
                    cancelScore: null,
                    sortKey: msg.messageId,
                });
            } catch {
                /* ignore */
            }
        }

        // Pre-gate skips streamed via the declined event (ephemeral, in-memory).
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

        // Race outcomes from the stop-signal classifier. Use sendAt for sorting since
        // race events can fire multiple times per triggering message.
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

    const sectionLabel = (text: string, badge?: number) => (
        <div
            style={{
                padding: '4px 8px 2px',
                fontSize: 10,
                fontWeight: 700,
                color: '#808080',
                textTransform: 'uppercase',
                letterSpacing: '0.5px',
                display: 'flex',
                alignItems: 'center',
                gap: 4,
            }}
        >
            {text}
            {badge !== undefined && badge > 0 && (
                <span
                    style={{
                        display: 'inline-flex',
                        alignItems: 'center',
                        justifyContent: 'center',
                        width: 14,
                        height: 14,
                        borderRadius: '50%',
                        background: '#808080',
                        color: '#fff',
                        fontSize: 8,
                        fontWeight: 700,
                    }}
                >
                    {badge}
                </span>
            )}
        </div>
    );

    return (
        <div
            className="participant-sidebar"
            style={{
                width: 180,
                minWidth: 180,
                height: '100%',
                display: 'grid',
                gridTemplateRows: 'auto 1fr',
                overflow: 'hidden',
            }}
        >
            {/* ── Participants section (auto height row) ── */}
            <div style={{ overflow: 'hidden auto' }}>
                {sectionLabel(`Participants — ${totalActive}`)}

                {/* AI list */}
                {aiPersonas.map((persona) => {
                    const id = persona.id ?? '';
                    const isActive = participantSet.has(id);
                    const isWorking = activePersonaIds.has(id);
                    const phase = personaPhases.get(id);
                    const isDeciding = phase?.phase === 'deciding';

                    return (
                        <button
                            key={id}
                            type="button"
                            className={`participant-row w-full text-left${isActive ? ' active' : ''}`}
                            onClick={() => onToggleParticipant(id)}
                            title={
                                isActive
                                    ? 'Click to remove from chat'
                                    : 'Click to add to chat'
                            }
                        >
                            <img
                                src={`https://robohash.org/${encodeURIComponent(id)}?size=32x32`}
                                alt={persona.name ?? id}
                                style={{
                                    width: 24,
                                    height: 24,
                                    flexShrink: 0,
                                    imageRendering: 'pixelated',
                                }}
                            />
                            <span
                                className={isWorking ? 'animate-pulse' : ''}
                                style={{
                                    flex: 1,
                                    overflow: 'hidden',
                                    textOverflow: 'ellipsis',
                                    whiteSpace: 'nowrap',
                                    fontSize: 11,
                                    fontWeight: isActive ? 600 : 400,
                                    color: isActive ? '#000' : '#808080',
                                }}
                            >
                                {persona.name ?? id.slice(0, 8)}
                            </span>
                            {isDeciding ? (
                                <span
                                    className="animate-pulse"
                                    style={{
                                        fontSize: 8,
                                        color: '#CC8800',
                                        flexShrink: 0,
                                    }}
                                    title="Deciding whether to respond"
                                >
                                    ●
                                </span>
                            ) : (
                                <span
                                    className={`participant-status-dot${isActive ? ' online' : ' offline'}${isWorking ? ' working' : ''}`}
                                />
                            )}
                        </button>
                    );
                })}

                {/* User row */}
                {userPersona && (
                    <>
                        <div
                            style={{
                                margin: '4px 8px 0',
                                borderTop: '1px solid #D4D0C8',
                            }}
                        />
                        <div className="participant-row active">
                            <img
                                src={`https://robohash.org/${encodeURIComponent(userPersona.name ?? selectedPersonaId)}?size=32x32&set=set5`}
                                alt={userPersona.name ?? 'You'}
                                style={{
                                    width: 24,
                                    height: 24,
                                    flexShrink: 0,
                                    imageRendering: 'pixelated',
                                }}
                            />
                            <span
                                style={{
                                    flex: 1,
                                    overflow: 'hidden',
                                    textOverflow: 'ellipsis',
                                    whiteSpace: 'nowrap',
                                    fontSize: 11,
                                    fontWeight: 600,
                                    color: '#003399',
                                }}
                            >
                                {userPersona.name ?? 'You'}
                            </span>
                            <span
                                style={{
                                    fontSize: 8,
                                    color: '#808080',
                                    flexShrink: 0,
                                }}
                            >
                                you
                            </span>
                        </div>
                    </>
                )}

                {/* Inline status / save controls */}
                {participantPersonaIds.length === 0 &&
                    aiPersonas.length > 0 && (
                        <div
                            style={{
                                padding: '3px 8px',
                                fontSize: 10,
                                color: '#996600',
                                background: '#FFFBEA',
                                borderTop: '1px solid #E6D87A',
                            }}
                        >
                            No AI participants — AI won't respond.
                        </div>
                    )}
                {saveError && (
                    <div
                        style={{
                            padding: '3px 8px',
                            fontSize: 10,
                            color: '#c00',
                            borderTop: '1px solid #ACA899',
                        }}
                    >
                        {saveError}
                    </div>
                )}
                {hasChanges && (
                    <div
                        className="flex gap-1 p-1"
                        style={{ borderTop: '1px solid #ACA899' }}
                    >
                        <button
                            type="button"
                            disabled={isSaving}
                            className="flex-1 text-[10px]"
                            onClick={onReset}
                        >
                            Reset
                        </button>
                        <button
                            type="button"
                            disabled={isSaving}
                            className="flex-1 text-[10px]"
                            onClick={onSave}
                        >
                            {isSaving ? 'Saving...' : 'Save'}
                        </button>
                    </div>
                )}
            </div>

            {/* ── Thought Log section (1fr grid row, always visible) ── */}
            <div
                style={{
                    borderTop: '2px solid #ACA899',
                    display: 'flex',
                    flexDirection: 'column',
                    overflow: 'hidden',
                }}
            >
                {sectionLabel(
                    'Thought Log',
                    decidingPhases.length + thoughtLog.length,
                )}

                <div style={{ flex: 1, overflowY: 'auto' }}>
                    {/* Live: personas currently deciding */}
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
                            <div
                                key={phase.messageId ?? personaId}
                                style={{
                                    borderBottom: '1px solid #E6D87A',
                                    background: '#FFFBEA',
                                    borderLeft: '3px solid #CC8800',
                                    padding: '5px 8px',
                                }}
                            >
                                <div className="flex items-center gap-1.5 mb-1">
                                    <img
                                        src={`https://robohash.org/${encodeURIComponent(personaId)}?size=32x32`}
                                        alt={name}
                                        style={{
                                            width: 14,
                                            height: 14,
                                            imageRendering: 'pixelated',
                                            flexShrink: 0,
                                        }}
                                    />
                                    <span
                                        className="animate-pulse"
                                        style={{
                                            fontSize: 10,
                                            fontWeight: 700,
                                            color: '#806600',
                                        }}
                                    >
                                        {name}…
                                    </span>
                                </div>
                                {decisionText && (
                                    <div
                                        style={{
                                            fontSize: 9,
                                            color: '#666',
                                            fontFamily: 'monospace',
                                            whiteSpace: 'pre-wrap',
                                            wordBreak: 'break-all',
                                            maxHeight: 100,
                                            overflowY: 'auto',
                                            lineHeight: 1.4,
                                        }}
                                    >
                                        {decisionText}
                                    </div>
                                )}
                            </div>
                        );
                    })}

                    {/* Settled decisions (go + skip), newest first */}
                    {thoughtLog.length === 0 && decidingPhases.length === 0 ? (
                        <div
                            style={{
                                padding: '10px 8px',
                                fontSize: 10,
                                color: '#ACA899',
                                textAlign: 'center',
                                lineHeight: 1.5,
                            }}
                        >
                            No decisions yet
                        </div>
                    ) : (
                        thoughtLog.map((entry) => {
                            // Per-variant colour: green = go, amber = decline/skip,
                            // red = race-cancel, purple = past-pnr/continue/emote.
                            const variantStyle = (() => {
                                switch (entry.kind) {
                                    case 'go':
                                        return { color: '#009900', label: 'go' };
                                    case 'decline':
                                        return { color: '#CC8800', label: 'decline' };
                                    case 'skip-obvious':
                                        return { color: '#CC8800', label: 'skip' };
                                    case 'race-cancel-decision':
                                        return { color: '#C03030', label: 'race·cancel·dec' };
                                    case 'race-cancel-generation':
                                        return { color: '#C03030', label: 'race·cancel·gen' };
                                    case 'race-past-pnr':
                                        return { color: '#7A40C0', label: 'race·past-pnr' };
                                    case 'race-continue':
                                        return { color: '#7A40C0', label: 'race·continue' };
                                    case 'emote':
                                        return { color: '#7A40C0', label: 'emote' };
                                }
                            })();
                            return (
                                <div
                                    key={entry.key}
                                    style={{
                                        borderBottom: '1px solid #D4D0C8',
                                        padding: '4px 8px',
                                        borderLeft: `3px solid ${variantStyle.color}`,
                                    }}
                                >
                                    <div className="flex items-center gap-1.5">
                                        {entry.personaId && (
                                            <img
                                                src={`https://robohash.org/${encodeURIComponent(entry.personaId)}?size=32x32`}
                                                alt={entry.personaName}
                                                style={{
                                                    width: 13,
                                                    height: 13,
                                                    imageRendering: 'pixelated',
                                                    flexShrink: 0,
                                                }}
                                            />
                                        )}
                                        <span
                                            style={{
                                                fontSize: 10,
                                                fontWeight: 600,
                                                color: '#333',
                                                flex: 1,
                                                overflow: 'hidden',
                                                textOverflow: 'ellipsis',
                                                whiteSpace: 'nowrap',
                                            }}
                                        >
                                            {entry.personaName}
                                        </span>
                                        <span
                                            style={{
                                                fontSize: 9,
                                                color: variantStyle.color,
                                                fontWeight: 700,
                                                flexShrink: 0,
                                            }}
                                        >
                                            {variantStyle.label}
                                        </span>
                                    </div>
                                    {entry.reason && (
                                        <div
                                            style={{
                                                fontSize: 9,
                                                color: '#666',
                                                fontStyle: 'italic',
                                                lineHeight: 1.3,
                                                marginTop: 1,
                                            }}
                                        >
                                            {entry.reason}
                                        </div>
                                    )}
                                    {entry.salience !== null && (
                                        <div
                                            style={{
                                                fontSize: 9,
                                                color: '#806600',
                                                fontFamily: 'monospace',
                                                lineHeight: 1.3,
                                            }}
                                        >
                                            salience={entry.salience.toFixed(2)}
                                            {entry.cancelScore !== null
                                                ? ` cancel=${entry.cancelScore.toFixed(2)}`
                                                : ''}
                                        </div>
                                    )}
                                    {entry.instruction &&
                                        (entry.kind === 'decline' ||
                                            entry.kind === 'skip-obvious') && (
                                            <div
                                                style={{
                                                    fontSize: 9,
                                                    color: '#806600',
                                                    lineHeight: 1.3,
                                                }}
                                            >
                                                → {entry.instruction}
                                            </div>
                                        )}
                                </div>
                            );
                        })
                    )}
                </div>
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
    userPersonaId,
    isGenerating,
}: {
    message: RealtimeChatMessage;
    onDelete: () => void;
    onTruncate: () => void;
    onReprompt: () => void;
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

    const appraisalData = useMemo(() => {
        if (!message.appraisal) return null;
        try {
            const raw = JSON.parse(message.appraisal);
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
    }, [message.appraisal]);

    const isAppraisalStop = appraisalData?.stop === true;
    const isDirectedAtUser =
        !!appraisalData &&
        !!userPersonaId &&
        appraisalData.personaId === userPersonaId;

    const userPersonaName =
        personas.find((p) => p.id === userPersonaId)?.name ?? 'you';

    // Race-cancelled message — shipped as an in-character abandonment line. Render
    // as a plain italic beat (grey, no avatar/bubble, no error chrome) so it reads as
    // character behaviour rather than a system error or a normal speech turn.
    if (message.kind === 'emote') {
        return (
            <div
                className="p-2 group"
                style={{
                    borderBottom: '1px solid #E8E4D8',
                    background: 'transparent',
                    borderLeft: '3px solid transparent',
                }}
            >
                <div
                    style={{
                        fontSize: 11,
                        fontStyle: 'italic',
                        color: '#888',
                        lineHeight: 1.4,
                    }}
                >
                    <span style={{ fontWeight: 600, marginRight: 6, color: '#999' }}>
                        {senderName}
                    </span>
                    {message.content}
                </div>
            </div>
        );
    }

    if (isDirectedAtUser && appraisalData) {
        return (
            <div
                className="group"
                style={{
                    borderBottom: '1px solid #B0C4F0',
                    background:
                        'linear-gradient(135deg, #EDF3FF 0%, #E6EEFF 100%)',
                    borderLeft: '4px solid #316AC5',
                    padding: '8px 10px',
                }}
            >
                <div className="flex items-center justify-between mb-2">
                    <span
                        style={{
                            fontSize: 11,
                            fontWeight: 700,
                            color: '#1a3a8f',
                            display: 'flex',
                            alignItems: 'center',
                            gap: 5,
                        }}
                    >
                        <span style={{ fontSize: 14 }}>🎬</span>
                        Your turn,{' '}
                        <span style={{ color: '#003399' }}>
                            {userPersonaName}
                        </span>
                    </span>
                    <span className="flex items-center gap-1 opacity-0 group-hover:opacity-100 transition-opacity">
                        <button
                            type="button"
                            disabled={busy}
                            onClick={onReprompt}
                            style={{ fontSize: '10px', color: '#666' }}
                        >
                            redo
                        </button>
                        <button
                            type="button"
                            disabled={busy}
                            onClick={onTruncate}
                            style={{ fontSize: '10px', color: '#666' }}
                        >
                            cut
                        </button>
                        <button
                            type="button"
                            disabled={busy}
                            onClick={onDelete}
                            style={{ fontSize: '10px', color: '#666' }}
                        >
                            del
                        </button>
                    </span>
                </div>
                <div
                    style={{
                        fontSize: 12,
                        color: '#1a1a4a',
                        fontWeight: 500,
                        marginBottom: appraisalData.reason ? 4 : 0,
                        lineHeight: 1.4,
                    }}
                >
                    {appraisalData.instruction}
                </div>
                {appraisalData.reason && (
                    <div
                        style={{
                            fontSize: 10,
                            color: '#5560AA',
                            fontStyle: 'italic',
                        }}
                    >
                        {appraisalData.reason}
                    </div>
                )}
            </div>
        );
    }

    return (
        <div
            className="p-2 group"
            style={{
                borderBottom: '1px solid #D4D0C8',
                background: isAppraisalStop
                    ? '#FFFBEA'
                    : isUser
                      ? '#FFFEF5'
                      : '#F8F8F8',
                borderLeft: isGenerating
                    ? '3px solid #316AC5'
                    : isAppraisalStop
                      ? '3px solid #CC8800'
                      : '3px solid transparent',
            }}
        >
            <div className="flex items-start justify-between gap-2 mb-1">
                <div className="flex items-center gap-2">
                    {isAppraisalStop ? (
                        <span
                            title="Appraisal"
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
                            color: isAppraisalStop
                                ? '#806600'
                                : isUser
                                  ? '#003399'
                                  : '#006600',
                        }}
                    >
                        {isAppraisalStop ? 'Appraisal' : senderName}
                    </span>
                    {formatTime(message.sendAt) ? (
                        <span style={{ color: '#ACA899', fontSize: 10 }}>
                            {formatTime(message.sendAt)}
                        </span>
                    ) : null}
                </div>
                <span
                    className="flex items-center gap-1 opacity-0 group-hover:opacity-100 transition-opacity"
                    style={{ flexShrink: 0 }}
                >
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

            {showDetails && hasDetails && (
                <div
                    className="mt-2 p-2"
                    style={{
                        background: '#FFF',
                        border: '1px solid #D4D0C8',
                    }}
                >
                    {(message.metadata?.modelName ||
                        message.metadata?.provider) && (
                        <div className="mb-2">
                            <div
                                className="text-[10px] font-semibold"
                                style={{ color: '#316AC5' }}
                            >
                                Model
                            </div>
                            <div
                                className="text-[11px]"
                                style={{ color: '#000' }}
                            >
                                {message.metadata?.provider
                                    ? `${message.metadata.provider} / ${message.metadata.modelName ?? '?'}`
                                    : (message.metadata?.modelName ?? '')}
                            </div>
                        </div>
                    )}

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
                                                'appraisal',
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
    personas,
}: {
    info: GenerationPhase;
    personas?: Persona[];
}) {
    if (info.phase === 'waiting') {
        return (
            <div
                className="flex items-center gap-2 animate-pulse"
                style={{
                    color: '#808080',
                    padding: '4px 8px',
                }}
            >
                <span>Waiting for responses...</span>
            </div>
        );
    }
    if (info.phase === 'deciding') {
        // deciding phases are shown in the sidebar Thoughts tab, not inline
        return null;
    }
    if (info.phase === 'typing' || info.phase === 'streaming') {
        const personaId = info.personaName; // actually the ID from attend event
        const resolvedName =
            personas?.find((p) => p.id === personaId)?.name ??
            personaId.slice(0, 8);
        return (
            <div
                style={{
                    borderBottom: '1px solid #D4D0C8',
                    background: '#F8F8F8',
                    borderLeft: '3px solid #316AC5',
                    padding: '6px 8px',
                }}
            >
                <div className="flex items-center gap-2 mb-1">
                    <img
                        src={`https://robohash.org/${encodeURIComponent(personaId)}?size=32x32`}
                        alt={resolvedName}
                        style={{
                            width: 22,
                            height: 22,
                            flexShrink: 0,
                            imageRendering: 'pixelated',
                        }}
                    />
                    <span style={{ fontWeight: 600, color: '#006600' }}>
                        {resolvedName}
                    </span>
                    {info.phase === 'streaming' && (
                        <span style={{ color: '#808080', fontSize: 10 }}>
                            ({info.charCount} chars)
                        </span>
                    )}
                </div>
                <div
                    className="flex items-center gap-1 pl-1"
                    style={{ color: '#808080' }}
                >
                    <span
                        className="inline-block animate-bounce"
                        style={{
                            animationDelay: '0ms',
                            fontSize: 16,
                            lineHeight: 1,
                        }}
                    >
                        •
                    </span>
                    <span
                        className="inline-block animate-bounce"
                        style={{
                            animationDelay: '150ms',
                            fontSize: 16,
                            lineHeight: 1,
                        }}
                    >
                        •
                    </span>
                    <span
                        className="inline-block animate-bounce"
                        style={{
                            animationDelay: '300ms',
                            fontSize: 16,
                            lineHeight: 1,
                        }}
                    >
                        •
                    </span>
                </div>
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
            style={{
                display: 'inline-block',
                width: 8,
                height: 8,
                borderRadius: '50%',
                background: color,
                flexShrink: 0,
            }}
        />
    );
}

function formatTime(iso: string | null | undefined): string {
    if (!iso) return '';
    try {
        const d = new Date(iso);
        return d.toLocaleTimeString([], {
            hour: '2-digit',
            minute: '2-digit',
        });
    } catch {
        return '';
    }
}

function ScenarioChip({
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
        <span style={{ position: 'relative' }}>
            <button
                type="button"
                onClick={() => setIsEditing((v) => !v)}
                title={scenario ?? 'Set the in-fiction setting / scenario'}
                style={{
                    fontSize: 10,
                    padding: '1px 6px',
                    color: scenario ? '#000' : '#666',
                    background: scenario ? '#FFF8E1' : '#F0F0F0',
                    border: '1px solid #ACA899',
                    fontStyle: scenario ? 'normal' : 'italic',
                    maxWidth: 240,
                    overflow: 'hidden',
                    whiteSpace: 'nowrap',
                    textOverflow: 'ellipsis',
                }}
            >
                {scenario ? `🎬 ${truncated}` : '+ Add scenario'}
            </button>
            {isEditing && (
                <div
                    style={{
                        position: 'absolute',
                        top: '100%',
                        left: 0,
                        marginTop: 4,
                        zIndex: 10,
                        width: 320,
                        background: '#FFFFFF',
                        border: '1px solid #ACA899',
                        boxShadow: '2px 2px 6px rgba(0,0,0,0.2)',
                        padding: 8,
                    }}
                >
                    <div
                        style={{
                            fontSize: 10,
                            fontWeight: 600,
                            marginBottom: 4,
                            color: '#000',
                        }}
                    >
                        Scenario
                    </div>
                    <textarea
                        rows={4}
                        maxLength={500}
                        className="w-full text-[11px]"
                        style={{
                            padding: '4px',
                            resize: 'vertical',
                            border: '1px solid #ACA899',
                        }}
                        placeholder="In-fiction setting (optional). Leave blank to clear."
                        value={draft}
                        onChange={(e) => setDraft(e.currentTarget.value)}
                    />
                    <div
                        className="flex justify-end gap-1"
                        style={{ marginTop: 6 }}
                    >
                        <button
                            type="button"
                            onClick={handleCancel}
                            disabled={updateScenario.isPending}
                            className="text-[11px]"
                            style={{ padding: '2px 8px' }}
                        >
                            Cancel
                        </button>
                        <button
                            type="button"
                            onClick={handleSave}
                            disabled={updateScenario.isPending}
                            className="text-[11px]"
                            style={{ padding: '2px 8px' }}
                        >
                            {updateScenario.isPending ? '...' : 'Save'}
                        </button>
                    </div>
                    {updateScenario.isError && (
                        <div
                            style={{
                                fontSize: 10,
                                color: '#c00',
                                marginTop: 4,
                            }}
                        >
                            Failed to save scenario.
                        </div>
                    )}
                </div>
            )}
        </span>
    );
}
