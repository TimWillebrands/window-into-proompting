import { useQueryClient } from '@tanstack/react-query';
import { useMemo, useState } from 'react';
import {
    useDeleteImportId,
    useDeleteImportIdCommit,
    useGetImportIdDraftSuspense,
    useGetImportIdLedgerSuspense,
    useGetImportIdRegistrySuspense,
    useGetImportIdSuspense,
} from '#api/party-zone';
import type {
    ImportDraftView,
    ImportLedger,
    ImportRegistryView,
    ImportSessionOverview,
    SceneCommitResult,
} from '../../api/model';
import CardsPanel from './components/CardsPanel';
import ChunkStrip from './components/ChunkStrip';
import CorrectionsPanel from './components/CorrectionsPanel';
import DraftPanel from './components/DraftPanel';
import LedgerPanel from './components/LedgerPanel';
import RegistryPanel from './components/RegistryPanel';
import ScenesPanel from './components/ScenesPanel';
import { useImportRealtime } from './lib/use-import-realtime';
import { invalidateImportSession, num } from './lib/workshop-utils';

type Tab = 'draft' | 'registry' | 'cards' | 'ledger' | 'corrections';

const TABS: Array<{ id: Tab; label: string }> = [
    { id: 'draft', label: 'Draft' },
    { id: 'registry', label: 'Registry' },
    { id: 'cards', label: 'Cards' },
    { id: 'ledger', label: 'Ledger' },
    { id: 'corrections', label: 'Corrections' },
];

export default function WorkshopScreen({
    sessionId,
    onExit,
}: {
    sessionId: string;
    onExit: () => void;
}) {
    const queryClient = useQueryClient();
    const overview = useGetImportIdSuspense(sessionId).data
        .data as ImportSessionOverview;
    const draft = useGetImportIdDraftSuspense(sessionId).data
        .data as ImportDraftView;
    const ledger = useGetImportIdLedgerSuspense(sessionId).data
        .data as ImportLedger;
    const registry = useGetImportIdRegistrySuspense(sessionId).data
        .data as ImportRegistryView;

    const { status: realtimeStatus, runs } = useImportRealtime(sessionId);

    const [selectedSceneId, setSelectedSceneId] = useState<string | null>(null);
    const [tab, setTab] = useState<Tab>('draft');
    const [lastCommit, setLastCommit] = useState<SceneCommitResult | null>(
        null,
    );

    const scenes = overview.scenes ?? [];
    const selectedScene = scenes.find((s) => s.id === selectedSceneId) ?? null;

    const abandonSession = useDeleteImportId({
        mutation: { onSuccess: onExit },
    });
    const rollbackCommits = useDeleteImportIdCommit({
        mutation: {
            onSuccess: () => {
                setLastCommit(null);
                invalidateImportSession(queryClient, sessionId);
            },
        },
    });

    const committedScenes = scenes.filter((s) => s.committed).length;
    const anyRunning = useMemo(
        () => Object.values(runs).some((r) => r.running),
        [runs],
    );

    return (
        <div
            className="app-surface flex h-full flex-col text-[11px]"
            style={{ background: 'transparent' }}
        >
            <div className="xp-glass-panel flex items-center gap-2 px-2 py-1">
                <span className="font-semibold">
                    {overview.fileName ?? 'import'}
                </span>
                <span className="xp-glass-chip">
                    {num(overview.chunkCount)} chunks
                </span>
                <span className="xp-glass-chip">
                    {scenes.length} scenes · {committedScenes} committed
                </span>
                <span
                    className="xp-glass-chip"
                    title={`realtime: ${realtimeStatus}`}
                    style={{
                        color:
                            realtimeStatus === 'connected'
                                ? '#2e8b57'
                                : '#b8860b',
                    }}
                >
                    {realtimeStatus === 'connected'
                        ? anyRunning
                            ? '● run in progress'
                            : '● live'
                        : `○ ${realtimeStatus}`}
                </span>
                <span className="flex-1" />
                {overview.commitTarget ? (
                    <button
                        type="button"
                        className="xp-glass-chip cursor-pointer"
                        title="Delete the committed Room (messages + memory events) and return every scene to draft. Minted personas stay in the library."
                        disabled={rollbackCommits.isPending}
                        onClick={() => {
                            if (
                                window.confirm(
                                    `Roll back all commits? This deletes room "${overview.commitTarget?.roomName}" and its imported memories.`,
                                )
                            )
                                rollbackCommits.mutate({ id: sessionId });
                        }}
                    >
                        ↩ Roll back commits
                    </button>
                ) : null}
                <button
                    type="button"
                    className="xp-glass-chip cursor-pointer"
                    style={{ color: '#a00' }}
                    disabled={abandonSession.isPending}
                    onClick={() => {
                        if (
                            window.confirm(
                                committedScenes > 0
                                    ? 'Abandon this import session? Committed scenes stay in their Room; the uncommitted draft evaporates.'
                                    : 'Abandon this import session? The uncommitted draft evaporates.',
                            )
                        )
                            abandonSession.mutate({ id: sessionId });
                    }}
                >
                    ✕ Abandon
                </button>
            </div>

            {lastCommit ? (
                <CommitResultBanner
                    result={lastCommit}
                    onDismiss={() => setLastCommit(null)}
                    onReviewRegistry={() => setTab('registry')}
                />
            ) : null}

            <div className="flex min-h-0 flex-1">
                <ChunkStrip
                    sessionId={sessionId}
                    chunks={overview.chunks ?? []}
                    scenes={scenes}
                    ledgerChunks={ledger.chunks ?? []}
                    selectedSceneId={selectedSceneId}
                    onSelectScene={setSelectedSceneId}
                />
                <ScenesPanel
                    sessionId={sessionId}
                    overview={overview}
                    draft={draft}
                    registry={registry}
                    runs={runs}
                    selectedSceneId={selectedSceneId}
                    onSelectScene={setSelectedSceneId}
                    onCommitted={(result) => {
                        setLastCommit(result);
                        invalidateImportSession(queryClient, sessionId);
                    }}
                />
                <div className="flex min-w-0 flex-1 flex-col">
                    <div className="xp-glass-panel flex gap-1 px-2 py-1">
                        {TABS.map((t) => (
                            <button
                                key={t.id}
                                type="button"
                                className="xp-glass-chip cursor-pointer"
                                style={
                                    tab === t.id
                                        ? {
                                              background: '#316ac5',
                                              color: '#fff',
                                              fontWeight: 700,
                                          }
                                        : undefined
                                }
                                onClick={() => setTab(t.id)}
                            >
                                {t.label}
                            </button>
                        ))}
                        {selectedScene ? (
                            <span
                                className="xp-glass-chip"
                                title="Panels are filtered to the selected scene"
                            >
                                scene {num(selectedScene.fromChunk)}–
                                {num(selectedScene.toChunk)}
                                <button
                                    type="button"
                                    className="ml-1 cursor-pointer"
                                    onClick={() => setSelectedSceneId(null)}
                                >
                                    ✕
                                </button>
                            </span>
                        ) : null}
                    </div>
                    <div className="min-h-0 flex-1 overflow-y-auto p-2">
                        {tab === 'draft' ? (
                            <DraftPanel
                                sessionId={sessionId}
                                draft={draft}
                                settings={overview.settings}
                                selectedSceneId={selectedSceneId}
                            />
                        ) : null}
                        {tab === 'registry' ? (
                            <RegistryPanel
                                sessionId={sessionId}
                                registry={registry}
                                settings={overview.settings}
                            />
                        ) : null}
                        {tab === 'cards' ? (
                            <CardsPanel sessionId={sessionId} draft={draft} />
                        ) : null}
                        {tab === 'ledger' ? (
                            <LedgerPanel
                                ledger={ledger}
                                selectedSceneId={selectedSceneId}
                            />
                        ) : null}
                        {tab === 'corrections' ? (
                            <CorrectionsPanel sessionId={sessionId} />
                        ) : null}
                    </div>
                </div>
            </div>
        </div>
    );
}

/// Post-commit digest. `unmatchedParticipants` is the registry-confirmation prompt:
/// unconfirmed concept proposals never claim participants, so the human closes the loop.
function CommitResultBanner({
    result,
    onDismiss,
    onReviewRegistry,
}: {
    result: SceneCommitResult;
    onDismiss: () => void;
    onReviewRegistry: () => void;
}) {
    const unmatched = result.unmatchedParticipants ?? [];
    const skipped = result.personaUpdatesSkipped ?? [];
    return (
        <div
            className="xp-glass-panel flex flex-wrap items-center gap-2 px-2 py-1"
            style={{ borderLeft: '3px solid #2e8b57' }}
        >
            <span className="font-semibold" style={{ color: '#2e8b57' }}>
                Scene committed
            </span>
            <span>
                {num(result.messagesWritten)} messages ·{' '}
                {num(result.eventsWritten)} events ·{' '}
                {num(result.recollectionsWritten)} recollections ·{' '}
                {num(result.correctionsRecorded)} corrections
            </span>
            {result.personasMinted?.length ? (
                <span className="xp-glass-chip">
                    minted: {result.personasMinted.join(', ')}
                </span>
            ) : null}
            {result.personasUpdated?.length ? (
                <span className="xp-glass-chip">
                    updated: {result.personasUpdated.join(', ')}
                </span>
            ) : null}
            {skipped.length ? (
                <span
                    className="xp-glass-chip"
                    style={{ color: '#b8860b' }}
                    title="These personas were edited in the library since the last commit; import will not clobber them. Re-align the card and commit again."
                >
                    ⚠ drift-skipped: {skipped.join(', ')}
                </span>
            ) : null}
            {unmatched.length ? (
                <button
                    type="button"
                    className="xp-glass-chip cursor-pointer"
                    style={{ color: '#b8860b' }}
                    title="Participants without a confirmed registry entry — confirm them as cast or concepts in the registry"
                    onClick={onReviewRegistry}
                >
                    ⚠ {unmatched.length} unmatched participants — review
                    registry
                </button>
            ) : null}
            <span className="flex-1" />
            <button
                type="button"
                className="cursor-pointer"
                onClick={onDismiss}
            >
                ✕
            </button>
        </div>
    );
}
