import { create } from 'zustand';
import { colorForCharIndex } from './char-color';

// ── Types ──────────────────────────────────────────────────────────────────

export type MsgId = string;
export type CharId = string;
export type SegmentId = string;

export type SegmentKind =
    | 'dialogue'
    | 'action'
    | 'thought'
    | 'narration'
    | 'ooc';

export type MsgRole = 'user' | 'model' | 'system';

export type ParsedChunk = {
    id: string;
    index: number;
    role: 'user' | 'model';
    text: string;
    tokenCount?: number;
};

export type ParsedFile = {
    fileName: string;
    systemInstruction: string;
    chunks: ParsedChunk[];
};

export type PhaseStatus = 'idle' | 'running' | 'done' | 'stale';
export type MsgStatus = 'pending' | 'running' | 'done' | 'error' | 'stale';

export type ParticipantLink = {
    mentionId: string;
    rawName: string;
    evidence: string;
    roleHint: string | null;
    linkedCharId: CharId | null;
    manualLink: boolean;
};

export type SegmentDraft = {
    id: SegmentId;
    text: string;
    kind: SegmentKind;
    charId: CharId | null;
    manualEdit: boolean;
};

export type MsgState = {
    id: MsgId;
    index: number;
    role: MsgRole;
    text: string;
    included: boolean;
    isReasoning: boolean;
    isHistory: boolean;
    participants: ParticipantLink[];
    segments: SegmentDraft[];
    droppedSegmentIds: SegmentId[];
    identifyStatus: MsgStatus;
    classifyStatus: MsgStatus;
};

export type CharDirty = {
    primaryName?: true;
    archetype?: true;
    prompt?: true;
    bio?: true;
};

export type CharState = {
    id: CharId;
    names: string[];
    primaryName: string;
    archetype: string | null;
    prompt: string;
    bio: string | null;
    color: string;
    reduceStatus: 'pending' | 'running' | 'done' | 'error';
    dirty: CharDirty;
    /** Computed at link-time — true when no living mention links to this char. */
    isOrphan: boolean;
};

export type ImportMetadata = {
    title: string;
    scenario: string;
    baseDateIso: string;
    stepSeconds: number;
};

export type ImportPhase = {
    identify: PhaseStatus;
    classify: PhaseStatus;
    staleReason: string | null;
};

export type ImportUI = {
    hoveredCharId: CharId | null;
    pinnedCharId: CharId | null;
    openCharDetailId: CharId | null;
    openCommitPopover: boolean;
    pickError: string | null;
    runError: string | null;
};

export type RosterSyncCharacter = {
    id: string;
    primary_name: string;
    names: string[];
    archetype: string | null;
};

export type RosterSyncLink = {
    msgId: MsgId;
    mentionId: string;
    charId: string;
};

export type RawMention = {
    mentionId: string;
    name: string;
    evidence: string;
    roleHint: string | null;
};

export type ClassifierSegmentInput = {
    persona_id: string | null;
    new_persona_name: string | null;
    text: string;
    kind: SegmentKind;
};

// ── Persistence shape ──────────────────────────────────────────────────────

export type PersistedMsgPatch = {
    included: boolean;
    participants: ParticipantLink[];
    segments: SegmentDraft[];
    droppedSegmentIds: SegmentId[];
    identifyStatus: MsgStatus;
    classifyStatus: MsgStatus;
};

export type PersistedSnapshot = {
    msgPatches: Record<MsgId, PersistedMsgPatch>;
    roster: { chars: Record<CharId, CharState>; charOrder: CharId[] };
    metadata: ImportMetadata;
    phase: ImportPhase;
};

// ── Store shape ────────────────────────────────────────────────────────────

type StoreState = {
    file: ParsedFile | null;
    fileHash: string | null;
    msgs: Record<MsgId, MsgState>;
    msgOrder: MsgId[];
    roster: { chars: Record<CharId, CharState>; charOrder: CharId[] };
    metadata: ImportMetadata;
    phase: ImportPhase;
    ui: ImportUI;
};

type Actions = {
    loadFile(
        file: ParsedFile,
        hash: string,
        opts: {
            snapshot?: PersistedSnapshot | null;
            defaultIncluded: (c: ParsedChunk) => boolean;
        },
    ): void;
    clearAll(): void;
    setPickError(err: string | null): void;
    setRunError(err: string | null): void;

    // Selection
    toggleMsgIncluded(id: MsgId): void;
    setMsgIncludedBulk(ids: MsgId[], included: boolean): void;

    // Identify-WS handlers
    beginIdentify(includedMsgIds: MsgId[]): void;
    onIdentifyMapProgress(msgId: MsgId, mentions: RawMention[]): void;
    onIdentifyRosterSync(
        chars: RosterSyncCharacter[],
        links: RosterSyncLink[],
    ): void;
    onIdentifyReduceProgress(
        charId: CharId,
        prompt: string,
        bio: string | null,
    ): void;
    onIdentifyCompleted(): void;
    onIdentifyError(err: string): void;

    // Classify-WS handlers
    beginClassify(includedMsgIds: MsgId[]): void;
    onClassifyProgress(msgId: MsgId, segments: ClassifierSegmentInput[]): void;
    onClassifyCompleted(): void;
    onClassifyError(err: string): void;

    // Char edits
    setCharField(
        charId: CharId,
        field: 'primaryName' | 'archetype' | 'prompt' | 'bio',
        value: string,
    ): void;
    revertCharField(
        charId: CharId,
        field: 'primaryName' | 'archetype' | 'prompt' | 'bio',
    ): void;
    deleteChar(charId: CharId): void;
    mergeCharsLocal(survivorId: CharId, victimId: CharId): void;

    // Mention / segment edits
    relinkMention(
        msgId: MsgId,
        mentionId: string,
        newCharId: CharId | null,
    ): void;
    reassignSegment(
        msgId: MsgId,
        segmentId: SegmentId,
        newCharId: CharId,
    ): void;
    dropSegment(msgId: MsgId, segmentId: SegmentId): void;

    // Metadata
    setMetadata(patch: Partial<ImportMetadata>): void;

    // UI
    setHoveredChar(id: CharId | null): void;
    setPinnedChar(id: CharId | null): void;
    openCharDetail(id: CharId | null): void;
    setCommitPopover(open: boolean): void;
};

// ── Helpers ────────────────────────────────────────────────────────────────

function defaultMetadata(): ImportMetadata {
    const iso = new Date().toISOString().slice(0, 16);
    return { title: '', scenario: '', baseDateIso: iso, stepSeconds: 60 };
}

function defaultPhase(): ImportPhase {
    return { identify: 'idle', classify: 'idle', staleReason: null };
}

function defaultUi(): ImportUI {
    return {
        hoveredCharId: null,
        pinnedCharId: null,
        openCharDetailId: null,
        openCommitPopover: false,
        pickError: null,
        runError: null,
    };
}

function buildMsgsFromFile(
    file: ParsedFile,
    defaultIncluded: (c: ParsedChunk) => boolean,
): { msgs: Record<MsgId, MsgState>; msgOrder: MsgId[] } {
    const msgs: Record<MsgId, MsgState> = {};
    const order: MsgId[] = [];
    const sysId = 'sys';
    msgs[sysId] = {
        id: sysId,
        index: -1,
        role: 'system',
        text: file.systemInstruction,
        included: false,
        isReasoning: false,
        isHistory: false,
        participants: [],
        segments: [],
        droppedSegmentIds: [],
        identifyStatus: 'pending',
        classifyStatus: 'pending',
    };
    order.push(sysId);
    for (const c of file.chunks) {
        msgs[c.id] = {
            id: c.id,
            index: c.index,
            role: c.role,
            text: c.text,
            included: defaultIncluded(c),
            isReasoning: false,
            isHistory: false,
            participants: [],
            segments: [],
            droppedSegmentIds: [],
            identifyStatus: 'pending',
            classifyStatus: 'pending',
        };
        order.push(c.id);
    }
    return { msgs, msgOrder: order };
}

function applyPersistedPatches(
    msgs: Record<MsgId, MsgState>,
    snapshot: PersistedSnapshot,
): Record<MsgId, MsgState> {
    const next = { ...msgs };
    for (const [id, patch] of Object.entries(snapshot.msgPatches)) {
        const existing = next[id];
        if (!existing) continue;
        next[id] = {
            ...existing,
            included: patch.included,
            participants: patch.participants,
            segments: patch.segments,
            droppedSegmentIds: patch.droppedSegmentIds,
            identifyStatus: patch.identifyStatus,
            classifyStatus: patch.classifyStatus,
        };
    }
    return next;
}

function recomputeOrphanFlags(
    chars: Record<CharId, CharState>,
    msgs: Record<MsgId, MsgState>,
): Record<CharId, CharState> {
    const livingChars = new Set<CharId>();
    for (const m of Object.values(msgs)) {
        if (!m.included) continue;
        for (const p of m.participants) {
            if (p.linkedCharId) livingChars.add(p.linkedCharId);
        }
    }
    const next: Record<CharId, CharState> = {};
    for (const [id, c] of Object.entries(chars)) {
        const isOrphan = !livingChars.has(id);
        next[id] = c.isOrphan === isOrphan ? c : { ...c, isOrphan };
    }
    return next;
}

function deriveOverallPhase(
    msgs: Record<MsgId, MsgState>,
    isRunning: boolean,
    pick: 'identify' | 'classify',
): { status: PhaseStatus; reason: string | null } {
    if (isRunning) return { status: 'running', reason: null };
    let needs = 0;
    let any = false;
    for (const m of Object.values(msgs)) {
        if (!m.included) continue;
        const s = pick === 'identify' ? m.identifyStatus : m.classifyStatus;
        if (s === 'done') any = true;
        else if (s === 'pending' || s === 'stale') needs++;
    }
    if (needs > 0 && any) {
        return {
            status: 'stale',
            reason: `${needs} message${needs === 1 ? '' : 's'} need ${pick === 'identify' ? 'identification' : 'classification'}`,
        };
    }
    if (needs > 0) {
        return { status: 'idle', reason: null };
    }
    if (any) return { status: 'done', reason: null };
    return { status: 'idle', reason: null };
}

function nextCharColor(charOrder: CharId[]): string {
    return colorForCharIndex(charOrder.length);
}

// ── Store ──────────────────────────────────────────────────────────────────

const initialState: StoreState = {
    file: null,
    fileHash: null,
    msgs: {},
    msgOrder: [],
    roster: { chars: {}, charOrder: [] },
    metadata: defaultMetadata(),
    phase: defaultPhase(),
    ui: defaultUi(),
};

export const useImportStore = create<StoreState & Actions>((set) => ({
    ...initialState,

    loadFile(file, hash, { snapshot, defaultIncluded }) {
        const built = buildMsgsFromFile(file, defaultIncluded);
        const msgs = snapshot
            ? applyPersistedPatches(built.msgs, snapshot)
            : built.msgs;

        const roster = snapshot?.roster ?? { chars: {}, charOrder: [] };
        const metadata = snapshot?.metadata ?? {
            ...defaultMetadata(),
            title: file.fileName.replace(/\.json$/i, '').slice(0, 120),
        };
        const phase = snapshot?.phase ?? defaultPhase();

        set({
            file,
            fileHash: hash,
            msgs: recomputeOrphanFlags(roster.chars, msgs) ? msgs : msgs,
            msgOrder: built.msgOrder,
            roster: {
                chars: recomputeOrphanFlags(roster.chars, msgs),
                charOrder: roster.charOrder,
            },
            metadata,
            phase,
            ui: defaultUi(),
        });
    },

    clearAll() {
        set({ ...initialState, metadata: defaultMetadata(), ui: defaultUi() });
    },

    setPickError(err) {
        set((s) => ({ ui: { ...s.ui, pickError: err } }));
    },
    setRunError(err) {
        set((s) => ({ ui: { ...s.ui, runError: err } }));
    },

    toggleMsgIncluded(id) {
        set((s) => {
            const m = s.msgs[id];
            if (!m) return s;
            const willInclude = !m.included;
            const next: MsgState = {
                ...m,
                included: willInclude,
                identifyStatus:
                    willInclude && m.identifyStatus === 'pending'
                        ? 'pending'
                        : willInclude
                          ? m.identifyStatus
                          : m.identifyStatus,
            };
            const msgs = { ...s.msgs, [id]: next };
            const roster = {
                ...s.roster,
                chars: recomputeOrphanFlags(s.roster.chars, msgs),
            };
            const idPhase = deriveOverallPhase(msgs, false, 'identify');
            const cPhase = deriveOverallPhase(msgs, false, 'classify');
            return {
                msgs,
                roster,
                phase: {
                    identify: idPhase.status,
                    classify: cPhase.status,
                    staleReason: idPhase.reason ?? cPhase.reason,
                },
            };
        });
    },

    setMsgIncludedBulk(ids, included) {
        set((s) => {
            const msgs = { ...s.msgs };
            for (const id of ids) {
                const m = msgs[id];
                if (!m) continue;
                msgs[id] = { ...m, included };
            }
            const roster = {
                ...s.roster,
                chars: recomputeOrphanFlags(s.roster.chars, msgs),
            };
            const idPhase = deriveOverallPhase(msgs, false, 'identify');
            const cPhase = deriveOverallPhase(msgs, false, 'classify');
            return {
                msgs,
                roster,
                phase: {
                    identify: idPhase.status,
                    classify: cPhase.status,
                    staleReason: idPhase.reason ?? cPhase.reason,
                },
            };
        });
    },

    beginIdentify(includedMsgIds) {
        set((s) => {
            const msgs = { ...s.msgs };
            for (const id of includedMsgIds) {
                const m = msgs[id];
                if (!m) continue;
                msgs[id] = { ...m, identifyStatus: 'running' };
            }
            return {
                msgs,
                phase: { ...s.phase, identify: 'running', staleReason: null },
                ui: { ...s.ui, runError: null },
            };
        });
    },

    onIdentifyMapProgress(msgId, mentions) {
        set((s) => {
            const m = s.msgs[msgId];
            if (!m) return s;
            // Preserve any existing manual links the user already set.
            const oldByMentionId = new Map(
                m.participants.map((p) => [p.mentionId, p]),
            );
            const participants: ParticipantLink[] = mentions.map((mention) => {
                const prev = oldByMentionId.get(mention.mentionId);
                if (prev?.manualLink) {
                    return {
                        ...prev,
                        rawName: mention.name,
                        evidence: mention.evidence,
                        roleHint: mention.roleHint,
                    };
                }
                return {
                    mentionId: mention.mentionId,
                    rawName: mention.name,
                    evidence: mention.evidence,
                    roleHint: mention.roleHint,
                    linkedCharId: null,
                    manualLink: false,
                };
            });
            return {
                msgs: {
                    ...s.msgs,
                    [msgId]: {
                        ...m,
                        participants,
                        identifyStatus: 'done',
                    },
                },
            };
        });
    },

    onIdentifyRosterSync(serverChars, serverLinks) {
        set((s) => {
            // Build new char map. For each server char, look up by primaryName in
            // existing roster (case-insensitive); if found, preserve dirty fields,
            // color, prompt/bio. Otherwise mint a new char with a fresh color.
            const existingByName = new Map<string, CharState>();
            for (const c of Object.values(s.roster.chars)) {
                existingByName.set(c.primaryName.toLowerCase(), c);
            }

            const charOrder: CharId[] = [];
            const chars: Record<CharId, CharState> = {};
            for (const sc of serverChars) {
                const lookupKey = sc.primary_name.toLowerCase();
                const prev = existingByName.get(lookupKey);
                const newId = sc.id;
                const fresh: CharState = prev
                    ? {
                          ...prev,
                          id: newId,
                          names: Array.from(
                              new Set([
                                  ...(prev.dirty.primaryName
                                      ? [prev.primaryName]
                                      : []),
                                  ...sc.names,
                              ]),
                          ),
                          primaryName: prev.dirty.primaryName
                              ? prev.primaryName
                              : sc.primary_name,
                          archetype: prev.dirty.archetype
                              ? prev.archetype
                              : sc.archetype,
                          // prompt/bio kept; reduce will overwrite if not dirty
                          reduceStatus: 'pending',
                      }
                    : {
                          id: newId,
                          names:
                              sc.names.length > 0
                                  ? sc.names
                                  : [sc.primary_name],
                          primaryName: sc.primary_name,
                          archetype: sc.archetype,
                          prompt: '',
                          bio: null,
                          color: nextCharColor([
                              ...s.roster.charOrder,
                              ...charOrder,
                          ]),
                          reduceStatus: 'pending',
                          dirty: {},
                          isOrphan: false,
                      };
                chars[newId] = fresh;
                charOrder.push(newId);
            }

            // Apply server links — but skip mentions already manually linked.
            const linksByMention = new Map<string, string>();
            for (const lk of serverLinks) {
                linksByMention.set(`${lk.msgId}:${lk.mentionId}`, lk.charId);
            }
            const msgs = { ...s.msgs };
            for (const m of Object.values(s.msgs)) {
                if (m.participants.length === 0) continue;
                let mutated = false;
                const nextP = m.participants.map((p) => {
                    if (
                        p.manualLink &&
                        p.linkedCharId &&
                        chars[p.linkedCharId]
                    ) {
                        return p;
                    }
                    const newCharId =
                        linksByMention.get(`${m.id}:${p.mentionId}`) ?? null;
                    if (p.linkedCharId !== newCharId) {
                        mutated = true;
                        return {
                            ...p,
                            linkedCharId: newCharId,
                            manualLink: false,
                        };
                    }
                    return p;
                });
                if (mutated) {
                    msgs[m.id] = { ...m, participants: nextP };
                }
            }

            const finalChars = recomputeOrphanFlags(chars, msgs);
            return {
                msgs,
                roster: { chars: finalChars, charOrder },
            };
        });
    },

    onIdentifyReduceProgress(charId, prompt, bio) {
        set((s) => {
            const c = s.roster.chars[charId];
            if (!c) return s;
            const next: CharState = {
                ...c,
                prompt: c.dirty.prompt ? c.prompt : prompt,
                bio: c.dirty.bio ? c.bio : bio,
                reduceStatus: 'done',
            };
            return {
                roster: {
                    ...s.roster,
                    chars: { ...s.roster.chars, [charId]: next },
                },
            };
        });
    },

    onIdentifyCompleted() {
        set((s) => {
            const idPhase = deriveOverallPhase(s.msgs, false, 'identify');
            return {
                phase: {
                    ...s.phase,
                    identify: idPhase.status,
                    staleReason: idPhase.reason,
                },
            };
        });
    },

    onIdentifyError(err) {
        set((s) => ({
            ui: { ...s.ui, runError: err },
            phase: { ...s.phase, identify: 'idle' },
        }));
    },

    beginClassify(includedMsgIds) {
        set((s) => {
            const msgs = { ...s.msgs };
            for (const id of includedMsgIds) {
                const m = msgs[id];
                if (!m) continue;
                msgs[id] = { ...m, classifyStatus: 'running' };
            }
            return {
                msgs,
                phase: { ...s.phase, classify: 'running', staleReason: null },
                ui: { ...s.ui, runError: null },
            };
        });
    },

    onClassifyProgress(msgId, segments) {
        set((s) => {
            const m = s.msgs[msgId];
            if (!m) return s;
            // Resolve persona references via roster char map. Classifier emits
            // either persona_id (an existing char) or new_persona_name (auto-create
            // not implemented in v1 — fall back to first orphan or null).
            const charsByName = new Map<string, CharId>();
            for (const c of Object.values(s.roster.chars)) {
                charsByName.set(c.primaryName.toLowerCase(), c.id);
                for (const n of c.names) charsByName.set(n.toLowerCase(), c.id);
            }
            const segs: SegmentDraft[] = segments.map((seg, i) => {
                let charId: CharId | null = null;
                if (seg.persona_id && s.roster.chars[seg.persona_id]) {
                    charId = seg.persona_id;
                } else if (seg.new_persona_name) {
                    charId =
                        charsByName.get(seg.new_persona_name.toLowerCase()) ??
                        null;
                }
                return {
                    id: `${msgId}-${i}`,
                    text: seg.text,
                    kind: seg.kind,
                    charId,
                    manualEdit: false,
                };
            });
            return {
                msgs: {
                    ...s.msgs,
                    [msgId]: {
                        ...m,
                        segments: segs,
                        droppedSegmentIds: [],
                        classifyStatus: 'done',
                    },
                },
            };
        });
    },

    onClassifyCompleted() {
        set((s) => {
            const cPhase = deriveOverallPhase(s.msgs, false, 'classify');
            return {
                phase: {
                    ...s.phase,
                    classify: cPhase.status,
                    staleReason: cPhase.reason,
                },
            };
        });
    },

    onClassifyError(err) {
        set((s) => ({
            ui: { ...s.ui, runError: err },
            phase: { ...s.phase, classify: 'idle' },
        }));
    },

    setCharField(charId, field, value) {
        set((s) => {
            const c = s.roster.chars[charId];
            if (!c) return s;
            const patch: Partial<CharState> = {};
            if (field === 'primaryName') patch.primaryName = value;
            else if (field === 'archetype') patch.archetype = value || null;
            else if (field === 'prompt') patch.prompt = value;
            else if (field === 'bio') patch.bio = value || null;
            const next: CharState = {
                ...c,
                ...patch,
                dirty: { ...c.dirty, [field]: true },
            };
            return {
                roster: {
                    ...s.roster,
                    chars: { ...s.roster.chars, [charId]: next },
                },
            };
        });
    },

    revertCharField(charId, field) {
        set((s) => {
            const c = s.roster.chars[charId];
            if (!c) return s;
            const dirty = { ...c.dirty };
            delete dirty[field];
            const next: CharState = { ...c, dirty };
            return {
                roster: {
                    ...s.roster,
                    chars: { ...s.roster.chars, [charId]: next },
                },
            };
        });
    },

    deleteChar(charId) {
        set((s) => {
            if (!s.roster.chars[charId]) return s;
            const chars = { ...s.roster.chars };
            delete chars[charId];
            const charOrder = s.roster.charOrder.filter((id) => id !== charId);
            // Unlink any participants/segments pointing at the deleted char.
            const msgs: Record<MsgId, MsgState> = {};
            for (const m of Object.values(s.msgs)) {
                let p = m.participants;
                let seg = m.segments;
                let mutated = false;
                if (p.some((x) => x.linkedCharId === charId)) {
                    mutated = true;
                    p = p.map((x) =>
                        x.linkedCharId === charId
                            ? { ...x, linkedCharId: null, manualLink: false }
                            : x,
                    );
                }
                if (seg.some((x) => x.charId === charId)) {
                    mutated = true;
                    seg = seg.map((x) =>
                        x.charId === charId ? { ...x, charId: null } : x,
                    );
                }
                msgs[m.id] = mutated
                    ? { ...m, participants: p, segments: seg }
                    : m;
            }
            return {
                roster: {
                    chars: recomputeOrphanFlags(chars, msgs),
                    charOrder,
                },
                msgs,
                ui:
                    s.ui.openCharDetailId === charId
                        ? { ...s.ui, openCharDetailId: null }
                        : s.ui,
            };
        });
    },

    mergeCharsLocal(survivorId, victimId) {
        set((s) => {
            const survivor = s.roster.chars[survivorId];
            const victim = s.roster.chars[victimId];
            if (!survivor || !victim || survivorId === victimId) return s;
            const merged: CharState = {
                ...survivor,
                names: Array.from(
                    new Set([
                        ...survivor.names,
                        ...victim.names,
                        victim.primaryName,
                    ]),
                ),
            };
            const chars = { ...s.roster.chars, [survivorId]: merged };
            delete chars[victimId];
            const charOrder = s.roster.charOrder.filter(
                (id) => id !== victimId,
            );

            const msgs: Record<MsgId, MsgState> = {};
            for (const m of Object.values(s.msgs)) {
                let mutated = false;
                let p = m.participants;
                let seg = m.segments;
                if (p.some((x) => x.linkedCharId === victimId)) {
                    mutated = true;
                    p = p.map((x) =>
                        x.linkedCharId === victimId
                            ? { ...x, linkedCharId: survivorId }
                            : x,
                    );
                }
                if (seg.some((x) => x.charId === victimId)) {
                    mutated = true;
                    seg = seg.map((x) =>
                        x.charId === victimId
                            ? { ...x, charId: survivorId }
                            : x,
                    );
                }
                msgs[m.id] = mutated
                    ? { ...m, participants: p, segments: seg }
                    : m;
            }
            return {
                roster: { chars: recomputeOrphanFlags(chars, msgs), charOrder },
                msgs,
            };
        });
    },

    relinkMention(msgId, mentionId, newCharId) {
        set((s) => {
            const m = s.msgs[msgId];
            if (!m) return s;
            const idx = m.participants.findIndex(
                (p) => p.mentionId === mentionId,
            );
            if (idx < 0) return s;
            const p = m.participants[idx];
            const nextP = m.participants.slice();
            nextP[idx] = { ...p, linkedCharId: newCharId, manualLink: true };
            const msgs = { ...s.msgs, [msgId]: { ...m, participants: nextP } };
            const chars = recomputeOrphanFlags(s.roster.chars, msgs);
            return { msgs, roster: { ...s.roster, chars } };
        });
    },

    reassignSegment(msgId, segmentId, newCharId) {
        set((s) => {
            const m = s.msgs[msgId];
            if (!m) return s;
            const idx = m.segments.findIndex((g) => g.id === segmentId);
            if (idx < 0) return s;
            const seg = m.segments[idx];
            const next = m.segments.slice();
            next[idx] = { ...seg, charId: newCharId, manualEdit: true };
            return {
                msgs: { ...s.msgs, [msgId]: { ...m, segments: next } },
            };
        });
    },

    dropSegment(msgId, segmentId) {
        set((s) => {
            const m = s.msgs[msgId];
            if (!m) return s;
            const segments = m.segments.filter((g) => g.id !== segmentId);
            const droppedSegmentIds = m.droppedSegmentIds.includes(segmentId)
                ? m.droppedSegmentIds
                : [...m.droppedSegmentIds, segmentId];
            return {
                msgs: {
                    ...s.msgs,
                    [msgId]: { ...m, segments, droppedSegmentIds },
                },
            };
        });
    },

    setMetadata(patch) {
        set((s) => ({ metadata: { ...s.metadata, ...patch } }));
    },

    setHoveredChar(id) {
        set((s) =>
            s.ui.hoveredCharId === id
                ? s
                : { ui: { ...s.ui, hoveredCharId: id } },
        );
    },

    setPinnedChar(id) {
        set((s) => ({ ui: { ...s.ui, pinnedCharId: id } }));
    },

    openCharDetail(id) {
        set((s) => ({ ui: { ...s.ui, openCharDetailId: id } }));
    },

    setCommitPopover(open) {
        set((s) => ({ ui: { ...s.ui, openCommitPopover: open } }));
    },
}));

// ── Selectors ──────────────────────────────────────────────────────────────

export function selectIncludedMsgIds(s: StoreState): MsgId[] {
    return s.msgOrder.filter((id) => s.msgs[id]?.included);
}

export function selectIncludedCount(s: StoreState): number {
    let n = 0;
    for (const id of s.msgOrder) {
        if (s.msgs[id]?.included) n++;
    }
    return n;
}

// re-export for convenience
export type { StoreState };
