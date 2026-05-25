import {
    DndContext,
    type DragEndEvent,
    DragOverlay,
    type DragStartEvent,
    KeyboardSensor,
    PointerSensor,
    useSensor,
    useSensors,
} from '@dnd-kit/core';
import { useQueryClient } from '@tanstack/react-query';
import { useCallback, useEffect, useRef, useState } from 'react';
import { getGetPartyIdChatGroupsQueryKey } from '#api/party-zone';
import { useDesktopStore } from '#lib/desktop-context';
import { ROOT_PARTY_ID } from '../../lib/chat-api';
import {
    type CommitMessage,
    type CommitPersonaStub,
    commitImport,
} from '../../lib/import-api';
import type {
    ImportChunkInput,
    ImportIdentifyMsg,
    ImportRosterEntry,
} from '../../lib/import-ws-stream';
import { CharDetailWindow } from './components/CharDetailWindow';
import { CommitPopover } from './components/CommitPopover';
import { MergeConfirmDialog } from './components/MergeConfirmDialog';
import { MessageColumn } from './components/MessageColumn';
import { MessagePartsColumn } from './components/MessagePartsColumn';
import { ParticipantsColumn } from './components/ParticipantsColumn';
import { PhaseHeader } from './components/PhaseHeader';
import { UniqueCharactersColumn } from './components/UniqueCharactersColumn';
import { useClassifyWs } from './hooks/use-classify-ws';
import { useIdentifyWs } from './hooks/use-identify-ws';
import { computeFileHash } from './state/file-hash';
import {
    clearSnapshot,
    loadSnapshot,
    startPersistence,
} from './state/import-persistence';
import {
    type ParsedChunk,
    type ParsedFile,
    selectIncludedCount,
    selectIncludedMsgIds,
    useImportStore,
} from './state/import-store';
import './styles/import.css';

// ── Helpers preserved from the original ImportApp ───────────────────────────

const SKIP_HEADER_RE = /^\s*#+\s*(history|checkpoint)/i;

function isReasoningChunk(chunk: ParsedChunk): boolean {
    if (chunk.role !== 'model') return false;
    const firstLine = chunk.text.trim().split('\n', 1)[0]?.trim();
    if (!firstLine) return false;
    return /^\*\*[^*\n]+\*\*\s*$/.test(firstLine);
}

function parseGeminiExport(rawText: string, fileName: string): ParsedFile {
    const json = JSON.parse(rawText);
    const sys = json?.systemInstruction?.text;
    const chunks = json?.chunkedPrompt?.chunks;
    if (typeof sys !== 'string' || !Array.isArray(chunks)) {
        throw new Error(
            'File does not look like a Gemini AI Studio export (missing systemInstruction.text or chunkedPrompt.chunks).',
        );
    }
    const parsed: ParsedChunk[] = chunks.flatMap((c: unknown, i: number) => {
        if (!c || typeof c !== 'object') return [];
        const obj = c as { role?: string; text?: string; tokenCount?: number };
        if (typeof obj.text !== 'string') return [];
        const role: 'user' | 'model' = obj.role === 'user' ? 'user' : 'model';
        return [
            {
                id: `c${i}`,
                index: i,
                role,
                text: obj.text,
                tokenCount: obj.tokenCount,
            },
        ];
    });
    return { fileName, systemInstruction: sys, chunks: parsed };
}

function isoToUnixMs(iso: string): number {
    const d = new Date(iso);
    const ms = d.getTime();
    return Number.isFinite(ms) ? ms : Date.now();
}

// ── Top-level shell ─────────────────────────────────────────────────────────

export default function ImportApp() {
    const file = useImportStore((s) => s.file);
    if (!file) {
        return <ImportPickGate />;
    }
    return <ImportCanvas />;
}

// ── File pick gate ──────────────────────────────────────────────────────────

function ImportPickGate() {
    const pickError = useImportStore((s) => s.ui.pickError);
    const setPickError = useImportStore((s) => s.setPickError);
    const loadFile = useImportStore((s) => s.loadFile);
    const [dragOver, setDragOver] = useState(false);
    const inputRef = useRef<HTMLInputElement>(null);

    const onPickFile = useCallback(
        async (f: File) => {
            setPickError(null);
            try {
                const text = await f.text();
                const parsed = parseGeminiExport(text, f.name);
                const hash = await computeFileHash(
                    parsed.systemInstruction,
                    parsed.chunks,
                );
                const existing = loadSnapshot(hash);
                const restore =
                    existing &&
                    confirm(
                        `Restore prior draft for "${f.name}"?\n\n` +
                            `${Object.keys(existing.msgPatches).length} messages with state, ` +
                            `${existing.roster.charOrder.length} characters.`,
                    );
                loadFile(parsed, hash, {
                    snapshot: restore ? existing : null,
                    defaultIncluded: (c) =>
                        !SKIP_HEADER_RE.test(c.text) && !isReasoningChunk(c),
                });
            } catch (e) {
                setPickError(
                    e instanceof Error ? e.message : 'Failed to parse file.',
                );
            }
        },
        [loadFile, setPickError],
    );

    return (
        <div className="imp-canvas" style={{ display: 'flex' }}>
            {/* biome-ignore lint/a11y/useKeyWithClickEvents: clickable dropzone */}
            {/* biome-ignore lint/a11y/noStaticElementInteractions: clickable dropzone */}
            <div
                className="imp-pick-zone"
                data-active={dragOver}
                onClick={() => inputRef.current?.click()}
                onDragOver={(e) => {
                    e.preventDefault();
                    setDragOver(true);
                }}
                onDragLeave={() => setDragOver(false)}
                onDrop={(e) => {
                    e.preventDefault();
                    setDragOver(false);
                    const f = e.dataTransfer.files?.[0];
                    if (f) onPickFile(f);
                }}
            >
                <h2>Pick a Gemini AI Studio export</h2>
                <p>
                    Drop a chat .json here, or click to browse. We split the
                    transcript into messages, identify the cast, classify each
                    message into per-character parts, then assemble a chat room.
                </p>
                <button type="button" className="imp-pick-zone-cta">
                    Browse for .json
                </button>
                <input
                    ref={inputRef}
                    type="file"
                    accept=".json,application/json"
                    style={{ display: 'none' }}
                    onChange={(e) => {
                        const f = e.currentTarget.files?.[0];
                        if (f) onPickFile(f);
                    }}
                />
                {pickError && (
                    <div
                        style={{
                            background: 'rgba(240,130,114,0.18)',
                            border: '1px solid rgba(240,130,114,0.5)',
                            padding: '6px 10px',
                            borderRadius: 4,
                            color: 'rgb(255 220 210)',
                            fontSize: 11,
                            maxWidth: 420,
                        }}
                    >
                        {pickError}
                    </div>
                )}
            </div>
        </div>
    );
}

// ── 4-column canvas ─────────────────────────────────────────────────────────

function ImportCanvas() {
    const file = useImportStore((s) => s.file);
    const fileHash = useImportStore((s) => s.fileHash);
    const hoveredChar = useImportStore((s) => s.ui.hoveredCharId);
    const openCharDetailId = useImportStore((s) => s.ui.openCharDetailId);
    const openCommit = useImportStore((s) => s.ui.openCommitPopover);
    const setCommitPopover = useImportStore((s) => s.setCommitPopover);
    const runError = useImportStore((s) => s.ui.runError);
    const setRunError = useImportStore((s) => s.setRunError);
    const clearAll = useImportStore((s) => s.clearAll);
    const phase = useImportStore((s) => s.phase);
    const relinkMention = useImportStore((s) => s.relinkMention);
    const reassignSegment = useImportStore((s) => s.reassignSegment);

    const { runIdentify, cancel: cancelIdentify } = useIdentifyWs();
    const { runClassify, cancel: cancelClassify } = useClassifyWs();
    const queryClient = useQueryClient();
    const openWindow = useDesktopStore((s) => s.openWindow);

    const [committing, setCommitting] = useState(false);
    const [mergeDialog, setMergeDialog] = useState<{
        survivorId: string;
        victimId: string;
    } | null>(null);

    // Wire persistence on mount; tear down when the app unmounts.
    useEffect(() => {
        const stop = startPersistence();
        return stop;
    }, []);

    // Cancel any running stream when the app unmounts.
    useEffect(
        () => () => {
            cancelIdentify();
            cancelClassify();
        },
        [cancelIdentify, cancelClassify],
    );

    const sensors = useSensors(
        useSensor(PointerSensor, { activationConstraint: { distance: 4 } }),
        useSensor(KeyboardSensor),
    );

    const buildIdentifyInputs = useCallback((): ImportIdentifyMsg[] => {
        const ids = selectIncludedMsgIds(useImportStore.getState());
        const msgs = useImportStore.getState().msgs;
        return ids
            .map((id) => {
                const m = msgs[id];
                if (!m) return null;
                return {
                    id: m.id,
                    role: m.role,
                    text: m.text,
                } as ImportIdentifyMsg;
            })
            .filter((x): x is ImportIdentifyMsg => x !== null);
    }, []);

    const buildClassifyInputs = useCallback((): {
        chunks: ImportChunkInput[];
        roster: ImportRosterEntry[];
    } => {
        const state = useImportStore.getState();
        const ids = selectIncludedMsgIds(state);
        const chunks: ImportChunkInput[] = ids
            .map((id) => {
                const m = state.msgs[id];
                if (!m || m.role === 'system') return null;
                return {
                    id: m.id,
                    role: m.role === 'user' ? 'user' : 'model',
                    text: m.text,
                } as ImportChunkInput;
            })
            .filter((x): x is ImportChunkInput => x !== null);
        const roster: ImportRosterEntry[] = state.roster.charOrder.map(
            (charId) => {
                const c = state.roster.chars[charId];
                return {
                    id: c.id,
                    name: c.primaryName,
                    archetype: c.archetype,
                    summary: c.prompt.slice(0, 200),
                };
            },
        );
        return { chunks, roster };
    }, []);

    const onClickIdentify = useCallback(async () => {
        if (!file) return;
        const msgs = buildIdentifyInputs();
        if (msgs.length === 0) {
            setRunError('No messages selected.');
            return;
        }
        await runIdentify(file.systemInstruction, msgs);
    }, [buildIdentifyInputs, file, runIdentify, setRunError]);

    const onClickClassify = useCallback(async () => {
        const { chunks, roster } = buildClassifyInputs();
        if (chunks.length === 0) {
            setRunError('No messages selected.');
            return;
        }
        if (roster.length === 0) {
            setRunError('Identify characters first.');
            return;
        }
        await runClassify(chunks, roster);
    }, [buildClassifyInputs, runClassify, setRunError]);

    const onClickCommit = useCallback(() => {
        setCommitPopover(true);
    }, [setCommitPopover]);

    const onCommit = useCallback(async () => {
        if (!file) return;
        setCommitting(true);
        try {
            const state = useImportStore.getState();
            const baseMs = isoToUnixMs(state.metadata.baseDateIso);
            const stepMs = Math.max(1, state.metadata.stepSeconds) * 1000;
            // Build placeholder ids per char: use the char id (e.g. "char-1").
            const personas: CommitPersonaStub[] = state.roster.charOrder.map(
                (id) => {
                    const c = state.roster.chars[id];
                    return {
                        placeholderId: c.id,
                        name: c.primaryName.trim() || 'Unnamed',
                        systemPrompt: c.prompt,
                        bio: c.bio,
                        isNew: true,
                        preExistingId: null,
                    };
                },
            );
            // Always include a fallback Narrator for orphan segments.
            const narratorId = '_narrator';
            personas.push({
                placeholderId: narratorId,
                name: 'Narrator',
                systemPrompt:
                    'A neutral narrator who describes the scene and environment.',
                bio: null,
                isNew: true,
                preExistingId: null,
            });

            const messages: CommitMessage[] = [];
            let cursor = 0;
            for (const msgId of state.msgOrder) {
                const m = state.msgs[msgId];
                if (!m || !m.included) continue;
                for (const seg of m.segments) {
                    messages.push({
                        senderPlaceholderId: seg.charId ?? narratorId,
                        senderType: m.role === 'user' ? 'user' : 'assistant',
                        content: seg.text,
                        sendAt: baseMs + cursor * stepMs,
                        kind: seg.kind,
                    });
                    cursor++;
                }
            }
            if (messages.length === 0) {
                throw new Error(
                    'No segments to commit. Classify messages first.',
                );
            }

            const res = await commitImport({
                partyId: ROOT_PARTY_ID,
                chatGroupName: state.metadata.title.trim() || 'Imported Chat',
                scenario: state.metadata.scenario.trim() || null,
                personas,
                messages,
                userParticipant: null,
            });
            await queryClient.invalidateQueries({
                queryKey: getGetPartyIdChatGroupsQueryKey(ROOT_PARTY_ID),
            });
            openWindow('open-party', { props: { roomId: res.chatGroupId } });
            setCommitPopover(false);
            // Keep state so the user can re-open the import; clear the draft so the
            // next file pick starts fresh.
            if (fileHash) clearSnapshot(fileHash);
        } catch (e) {
            setRunError(e instanceof Error ? e.message : String(e));
            throw e;
        } finally {
            setCommitting(false);
        }
    }, [
        file,
        fileHash,
        openWindow,
        queryClient,
        setCommitPopover,
        setRunError,
    ]);

    const onDragStart = useCallback((_e: DragStartEvent) => {
        // Could record dragged item kind for the overlay; using id parsing
        // inline keeps DragOverlay decoupled from a separate state field.
    }, []);

    const onDragEnd = useCallback(
        (e: DragEndEvent) => {
            const { active, over } = e;
            if (!over) return;
            const a = active.data.current as
                | { kind: 'chip'; msgId: string; mentionId: string }
                | { kind: 'char'; charId: string }
                | undefined;
            const o = over.data.current as
                | { kind: 'char-drop'; charId: string }
                | { kind: 'segment'; msgId: string; segmentId: string }
                | undefined;
            if (!a || !o) return;
            if (a.kind === 'chip' && o.kind === 'char-drop') {
                relinkMention(a.msgId, a.mentionId, o.charId);
            } else if (a.kind === 'char' && o.kind === 'segment') {
                reassignSegment(o.msgId, o.segmentId, a.charId);
            } else if (
                a.kind === 'char' &&
                o.kind === 'char-drop' &&
                a.charId !== o.charId
            ) {
                // Survivor = drop target; victim = dragged source.
                setMergeDialog({ survivorId: o.charId, victimId: a.charId });
            }
        },
        [reassignSegment, relinkMention],
    );

    const includedCount = useImportStore(selectIncludedCount);
    const charCount = useImportStore((s) => s.roster.charOrder.length);
    const segTotal = useImportStore((s) => {
        let n = 0;
        for (const id of s.msgOrder) n += s.msgs[id]?.segments.length ?? 0;
        return n;
    });
    const canIdentify = includedCount > 0 && phase.identify !== 'running';
    const canClassify =
        includedCount > 0 && phase.classify !== 'running' && charCount > 0;
    const canCommit = charCount > 0 && segTotal > 0 && !committing;

    const onRegenIdentify = useCallback(
        async (msgId: string) => {
            if (!file) return;
            const m = useImportStore.getState().msgs[msgId];
            if (!m) return;
            await runIdentify(file.systemInstruction, [
                { id: m.id, role: m.role, text: m.text },
            ]);
        },
        [file, runIdentify],
    );

    const onRegenClassify = useCallback(
        async (msgId: string) => {
            const s = useImportStore.getState();
            const m = s.msgs[msgId];
            if (!m || m.role === 'system') return;
            const roster: ImportRosterEntry[] = s.roster.charOrder.map((id) => {
                const c = s.roster.chars[id];
                return {
                    id: c.id,
                    name: c.primaryName,
                    archetype: c.archetype,
                    summary: c.prompt.slice(0, 200),
                };
            });
            if (m.segments.length > 0) {
                const manualCount = m.segments.filter(
                    (g) => g.manualEdit,
                ).length;
                if (
                    manualCount > 0 &&
                    !confirm(
                        `Re-classifying this message will overwrite ${manualCount} manual segment edit(s). Continue?`,
                    )
                ) {
                    return;
                }
            }
            await runClassify(
                [
                    {
                        id: m.id,
                        role: m.role === 'user' ? 'user' : 'model',
                        text: m.text,
                    },
                ],
                roster,
            );
        },
        [runClassify],
    );

    return (
        <div
            className="imp-canvas"
            data-hovered-char={hoveredChar ?? undefined}
        >
            <PhaseHeader
                phase={phase}
                onIdentify={onClickIdentify}
                onClassify={onClickClassify}
                onCommit={onClickCommit}
                canIdentify={canIdentify}
                canClassify={canClassify}
                canCommit={canCommit}
                onClearDraft={() => {
                    if (
                        confirm(
                            'Clear the current import and pick a new file? Local draft will stay in browser storage until you start the next import.',
                        )
                    ) {
                        cancelIdentify();
                        cancelClassify();
                        clearAll();
                    }
                }}
            />
            {runError && (
                <div className="imp-error">
                    <span style={{ flex: 1 }}>{runError}</span>
                    <button
                        type="button"
                        className="imp-detail-btn"
                        onClick={() => setRunError(null)}
                    >
                        Dismiss
                    </button>
                </div>
            )}
            <DndContext
                sensors={sensors}
                onDragStart={onDragStart}
                onDragEnd={onDragEnd}
            >
                <MessageColumn
                    onRegenIdentify={onRegenIdentify}
                    onRegenClassify={onRegenClassify}
                />
                <ParticipantsColumn />
                <MessagePartsColumn />
                <UniqueCharactersColumn />
                <DragOverlay
                    className="imp-drag-overlay"
                    dropAnimation={null}
                />
            </DndContext>
            {openCharDetailId && <CharDetailWindow charId={openCharDetailId} />}
            {openCommit && (
                <CommitPopover
                    onCommit={onCommit}
                    onClose={() => setCommitPopover(false)}
                    busy={committing}
                />
            )}
            {mergeDialog && (
                <MergeConfirmDialog
                    survivorId={mergeDialog.survivorId}
                    victimId={mergeDialog.victimId}
                    onClose={() => setMergeDialog(null)}
                />
            )}
        </div>
    );
}
