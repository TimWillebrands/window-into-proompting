import { useQueryClient } from '@tanstack/react-query';
import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { getGetPartyIdChatGroupsQueryKey } from '#api/party-zone';
import { useDesktopStore } from '#lib/desktop-context';
import { ROOT_PARTY_ID } from '../../lib/chat-api';
import {
    type CommitMessage,
    type CommitPersonaStub,
    type ExtractedPersona,
    commitImport,
    extractPersonas,
    mergePersonas,
} from '../../lib/import-api';
import {
    type ImportChunkInput,
    type ImportRosterEntry,
    importClassifyWsStream,
} from '../../lib/import-ws-stream';

// ── Local domain types ──────────────────────────────────────────────────────

type ParsedChunk = {
    id: string; // synthesized — "c{index}"
    index: number;
    role: 'user' | 'model';
    text: string;
    tokenCount?: number;
};

type ParsedFile = {
    fileName: string;
    systemInstruction: string;
    chunks: ParsedChunk[];
};

type PersonaDraft = {
    placeholderId: string;
    name: string;
    archetype: string | null;
    systemPrompt: string;
    bio: string | null;
};

type SegmentKind = 'dialogue' | 'action' | 'thought' | 'narration' | 'ooc';

type ClassifiedSegment = {
    persona_id: string | null;
    new_persona_name: string | null;
    text: string;
    kind: SegmentKind;
};

type SegmentDraft = {
    id: string;
    chunkId: string;
    chunkIndex: number;
    personaPlaceholderId: string;
    text: string;
    kind: SegmentKind;
    senderType: 'user' | 'assistant';
};

type Step =
    | 'pick'
    | 'review-personas'
    | 'classify'
    | 'review-segments'
    | 'committing'
    | 'done'
    | 'error';

// ── Helpers ─────────────────────────────────────────────────────────────────

const SKIP_HEADER_RE = /^\s*#+\s*(history|checkpoint)/i;

/**
 * Heuristic for Gemini Pro/Flash "thinking aloud" preambles. Pattern: the first
 * non-empty line is a bolded standalone heading like `**Observing Social Dynamics**`,
 * authored by `role: model`, and the rest of the chunk is meta-analysis prose. These
 * are typically not part of the in-fiction transcript — the user wants to toggle them
 * out of an import as a group.
 *
 * False positives are tolerable: the user can re-check individual rows.
 */
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
        const role: 'user' | 'model' =
            obj.role === 'user' ? 'user' : 'model';
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

function placeholderForExtractedName(seen: Map<string, string>, name: string): string {
    const norm = name.trim().toLowerCase();
    const existing = seen.get(norm);
    if (existing) return existing;
    const id = `p${seen.size + 1}`;
    seen.set(norm, id);
    return id;
}

function chunkPreview(text: string, max = 240): string {
    const trimmed = text.trim();
    if (trimmed.length <= max) return trimmed;
    return `${trimmed.slice(0, max)}…`;
}

function isoToUnixMs(iso: string): number {
    const d = new Date(iso);
    const ms = d.getTime();
    return Number.isFinite(ms) ? ms : Date.now();
}

// ── Component ───────────────────────────────────────────────────────────────

export default function ImportApp() {
    const [step, setStep] = useState<Step>('pick');
    const [parsed, setParsed] = useState<ParsedFile | null>(null);
    const [selected, setSelected] = useState<Set<string>>(new Set());
    const [extracting, setExtracting] = useState(false);
    const [personas, setPersonas] = useState<PersonaDraft[]>([]);
    const [classifyTotal, setClassifyTotal] = useState(0);
    const [classifyDone, setClassifyDone] = useState(0);
    const [classifyResults, setClassifyResults] = useState<Map<string, ClassifiedSegment[]>>(new Map());
    const [segments, setSegments] = useState<SegmentDraft[]>([]);
    const [chatGroupName, setChatGroupName] = useState('');
    const [scenario, setScenario] = useState('');
    const [baseDateIso, setBaseDateIso] = useState(() => {
        const now = new Date();
        return now.toISOString().slice(0, 16); // datetime-local format
    });
    const [stepSeconds, setStepSeconds] = useState(60);
    const [errorMsg, setErrorMsg] = useState<string | null>(null);

    const abortRef = useRef<AbortController | null>(null);
    const queryClient = useQueryClient();
    const openWindow = useDesktopStore((s) => s.openWindow);

    useEffect(() => () => abortRef.current?.abort(), []);

    // ── Step 1: file picker ────────────────────────────────────────────────
    const onPickFile = useCallback(
        async (file: File) => {
            setErrorMsg(null);
            try {
                const text = await file.text();
                const result = parseGeminiExport(text, file.name);
                setParsed(result);
                setChatGroupName(file.name.replace(/\.json$/i, '').slice(0, 120));
                const autoSelected = new Set(
                    result.chunks
                        .filter(
                            (c) =>
                                !SKIP_HEADER_RE.test(c.text) &&
                                !isReasoningChunk(c),
                        )
                        .map((c) => c.id),
                );
                setSelected(autoSelected);
            } catch (e) {
                setErrorMsg(
                    e instanceof Error ? e.message : 'Failed to parse file.',
                );
            }
        },
        [],
    );

    const onExtractPersonas = useCallback(async () => {
        if (!parsed) return;
        setErrorMsg(null);
        setExtracting(true);
        abortRef.current?.abort();
        const ac = new AbortController();
        abortRef.current = ac;
        try {
            // Pass selected chunks (in chunk order) as extraction context. The backend
            // re-applies its own char budget, but we trim here first so we don't ship
            // a 1MB JSON body for a big export — cap at 32 chunks / 50KB total.
            const sample = parsed.chunks
                .filter((c) => selected.has(c.id) && !isReasoningChunk(c))
                .sort((a, b) => a.index - b.index)
                .slice(0, 32);
            let sampleBudget = 50_000;
            const sampleChunks = sample.flatMap((c) => {
                if (sampleBudget <= 0) return [];
                const take = c.text.length > sampleBudget ? c.text.slice(0, sampleBudget) : c.text;
                sampleBudget -= take.length;
                return [{ role: c.role, text: take }];
            });
            const extracted = await extractPersonas(
                parsed.systemInstruction,
                sampleChunks,
                ac.signal,
            );
            const seen = new Map<string, string>();
            const drafts: PersonaDraft[] = extracted.map((p) => ({
                placeholderId: placeholderForExtractedName(seen, p.name),
                name: p.name,
                archetype: p.archetype,
                systemPrompt: p.system_prompt,
                bio: p.bio,
            }));
            setPersonas(drafts);
            setStep('review-personas');
        } catch (e) {
            if (e instanceof DOMException && e.name === 'AbortError') return;
            setErrorMsg(
                e instanceof Error ? e.message : 'Extraction failed.',
            );
        } finally {
            setExtracting(false);
        }
    }, [parsed, selected]);

    // ── Step 3: classify ───────────────────────────────────────────────────
    const ensureBuiltInPersonas = useCallback((current: PersonaDraft[]): PersonaDraft[] => {
        // Narrator + GM are populated by the classifier for scene-setting / OOC segments.
        // Guarantee they exist so commits don't drop messages referencing them.
        const next = [...current];
        const haveByName = new Map(next.map((p) => [p.name.toLowerCase(), p]));
        const addIfMissing = (name: string, systemPrompt: string) => {
            if (haveByName.has(name.toLowerCase())) return;
            const placeholderId = `p${next.length + 1}`;
            next.push({
                placeholderId,
                name,
                archetype: null,
                systemPrompt,
                bio: null,
            });
            haveByName.set(name.toLowerCase(), next[next.length - 1]);
        };
        addIfMissing('Narrator', 'A neutral narrator who describes the scene and environment.');
        addIfMissing('GM', 'The author/game-master scaffolding voice. Out-of-character setup, scene transitions, stage directions.');
        addIfMissing('Unattributed', 'Fallback for segments the classifier could not attribute.');
        return next;
    }, []);

    const onStartClassify = useCallback(async () => {
        if (!parsed) return;
        const finalPersonas = ensureBuiltInPersonas(personas);
        setPersonas(finalPersonas);
        const roster: ImportRosterEntry[] = finalPersonas.map((p) => ({
            id: p.placeholderId,
            name: p.name,
            archetype: p.archetype,
            summary: p.systemPrompt.slice(0, 200),
        }));
        const chunks: ImportChunkInput[] = parsed.chunks
            .filter((c) => selected.has(c.id))
            .map((c) => ({ id: c.id, role: c.role, text: c.text }));

        if (chunks.length === 0) {
            setErrorMsg('No chunks selected.');
            return;
        }

        // Reset progress state on every retry.
        setClassifyTotal(chunks.length);
        setClassifyDone(0);
        setClassifyResults(new Map());
        setErrorMsg(null);
        setStep('classify');

        abortRef.current?.abort();
        const ac = new AbortController();
        abortRef.current = ac;

        const byNameLower = new Map(finalPersonas.map((p) => [p.name.toLowerCase(), p]));
        const acc = new Map<string, ClassifiedSegment[]>();

        try {
            for await (const envelope of importClassifyWsStream(
                { chunks, roster },
                ac.signal,
            )) {
                if (envelope.type === 'import.classify.progress') {
                    const d = envelope.data as {
                        chunkId: string;
                        completed: number;
                        total: number;
                        segments: ClassifiedSegment[];
                    };
                    acc.set(d.chunkId, d.segments);
                    setClassifyResults(new Map(acc));
                    setClassifyDone(d.completed);
                } else if (envelope.type === 'import.classify.completed') {
                    break;
                } else if (envelope.type === 'import.classify.error') {
                    const err = envelope.data as { error?: string };
                    setErrorMsg(err.error ?? 'Classifier error.');
                    setStep('error');
                    return;
                }
            }

            // Build segment drafts from the accumulated results.
            // Resolve persona references via name (the classifier may emit new_persona_name);
            // unknown names get an auto-created persona stub.
            const personasNow = [...finalPersonas];
            const byNameLowerLocal = new Map(byNameLower);
            const drafts: SegmentDraft[] = [];
            const selectedSorted = parsed.chunks
                .filter((c) => selected.has(c.id))
                .sort((a, b) => a.index - b.index);
            for (const chunk of selectedSorted) {
                const segs = acc.get(chunk.id);
                if (!segs || segs.length === 0) {
                    drafts.push({
                        id: `${chunk.id}-fallback`,
                        chunkId: chunk.id,
                        chunkIndex: chunk.index,
                        personaPlaceholderId:
                            byNameLowerLocal.get('unattributed')?.placeholderId ?? 'p1',
                        text: chunk.text,
                        kind: 'narration',
                        senderType: chunk.role === 'user' ? 'user' : 'assistant',
                    });
                    continue;
                }
                let segIdx = 0;
                for (const seg of segs) {
                    let placeholderId: string | null = null;
                    if (seg.persona_id) {
                        // Classifier returned an id from the roster — verify it exists.
                        const found = personasNow.find((p) => p.placeholderId === seg.persona_id);
                        if (found) placeholderId = found.placeholderId;
                    }
                    if (!placeholderId && seg.new_persona_name) {
                        const nameLower = seg.new_persona_name.toLowerCase();
                        const existing = byNameLowerLocal.get(nameLower);
                        if (existing) {
                            placeholderId = existing.placeholderId;
                        } else {
                            const newId = `p${personasNow.length + 1}`;
                            const stub: PersonaDraft = {
                                placeholderId: newId,
                                name: seg.new_persona_name,
                                archetype: null,
                                systemPrompt: `Auto-added during import. Source chunk ${chunk.index}.`,
                                bio: null,
                            };
                            personasNow.push(stub);
                            byNameLowerLocal.set(nameLower, stub);
                            placeholderId = newId;
                        }
                    }
                    if (!placeholderId) {
                        placeholderId =
                            byNameLowerLocal.get('unattributed')?.placeholderId ?? 'p1';
                    }
                    drafts.push({
                        id: `${chunk.id}-${segIdx++}`,
                        chunkId: chunk.id,
                        chunkIndex: chunk.index,
                        personaPlaceholderId: placeholderId,
                        text: seg.text,
                        kind: seg.kind,
                        senderType: chunk.role === 'user' ? 'user' : 'assistant',
                    });
                }
            }
            setPersonas(personasNow);
            setSegments(drafts);
            setStep('review-segments');
        } catch (e) {
            if (e instanceof DOMException && e.name === 'AbortError') return;
            setErrorMsg(
                e instanceof Error ? e.message : 'Classification stream failed.',
            );
            setStep('error');
        }
    }, [parsed, personas, selected, ensureBuiltInPersonas]);

    // ── Step 5: commit ─────────────────────────────────────────────────────
    const onCommit = useCallback(async () => {
        if (!parsed || segments.length === 0) return;
        setErrorMsg(null);
        setStep('committing');
        const baseMs = isoToUnixMs(baseDateIso);
        const stepMs = Math.max(1, stepSeconds) * 1000;
        const commitPersonas: CommitPersonaStub[] = personas.map((p) => ({
            placeholderId: p.placeholderId,
            name: p.name.trim() || 'Unnamed',
            systemPrompt: p.systemPrompt,
            bio: p.bio,
            isNew: true,
            preExistingId: null,
        }));
        const commitMessages: CommitMessage[] = segments.map((seg, i) => ({
            senderPlaceholderId: seg.personaPlaceholderId,
            senderType: seg.senderType,
            content: seg.text,
            sendAt: baseMs + i * stepMs,
            kind: seg.kind ?? null,
        }));
        try {
            const res = await commitImport({
                partyId: ROOT_PARTY_ID,
                chatGroupName: chatGroupName.trim() || 'Imported Chat',
                scenario: scenario.trim() || null,
                personas: commitPersonas,
                messages: commitMessages,
                userParticipant: null,
            });
            await queryClient.invalidateQueries({
                queryKey: getGetPartyIdChatGroupsQueryKey(ROOT_PARTY_ID),
            });
            setStep('done');
            // Open the new room in D'Accord.
            openWindow('open-party', { props: { roomId: res.chatGroupId } });
        } catch (e) {
            setErrorMsg(e instanceof Error ? e.message : 'Commit failed.');
            setStep('error');
        }
    }, [parsed, segments, personas, baseDateIso, stepSeconds, chatGroupName, scenario, openWindow, queryClient]);

    // ── Render helpers ─────────────────────────────────────────────────────

    const personaByPlaceholder = useMemo(
        () => new Map(personas.map((p) => [p.placeholderId, p])),
        [personas],
    );

    return (
        <div className="flex h-full flex-col gap-2 p-3 text-[12px]">
            <header className="flex items-center justify-between">
                <h2 className="text-[13px] font-bold">
                    Import Chat (Gemini JSON)
                </h2>
                <StepBreadcrumb step={step} />
            </header>

            {errorMsg && (
                <div className="border border-red-400 bg-red-50 p-2 text-red-700">
                    {errorMsg}
                </div>
            )}

            {step === 'pick' && (
                <PickPanel
                    parsed={parsed}
                    selected={selected}
                    onPickFile={onPickFile}
                    onToggleChunk={(id) => {
                        setSelected((prev) => {
                            const next = new Set(prev);
                            if (next.has(id)) next.delete(id);
                            else next.add(id);
                            return next;
                        });
                    }}
                    onSelectAll={() => {
                        if (!parsed) return;
                        setSelected(new Set(parsed.chunks.map((c) => c.id)));
                    }}
                    onClearAll={() => setSelected(new Set())}
                    onSetReasoningSelected={(checked) => {
                        if (!parsed) return;
                        const reasoningIds = new Set(
                            parsed.chunks
                                .filter(isReasoningChunk)
                                .map((c) => c.id),
                        );
                        setSelected((prev) => {
                            const next = new Set(prev);
                            for (const id of reasoningIds) {
                                if (checked) next.add(id);
                                else next.delete(id);
                            }
                            return next;
                        });
                    }}
                    onContinue={onExtractPersonas}
                    extracting={extracting}
                />
            )}

            {step === 'review-personas' && (
                <ReviewPersonasPanel
                    personas={personas}
                    setPersonas={setPersonas}
                    onBack={() => setStep('pick')}
                    onContinue={onStartClassify}
                />
            )}

            {step === 'classify' && (
                <div className="flex flex-1 items-center justify-center">
                    <div className="text-center">
                        <p>Classifying chunks…</p>
                        <p className="mt-2 font-bold tabular-nums">
                            {classifyDone} / {classifyTotal}
                        </p>
                        <p className="mt-1 text-[10px] text-slate-500">
                            (Concurrency capped at 5; partial fallback on any
                            failed chunk.)
                        </p>
                        <button
                            type="button"
                            onClick={() => abortRef.current?.abort()}
                            className="mt-3"
                        >
                            Cancel
                        </button>
                    </div>
                </div>
            )}

            {step === 'review-segments' && (
                <ReviewSegmentsPanel
                    parsed={parsed}
                    segments={segments}
                    setSegments={setSegments}
                    personas={personas}
                    personaByPlaceholder={personaByPlaceholder}
                    chatGroupName={chatGroupName}
                    setChatGroupName={setChatGroupName}
                    scenario={scenario}
                    setScenario={setScenario}
                    baseDateIso={baseDateIso}
                    setBaseDateIso={setBaseDateIso}
                    stepSeconds={stepSeconds}
                    setStepSeconds={setStepSeconds}
                    onBack={() => setStep('review-personas')}
                    onCommit={onCommit}
                />
            )}

            {step === 'committing' && (
                <div className="flex flex-1 items-center justify-center">
                    Committing…
                </div>
            )}

            {step === 'done' && (
                <div className="flex flex-1 flex-col items-center justify-center gap-2 text-center">
                    <p>Done.</p>
                    <p className="text-[11px] text-slate-500">
                        The new room should be open in D'Accord. You can close
                        this window.
                    </p>
                </div>
            )}

            {step === 'error' && (
                <div className="flex flex-1 flex-col items-center justify-center gap-2 text-center">
                    <p>Something went wrong.</p>
                    <button
                        type="button"
                        onClick={() => {
                            setErrorMsg(null);
                            setStep('pick');
                        }}
                    >
                        Start over
                    </button>
                </div>
            )}
        </div>
    );
}

// ── Panel components ────────────────────────────────────────────────────────

function StepBreadcrumb({ step }: { step: Step }) {
    const items: Array<[Step | 'pick', string]> = [
        ['pick', '1 · Pick file'],
        ['review-personas', '2 · Personas'],
        ['classify', '3 · Classify'],
        ['review-segments', '4 · Review'],
        ['committing', '5 · Commit'],
    ];
    const activeIdx = items.findIndex(([s]) => s === step);
    return (
        <ol className="flex gap-1 text-[10px] text-slate-500">
            {items.map(([s, label], i) => (
                <li
                    key={s}
                    className={
                        i === activeIdx ? 'font-bold text-slate-900' : ''
                    }
                >
                    {label}
                    {i < items.length - 1 ? ' ›' : ''}
                </li>
            ))}
        </ol>
    );
}

function PickPanel({
    parsed,
    selected,
    onPickFile,
    onToggleChunk,
    onSelectAll,
    onClearAll,
    onSetReasoningSelected,
    onContinue,
    extracting,
}: {
    parsed: ParsedFile | null;
    selected: Set<string>;
    onPickFile: (file: File) => void;
    onToggleChunk: (id: string) => void;
    onSelectAll: () => void;
    onClearAll: () => void;
    onSetReasoningSelected: (checked: boolean) => void;
    onContinue: () => void;
    extracting: boolean;
}) {
    const reasoningCount = parsed
        ? parsed.chunks.filter(isReasoningChunk).length
        : 0;
    return (
        <div className="flex flex-1 flex-col gap-2 overflow-hidden">
            <div className="flex items-center gap-2">
                <input
                    type="file"
                    accept=".json,application/json"
                    onChange={(e) => {
                        const f = e.currentTarget.files?.[0];
                        if (f) onPickFile(f);
                    }}
                />
                {parsed && (
                    <span className="text-[11px] text-slate-500">
                        {parsed.fileName} · {parsed.chunks.length} chunks ·{' '}
                        {selected.size} selected
                    </span>
                )}
            </div>

            {parsed && (
                <>
                    <div className="flex flex-wrap items-center gap-2 text-[10px]">
                        <button type="button" onClick={onSelectAll}>
                            Select all
                        </button>
                        <button type="button" onClick={onClearAll}>
                            Clear all
                        </button>
                        {reasoningCount > 0 && (
                            <>
                                <span className="text-slate-300">|</span>
                                <button
                                    type="button"
                                    onClick={() => onSetReasoningSelected(false)}
                                    title="Uncheck every chunk detected as model reasoning preamble (bold standalone heading + analysis prose)."
                                >
                                    Deselect reasoning ({reasoningCount})
                                </button>
                                <button
                                    type="button"
                                    onClick={() => onSetReasoningSelected(true)}
                                >
                                    Select reasoning
                                </button>
                            </>
                        )}
                        <span className="text-slate-500">
                            (History / Checkpoint / reasoning chunks auto-deselected.)
                        </span>
                    </div>
                    <ul className="flex-1 overflow-y-auto border border-slate-300 bg-white">
                        {parsed.chunks.map((c) => {
                            const reasoning = isReasoningChunk(c);
                            return (
                                <li
                                    key={c.id}
                                    className="border-b border-slate-200 px-2 py-1"
                                >
                                    <label className="flex cursor-pointer items-start gap-2">
                                        <input
                                            type="checkbox"
                                            checked={selected.has(c.id)}
                                            onChange={() => onToggleChunk(c.id)}
                                        />
                                        <span className="flex-1">
                                            <span className="font-mono text-[10px] text-slate-500">
                                                #{c.index} · {c.role}
                                                {c.tokenCount
                                                    ? ` · ${c.tokenCount}t`
                                                    : ''}
                                                {reasoning && (
                                                    <span className="ml-1 rounded bg-amber-100 px-1 text-amber-800">
                                                        reasoning
                                                    </span>
                                                )}
                                            </span>
                                            <div className="whitespace-pre-wrap text-[11px]">
                                                {chunkPreview(c.text)}
                                            </div>
                                        </span>
                                    </label>
                                </li>
                            );
                        })}
                    </ul>
                    <div className="flex justify-end">
                        <button
                            type="button"
                            onClick={onContinue}
                            disabled={extracting || selected.size === 0}
                        >
                            {extracting
                                ? 'Extracting personas…'
                                : 'Extract personas →'}
                        </button>
                    </div>
                </>
            )}
        </div>
    );
}

function ReviewPersonasPanel({
    personas,
    setPersonas,
    onBack,
    onContinue,
}: {
    personas: PersonaDraft[];
    setPersonas: (next: PersonaDraft[]) => void;
    onBack: () => void;
    onContinue: () => void;
}) {
    const [mergeChecked, setMergeChecked] = useState<Set<string>>(new Set());
    const [merging, setMerging] = useState(false);
    const [mergeError, setMergeError] = useState<string | null>(null);

    const updateField = (
        placeholderId: string,
        patch: Partial<PersonaDraft>,
    ) => {
        setPersonas(
            personas.map((p) =>
                p.placeholderId === placeholderId ? { ...p, ...patch } : p,
            ),
        );
    };
    const removePersona = (placeholderId: string) => {
        setPersonas(personas.filter((p) => p.placeholderId !== placeholderId));
        setMergeChecked((prev) => {
            if (!prev.has(placeholderId)) return prev;
            const next = new Set(prev);
            next.delete(placeholderId);
            return next;
        });
    };
    const addBlank = () => {
        const newId = `p${personas.length + 1}`;
        setPersonas([
            ...personas,
            {
                placeholderId: newId,
                name: '',
                archetype: null,
                systemPrompt: '',
                bio: null,
            },
        ]);
    };
    const toggleMerge = (placeholderId: string) => {
        setMergeChecked((prev) => {
            const next = new Set(prev);
            if (next.has(placeholderId)) next.delete(placeholderId);
            else next.add(placeholderId);
            return next;
        });
    };

    const onMergeSelected = useCallback(async () => {
        const ids = Array.from(mergeChecked);
        if (ids.length < 2) return;
        // Preserve original list order — the LLM merge prompt prefers names from
        // body text but the first selected wins as a tie-breaker, so keep stable.
        const inOrder = personas.filter((p) => mergeChecked.has(p.placeholderId));
        const stubs: ExtractedPersona[] = inOrder.map((p) => ({
            name: p.name,
            archetype: p.archetype,
            system_prompt: p.systemPrompt,
            bio: p.bio,
        }));
        setMergeError(null);
        setMerging(true);
        try {
            const result = await mergePersonas(stubs);
            const survivor = inOrder[0];
            const mergedDraft: PersonaDraft = {
                placeholderId: survivor.placeholderId,
                name: result.name,
                archetype: result.archetype,
                systemPrompt: result.system_prompt,
                bio: result.bio,
            };
            // Replace the survivor with the merged draft and drop the rest in place.
            const idsToDrop = new Set(inOrder.slice(1).map((p) => p.placeholderId));
            setPersonas(
                personas
                    .map((p) =>
                        p.placeholderId === survivor.placeholderId ? mergedDraft : p,
                    )
                    .filter((p) => !idsToDrop.has(p.placeholderId)),
            );
            setMergeChecked(new Set());
        } catch (e) {
            setMergeError(
                e instanceof Error ? e.message : 'Merge failed.',
            );
        } finally {
            setMerging(false);
        }
    }, [mergeChecked, personas, setPersonas]);

    return (
        <div className="flex flex-1 flex-col gap-2 overflow-hidden">
            <p className="text-[11px] text-slate-500">
                Personas extracted from the systemInstruction. Edit names + system
                prompts before classification — the classifier sees these names.
                Tick the checkbox on two or more rows to merge duplicates / name
                variants via the LLM.
            </p>
            {mergeError && (
                <div className="border border-red-400 bg-red-50 p-2 text-red-700">
                    {mergeError}
                </div>
            )}
            <ul className="flex-1 overflow-y-auto border border-slate-300 bg-white">
                {personas.map((p) => (
                    <li
                        key={p.placeholderId}
                        className="flex gap-2 border-b border-slate-200 p-2"
                    >
                        <input
                            type="checkbox"
                            checked={mergeChecked.has(p.placeholderId)}
                            onChange={() => toggleMerge(p.placeholderId)}
                            disabled={merging}
                            title="Select to merge with other ticked rows"
                            className="mt-1"
                        />
                        <img
                            src={`https://robohash.org/${encodeURIComponent(p.placeholderId)}?size=40x40`}
                            alt=""
                            width={40}
                            height={40}
                        />
                        <div className="flex flex-1 flex-col gap-1">
                            <input
                                type="text"
                                value={p.name}
                                onChange={(e) =>
                                    updateField(p.placeholderId, {
                                        name: e.currentTarget.value,
                                    })
                                }
                                placeholder="Name"
                            />
                            <textarea
                                value={p.systemPrompt}
                                onChange={(e) =>
                                    updateField(p.placeholderId, {
                                        systemPrompt: e.currentTarget.value,
                                    })
                                }
                                rows={3}
                                placeholder="System prompt"
                            />
                            <textarea
                                value={p.bio ?? ''}
                                onChange={(e) =>
                                    updateField(p.placeholderId, {
                                        bio: e.currentTarget.value || null,
                                    })
                                }
                                rows={2}
                                placeholder="Bio (optional)"
                            />
                        </div>
                        <button
                            type="button"
                            onClick={() => removePersona(p.placeholderId)}
                        >
                            Remove
                        </button>
                    </li>
                ))}
            </ul>
            <div className="flex justify-between">
                <div className="flex gap-2">
                    <button type="button" onClick={onBack} disabled={merging}>
                        ← Back
                    </button>
                    <button type="button" onClick={addBlank} disabled={merging}>
                        + Add persona
                    </button>
                    {mergeChecked.size >= 2 && (
                        <button
                            type="button"
                            onClick={onMergeSelected}
                            disabled={merging}
                        >
                            {merging
                                ? 'Merging…'
                                : `Merge selected (${mergeChecked.size}) →`}
                        </button>
                    )}
                </div>
                <button
                    type="button"
                    onClick={onContinue}
                    disabled={personas.length === 0 || merging}
                >
                    Classify chunks →
                </button>
            </div>
        </div>
    );
}

function ReviewSegmentsPanel({
    parsed,
    segments,
    setSegments,
    personas,
    personaByPlaceholder,
    chatGroupName,
    setChatGroupName,
    scenario,
    setScenario,
    baseDateIso,
    setBaseDateIso,
    stepSeconds,
    setStepSeconds,
    onBack,
    onCommit,
}: {
    parsed: ParsedFile | null;
    segments: SegmentDraft[];
    setSegments: (next: SegmentDraft[]) => void;
    personas: PersonaDraft[];
    personaByPlaceholder: Map<string, PersonaDraft>;
    chatGroupName: string;
    setChatGroupName: (s: string) => void;
    scenario: string;
    setScenario: (s: string) => void;
    baseDateIso: string;
    setBaseDateIso: (s: string) => void;
    stepSeconds: number;
    setStepSeconds: (n: number) => void;
    onBack: () => void;
    onCommit: () => void;
}) {
    if (!parsed) return null;
    const chunkById = new Map(parsed.chunks.map((c) => [c.id, c]));

    const updateSegment = (id: string, patch: Partial<SegmentDraft>) => {
        setSegments(segments.map((s) => (s.id === id ? { ...s, ...patch } : s)));
    };
    const removeSegment = (id: string) => {
        setSegments(segments.filter((s) => s.id !== id));
    };

    const segmentsByChunk = new Map<string, SegmentDraft[]>();
    for (const s of segments) {
        const list = segmentsByChunk.get(s.chunkId) ?? [];
        list.push(s);
        segmentsByChunk.set(s.chunkId, list);
    }

    return (
        <div className="flex flex-1 flex-col gap-2 overflow-hidden">
            <div className="grid grid-cols-3 gap-2 border-b border-slate-200 pb-2 text-[11px]">
                <label className="flex flex-col gap-1">
                    <span className="font-bold">Room title</span>
                    <input
                        type="text"
                        value={chatGroupName}
                        onChange={(e) =>
                            setChatGroupName(e.currentTarget.value)
                        }
                        maxLength={120}
                    />
                </label>
                <label className="flex flex-col gap-1">
                    <span className="font-bold">Scenario (optional)</span>
                    <input
                        type="text"
                        value={scenario}
                        onChange={(e) => setScenario(e.currentTarget.value)}
                        maxLength={2000}
                    />
                </label>
                <label className="flex flex-col gap-1">
                    <span className="font-bold">Base time · step (s)</span>
                    <div className="flex gap-1">
                        <input
                            type="datetime-local"
                            value={baseDateIso}
                            onChange={(e) =>
                                setBaseDateIso(e.currentTarget.value)
                            }
                        />
                        <input
                            type="number"
                            value={stepSeconds}
                            onChange={(e) =>
                                setStepSeconds(
                                    Number.parseInt(e.currentTarget.value, 10) || 60,
                                )
                            }
                            min={1}
                            style={{ width: '60px' }}
                        />
                    </div>
                </label>
            </div>

            <p className="text-[11px] text-slate-500">
                {segments.length} segments across{' '}
                {segmentsByChunk.size} chunks. Reassign persona / drop segments
                before committing.
            </p>

            <ul className="flex-1 overflow-y-auto border border-slate-300 bg-white">
                {[...segmentsByChunk.entries()]
                    .sort(([, a], [, b]) => a[0].chunkIndex - b[0].chunkIndex)
                    .map(([chunkId, segs]) => {
                        const chunk = chunkById.get(chunkId);
                        return (
                            <li
                                key={chunkId}
                                className="border-b border-slate-200 p-2"
                            >
                                <div className="mb-1 flex items-center gap-2 text-[10px] text-slate-500">
                                    <span>
                                        Chunk #{chunk?.index} · {chunk?.role}
                                    </span>
                                </div>
                                <ul className="flex flex-col gap-1">
                                    {segs.map((seg) => {
                                        const persona = personaByPlaceholder.get(
                                            seg.personaPlaceholderId,
                                        );
                                        return (
                                            <li
                                                key={seg.id}
                                                className="flex gap-2 border border-slate-200 p-1"
                                            >
                                                <div className="flex w-32 shrink-0 flex-col gap-1">
                                                    <select
                                                        value={
                                                            seg.personaPlaceholderId
                                                        }
                                                        onChange={(e) =>
                                                            updateSegment(seg.id, {
                                                                personaPlaceholderId:
                                                                    e.currentTarget.value,
                                                            })
                                                        }
                                                    >
                                                        {personas.map((p) => (
                                                            <option
                                                                key={p.placeholderId}
                                                                value={p.placeholderId}
                                                            >
                                                                {p.name ||
                                                                    p.placeholderId}
                                                            </option>
                                                        ))}
                                                    </select>
                                                    <select
                                                        value={seg.kind}
                                                        onChange={(e) =>
                                                            updateSegment(seg.id, {
                                                                kind: e.currentTarget
                                                                    .value as SegmentKind,
                                                            })
                                                        }
                                                    >
                                                        {(
                                                            [
                                                                'dialogue',
                                                                'action',
                                                                'thought',
                                                                'narration',
                                                                'ooc',
                                                            ] as SegmentKind[]
                                                        ).map((k) => (
                                                            <option key={k} value={k}>
                                                                {k}
                                                            </option>
                                                        ))}
                                                    </select>
                                                    <button
                                                        type="button"
                                                        onClick={() =>
                                                            removeSegment(seg.id)
                                                        }
                                                    >
                                                        Drop
                                                    </button>
                                                </div>
                                                <div className="flex-1 whitespace-pre-wrap text-[11px]">
                                                    {seg.text}
                                                </div>
                                            </li>
                                        );
                                    })}
                                </ul>
                            </li>
                        );
                    })}
            </ul>

            <div className="flex justify-between">
                <button type="button" onClick={onBack}>
                    ← Back
                </button>
                <button
                    type="button"
                    onClick={onCommit}
                    disabled={segments.length === 0}
                >
                    Commit {segments.length} messages →
                </button>
            </div>
        </div>
    );
}
