import { useQueryClient } from '@tanstack/react-query';
import { useCallback, useState } from 'react';
import {
    postImportIdScenesSceneIdCommit,
    useDeleteImportIdScenesSceneId,
    usePostImportIdScenesSceneIdFinalize,
    usePostImportIdScenesSceneIdRun,
    usePutImportIdScenesSceneId,
} from '#api/party-zone';
import type {
    ImportDraftView,
    ImportRegistryView,
    ImportScene,
    ImportSessionOverview,
    RegistryCastEntry,
    SceneCommitResult,
} from '../../../api/model';
import type { SceneRunProgress } from '../lib/use-import-realtime';
import { invalidateImportSession, num } from '../lib/workshop-utils';
import CommitDialog from './CommitDialog';

interface CommitReadiness {
    ran: boolean;
    blockers: string[];
}

const nameKey = (value: string | null | undefined) =>
    (value ?? '').trim().toLowerCase();

/// Client-side mirror of the backend commit gates, so 409s become guidance instead of
/// surprises: undecided match proposals block, minted personas need a reviewed card.
function commitReadiness(
    scene: ImportScene,
    draft: ImportDraftView,
    registry: ImportRegistryView,
): CommitReadiness {
    const items = (draft.items ?? []).filter((i) => i.sceneId === scene.id);
    const touched = new Set<string>();
    for (const item of items) {
        if (item.persona) touched.add(nameKey(item.persona));
        for (const participant of item.participants ?? [])
            touched.add(nameKey(participant));
    }

    const castByAlias = new Map<string, RegistryCastEntry>();
    for (const entry of registry.cast ?? []) {
        castByAlias.set(nameKey(entry.name), entry);
        for (const alias of entry.aliases ?? [])
            castByAlias.set(nameKey(alias), entry);
    }

    const blockers: string[] = [];
    const undecided = new Set<string>();
    for (const key of touched) {
        const entry = castByAlias.get(key);
        if (entry?.matchState === 'proposed' && entry.name)
            undecided.add(entry.name);
    }
    if (undecided.size > 0)
        blockers.push(
            `undecided match proposals: ${[...undecided].join(', ')} — confirm match or mint in the registry`,
        );

    const cardless = new Set<string>();
    for (const item of items) {
        if (item.type !== 'trait' || !item.persona) continue;
        const entry = castByAlias.get(nameKey(item.persona));
        // Mirrors the backend: a concept claim only counts when confirmed.
        if (entry?.routing === 'concept' && entry.confirmed === true) continue;
        if (entry?.matchState === 'confirmed-match') continue;
        const owner = entry?.name ?? item.persona;
        const hasCard = (draft.cards ?? []).some(
            (c) => nameKey(c.persona) === nameKey(owner),
        );
        if (!hasCard) cardless.add(owner);
    }
    if (cardless.size > 0)
        blockers.push(
            `no card yet for: ${[...cardless].join(', ')} — Finalize writes one, or route noise to a confirmed concept in the Registry`,
        );

    return { ran: num(scene.runCount) > 0, blockers };
}

export default function ScenesPanel({
    sessionId,
    overview,
    draft,
    registry,
    runs,
    selectedSceneId,
    onSelectScene,
    onCommitted,
}: {
    sessionId: string;
    overview: ImportSessionOverview;
    draft: ImportDraftView;
    registry: ImportRegistryView;
    runs: Record<string, SceneRunProgress>;
    selectedSceneId: string | null;
    onSelectScene: (sceneId: string | null) => void;
    onCommitted: (result: SceneCommitResult) => void;
}) {
    const queryClient = useQueryClient();
    const scenes = overview.scenes ?? [];
    const hasCommitTarget = overview.commitTarget != null;

    const [commitDialogFor, setCommitDialogFor] = useState<
        'single' | 'all' | null
    >(null);
    const [pendingCommitScene, setPendingCommitScene] = useState<string | null>(
        null,
    );
    const [commitBusy, setCommitBusy] = useState(false);
    const [commitError, setCommitError] = useState<string | null>(null);

    const runScene = usePostImportIdScenesSceneIdRun({
        mutation: {
            onSuccess: () => invalidateImportSession(queryClient, sessionId),
            onError: () => invalidateImportSession(queryClient, sessionId),
        },
    });
    const finalizeScene = usePostImportIdScenesSceneIdFinalize({
        mutation: {
            onSuccess: () => invalidateImportSession(queryClient, sessionId),
        },
    });
    const updateScene = usePutImportIdScenesSceneId({
        mutation: {
            onSuccess: () => invalidateImportSession(queryClient, sessionId),
        },
    });
    const deleteScene = useDeleteImportIdScenesSceneId({
        mutation: {
            onSuccess: () => {
                onSelectScene(null);
                invalidateImportSession(queryClient, sessionId);
            },
        },
    });

    const commitScenes = useCallback(
        async (
            sceneIds: string[],
            target: { partyId?: string; roomName?: string } | null,
        ) => {
            setCommitBusy(true);
            setCommitError(null);
            try {
                let first = !hasCommitTarget;
                for (const sceneId of sceneIds) {
                    const body = first && target ? target : {};
                    first = false;
                    const result = await postImportIdScenesSceneIdCommit(
                        sessionId,
                        sceneId,
                        body,
                    );
                    onCommitted(result.data as SceneCommitResult);
                }
            } catch (error) {
                setCommitError(
                    error instanceof Error ? error.message : String(error),
                );
                invalidateImportSession(queryClient, sessionId);
            } finally {
                setCommitBusy(false);
            }
        },
        [hasCommitTarget, onCommitted, queryClient, sessionId],
    );

    const readyScenes = scenes.filter((scene) => {
        if (scene.committed || !scene.id) return false;
        const readiness = commitReadiness(scene, draft, registry);
        return readiness.ran && readiness.blockers.length === 0;
    });

    const startCommit = (sceneIds: string[], mode: 'single' | 'all') => {
        if (sceneIds.length === 0) return;
        if (!hasCommitTarget) {
            setPendingCommitScene(mode === 'single' ? sceneIds[0] : null);
            setCommitDialogFor(mode);
            return;
        }
        commitScenes(sceneIds, null);
    };

    return (
        <div
            className="xp-glass-panel flex flex-col"
            style={{
                width: '300px',
                borderRight: '1px solid rgba(255,255,255,0.7)',
            }}
        >
            <div className="flex items-center justify-between px-2 py-1">
                <span className="font-semibold">Scenes</span>
                <button
                    type="button"
                    className="xp-glass-chip cursor-pointer"
                    disabled={commitBusy || readyScenes.length === 0}
                    title="Commit every scene that has been run and has no blockers, one by one"
                    onClick={() =>
                        startCommit(
                            readyScenes.map((s) => s.id ?? ''),
                            'all',
                        )
                    }
                >
                    Commit all reviewed ({readyScenes.length})
                </button>
            </div>
            {overview.commitTarget ? (
                <div className="px-2 pb-1" style={{ color: '#555' }}>
                    → room “{overview.commitTarget.roomName}”
                </div>
            ) : null}
            {commitError ? (
                <div className="px-2 pb-1" style={{ color: '#c00' }}>
                    Commit refused: {commitError}
                </div>
            ) : null}
            <div className="min-h-0 flex-1 overflow-y-auto px-1 pb-1">
                {scenes.length === 0 ? (
                    <p className="p-2" style={{ color: '#555' }}>
                        No scenes yet — select a chunk range on the left.
                    </p>
                ) : null}
                {scenes.map((scene) => (
                    <SceneCard
                        key={scene.id}
                        sessionId={sessionId}
                        scene={scene}
                        readiness={commitReadiness(scene, draft, registry)}
                        run={scene.id ? runs[scene.id] : undefined}
                        selected={scene.id === selectedSceneId}
                        commitBusy={commitBusy}
                        onSelect={() =>
                            onSelectScene(
                                scene.id === selectedSceneId
                                    ? null
                                    : (scene.id ?? null),
                            )
                        }
                        onRun={() =>
                            scene.id &&
                            runScene.mutate({
                                id: sessionId,
                                sceneId: scene.id,
                            })
                        }
                        onFinalize={() =>
                            scene.id &&
                            finalizeScene.mutate({
                                id: sessionId,
                                sceneId: scene.id,
                            })
                        }
                        finalizePending={
                            finalizeScene.isPending &&
                            finalizeScene.variables?.sceneId === scene.id
                        }
                        onCommit={() =>
                            scene.id && startCommit([scene.id], 'single')
                        }
                        onSaveNote={(note) =>
                            scene.id &&
                            updateScene.mutate({
                                id: sessionId,
                                sceneId: scene.id,
                                data: {
                                    fromChunk: scene.fromChunk,
                                    toChunk: scene.toChunk,
                                    note: note || null,
                                    includeDossier: scene.includeDossier,
                                },
                            })
                        }
                        onDelete={() => {
                            if (
                                scene.id &&
                                window.confirm(
                                    'Delete this scene and its draft slice?',
                                )
                            )
                                deleteScene.mutate({
                                    id: sessionId,
                                    sceneId: scene.id,
                                });
                        }}
                    />
                ))}
            </div>
            {commitDialogFor ? (
                <CommitDialog
                    busy={commitBusy}
                    onClose={() => setCommitDialogFor(null)}
                    onSubmit={(target) => {
                        const ids =
                            commitDialogFor === 'single' && pendingCommitScene
                                ? [pendingCommitScene]
                                : readyScenes.map((s) => s.id ?? '');
                        setCommitDialogFor(null);
                        commitScenes(ids, target);
                    }}
                />
            ) : null}
        </div>
    );
}

function SceneCard({
    scene,
    readiness,
    run,
    selected,
    commitBusy,
    finalizePending,
    onSelect,
    onRun,
    onFinalize,
    onCommit,
    onSaveNote,
    onDelete,
}: {
    sessionId: string;
    scene: ImportScene;
    readiness: CommitReadiness;
    run: SceneRunProgress | undefined;
    selected: boolean;
    commitBusy: boolean;
    finalizePending: boolean;
    onSelect: () => void;
    onRun: () => void;
    onFinalize: () => void;
    onCommit: () => void;
    onSaveNote: (note: string) => void;
    onDelete: () => void;
}) {
    const [editingNote, setEditingNote] = useState(false);
    const [noteDraft, setNoteDraft] = useState(scene.note ?? '');
    const committed = scene.committed === true;
    const running = run?.running === true;
    const lastPreview = run?.previews[run.previews.length - 1];

    return (
        <div
            className="xp-glass-card mb-1 flex flex-col gap-1 p-2"
            style={{
                border: selected
                    ? '1px solid #316ac5'
                    : '1px solid transparent',
                opacity: committed ? 0.75 : 1,
            }}
        >
            <button
                type="button"
                className="flex w-full cursor-pointer items-center gap-1 text-left"
                onClick={onSelect}
            >
                <span className="font-semibold">
                    {committed ? '🔒 ' : ''}Chunks {num(scene.fromChunk)}–
                    {num(scene.toChunk)}
                </span>
                {scene.includeDossier ? (
                    <span className="xp-glass-chip" title="dossier included">
                        📜
                    </span>
                ) : null}
                <span className="flex-1" />
                <span style={{ color: '#666' }}>
                    {committed
                        ? 'committed'
                        : num(scene.runCount) > 0
                          ? `run ×${num(scene.runCount)}`
                          : 'not run'}
                </span>
            </button>

            {editingNote ? (
                <div className="flex gap-1">
                    <input
                        type="text"
                        className="min-w-0 flex-1"
                        value={noteDraft}
                        onChange={(e) => setNoteDraft(e.target.value)}
                        placeholder="Note for the extractor"
                    />
                    <button
                        type="button"
                        className="xp-glass-chip cursor-pointer"
                        onClick={() => {
                            onSaveNote(noteDraft.trim());
                            setEditingNote(false);
                        }}
                    >
                        Save
                    </button>
                </div>
            ) : (
                <button
                    type="button"
                    className="cursor-pointer text-left"
                    style={{ color: scene.note ? '#333' : '#888' }}
                    disabled={committed}
                    title={
                        committed
                            ? undefined
                            : 'Click to edit the note, then re-run to apply'
                    }
                    onClick={() => {
                        setNoteDraft(scene.note ?? '');
                        setEditingNote(true);
                    }}
                >
                    {scene.note ? `“${scene.note}”` : 'no note'}
                </button>
            )}

            {running ? (
                <div className="flex flex-col gap-[2px]">
                    <div className="flex items-center gap-1">
                        <progress
                            className="min-w-0 flex-1"
                            value={run.callsDone}
                            max={Math.max(run.totalCalls, 1)}
                        />
                        <span>
                            {run.callsDone}/{run.totalCalls || '?'}
                            {run.stage ? ` ${run.stage}` : ''}
                        </span>
                    </div>
                    {lastPreview ? (
                        <span
                            className="truncate"
                            style={{ color: '#555' }}
                            title={lastPreview.summary}
                        >
                            {lastPreview.type === 'trait'
                                ? `${lastPreview.persona}: `
                                : ''}
                            {lastPreview.summary}
                        </span>
                    ) : null}
                </div>
            ) : null}
            {run?.error && !running ? (
                <span style={{ color: '#c00' }}>Run failed: {run.error}</span>
            ) : null}

            {!committed ? (
                <div className="flex flex-wrap gap-1">
                    <button
                        type="button"
                        className="xp-glass-chip cursor-pointer"
                        disabled={running}
                        title={
                            readiness.ran
                                ? 'Re-run replaces this scene’s draft slice; other scenes are untouched'
                                : 'Run the extraction map for this scene'
                        }
                        onClick={onRun}
                    >
                        {running
                            ? 'Running…'
                            : readiness.ran
                              ? '↻ Regenerate'
                              : '▶ Run'}
                    </button>
                    <button
                        type="button"
                        className="xp-glass-chip cursor-pointer"
                        disabled={!readiness.ran || running || finalizePending}
                        title="One LLM call per touched persona: compress traits + write a one-line bio. Cards land in the Cards tab for review."
                        onClick={onFinalize}
                    >
                        {finalizePending ? 'Finalizing…' : '✦ Finalize'}
                    </button>
                    <button
                        type="button"
                        className="xp-glass-chip cursor-pointer font-semibold"
                        disabled={
                            !readiness.ran ||
                            running ||
                            commitBusy ||
                            readiness.blockers.length > 0
                        }
                        title={
                            readiness.blockers.length > 0
                                ? readiness.blockers.join('\n')
                                : 'Deterministic writes only — no LLM'
                        }
                        onClick={onCommit}
                    >
                        ✔ Commit
                    </button>
                    <span className="flex-1" />
                    <button
                        type="button"
                        className="xp-glass-chip cursor-pointer"
                        style={{ color: '#a00' }}
                        disabled={running}
                        onClick={onDelete}
                    >
                        🗑
                    </button>
                </div>
            ) : null}
            {!committed && !running ? (
                !readiness.ran ? (
                    <div style={{ color: '#316ac5' }}>
                        ▸ Next: Run extracts this scene into the draft (LLM)
                    </div>
                ) : readiness.blockers.length > 0 ? (
                    <div style={{ color: '#b8860b' }}>
                        {readiness.blockers.map((blocker) => (
                            <div key={blocker}>⚠ {blocker}</div>
                        ))}
                    </div>
                ) : (
                    <div style={{ color: '#2e8b57' }}>
                        ✔ Ready — Commit writes messages + memories, no LLM
                    </div>
                )
            ) : null}
        </div>
    );
}
