import { useQueryClient } from '@tanstack/react-query';
import { useMemo, useState } from 'react';
import { usePatchImportIdDraftItemId } from '#api/party-zone';
import type {
    ImportDraftItem,
    ImportDraftView,
    ImportSettings,
} from '../../../api/model';
import { invalidateImportSession, num } from '../lib/workshop-utils';

const TYPE_BADGES: Record<string, { label: string; color: string }> = {
    trait: { label: 'trait', color: '#8e5bb5' },
    episode: { label: 'episode', color: '#4a7ebb' },
    rule: { label: 'rule', color: '#9aa0a6' },
};

/// Draft review: every item the fold produced, with weights, routing and the
/// weight-floor verdict. Sub-floor rows are greyed with their reason and flippable —
/// marks are draft state, not deletions.
export default function DraftPanel({
    sessionId,
    draft,
    settings,
    selectedSceneId,
}: {
    sessionId: string;
    draft: ImportDraftView;
    settings: ImportSettings | undefined;
    selectedSceneId: string | null;
}) {
    const items = useMemo(() => {
        const all = draft.items ?? [];
        const filtered = selectedSceneId
            ? all.filter((i) => i.sceneId === selectedSceneId)
            : all;
        return [...filtered].sort(
            (a, b) =>
                num(a.sourceChunks?.[0], Number.MAX_SAFE_INTEGER) -
                num(b.sourceChunks?.[0], Number.MAX_SAFE_INTEGER),
        );
    }, [draft.items, selectedSceneId]);

    if (items.length === 0) {
        return (
            <p style={{ color: '#555' }}>
                {selectedSceneId
                    ? 'No draft items for this scene yet — run it.'
                    : 'No draft items yet. Create a scene and run it.'}
            </p>
        );
    }

    return (
        <div className="flex flex-col gap-1">
            <p style={{ color: '#555' }}>
                Weight floor {num(settings?.weightFloor, 0.5).toFixed(2)}:
                episodes below it route to Room history only (greyed) — flip any
                row the extractor got wrong.
            </p>
            {items.map((item) => (
                <DraftItemRow key={item.id} sessionId={sessionId} item={item} />
            ))}
        </div>
    );
}

function DraftItemRow({
    sessionId,
    item,
}: {
    sessionId: string;
    item: ImportDraftItem;
}) {
    const queryClient = useQueryClient();
    const [editing, setEditing] = useState(false);
    const [summaryDraft, setSummaryDraft] = useState(item.summary ?? '');
    const [weightDraft, setWeightDraft] = useState<string>(
        item.weight != null ? String(num(item.weight)) : '',
    );

    const editItem = usePatchImportIdDraftItemId({
        mutation: {
            onSuccess: () => {
                setEditing(false);
                invalidateImportSession(queryClient, sessionId);
            },
        },
    });

    const badge = TYPE_BADGES[item.type ?? ''] ?? {
        label: item.type ?? '?',
        color: '#9aa0a6',
    };
    const isEpisode = item.type === 'episode';
    const historyRouted = item.routing === 'history';
    const greyed = isEpisode && historyRouted;
    const patch = (data: {
        routing?: string | null;
        summary?: string | null;
        weight?: number | null;
    }) => {
        if (!item.id) return;
        editItem.mutate({ id: sessionId, itemId: item.id, data });
    };

    return (
        <div
            className="xp-glass-card flex flex-col gap-1 p-2"
            style={{ opacity: greyed ? 0.55 : 1 }}
        >
            <div className="flex items-center gap-1">
                <span
                    className="xp-glass-chip"
                    style={{ color: badge.color, fontWeight: 700 }}
                >
                    {badge.label}
                </span>
                {item.persona ? (
                    <span className="font-semibold">{item.persona}</span>
                ) : null}
                {item.weight != null ? (
                    <span
                        className="xp-glass-chip"
                        title="How much this moment would stick a day later"
                    >
                        w {num(item.weight).toFixed(2)}
                    </span>
                ) : null}
                {isEpisode ? (
                    <span
                        className="xp-glass-chip"
                        title={item.routingReason ?? undefined}
                        style={{
                            color: historyRouted ? '#708090' : '#2e8b57',
                        }}
                    >
                        {historyRouted ? 'history only' : 'memory event'}
                        {item.routingReason ? ` — ${item.routingReason}` : ''}
                    </span>
                ) : null}
                {item.routingOverridden ? (
                    <span
                        className="xp-glass-chip"
                        title="Routing set by you; “auto” returns it to the weight-floor rule"
                    >
                        ✍ overridden
                    </span>
                ) : null}
                <span className="flex-1" />
                <span style={{ color: '#888' }}>
                    #{(item.sourceChunks ?? []).map((c) => num(c)).join(',')}
                </span>
            </div>

            {editing ? (
                <div className="flex flex-col gap-1">
                    <textarea
                        rows={2}
                        value={summaryDraft}
                        onChange={(e) => setSummaryDraft(e.target.value)}
                    />
                    <div className="flex items-center gap-1">
                        {isEpisode ? (
                            <label className="flex items-center gap-1">
                                weight
                                <input
                                    type="number"
                                    min={0}
                                    max={1}
                                    step={0.05}
                                    className="w-16"
                                    value={weightDraft}
                                    onChange={(e) =>
                                        setWeightDraft(e.target.value)
                                    }
                                />
                            </label>
                        ) : null}
                        <button
                            type="button"
                            className="xp-glass-chip cursor-pointer"
                            disabled={editItem.isPending}
                            onClick={() =>
                                patch({
                                    summary: summaryDraft.trim() || null,
                                    weight:
                                        isEpisode && weightDraft !== ''
                                            ? Number(weightDraft)
                                            : null,
                                })
                            }
                        >
                            Save
                        </button>
                        <button
                            type="button"
                            className="xp-glass-chip cursor-pointer"
                            onClick={() => setEditing(false)}
                        >
                            Cancel
                        </button>
                    </div>
                </div>
            ) : (
                <button
                    type="button"
                    className="cursor-pointer text-left"
                    title="Click to edit"
                    onClick={() => {
                        setSummaryDraft(item.summary ?? '');
                        setWeightDraft(
                            item.weight != null ? String(num(item.weight)) : '',
                        );
                        setEditing(true);
                    }}
                >
                    {item.summary}
                </button>
            )}

            {item.suggestedSummary && item.suggestedSummary !== item.summary ? (
                <span style={{ color: '#888' }}>
                    extractor said: “{item.suggestedSummary}”
                </span>
            ) : null}

            {isEpisode && !editing ? (
                <div className="flex gap-1">
                    <button
                        type="button"
                        className="xp-glass-chip cursor-pointer"
                        disabled={editItem.isPending}
                        title={
                            historyRouted
                                ? 'Promote: also seed a memory Event for this episode'
                                : 'Demote: keep in Room history only, no memory Event'
                        }
                        onClick={() =>
                            patch({
                                routing: historyRouted ? 'event' : 'history',
                            })
                        }
                    >
                        {historyRouted
                            ? '↑ promote to event'
                            : '↓ history only'}
                    </button>
                    {item.routingOverridden ? (
                        <button
                            type="button"
                            className="xp-glass-chip cursor-pointer"
                            disabled={editItem.isPending}
                            onClick={() => patch({ routing: 'auto' })}
                        >
                            reset to auto
                        </button>
                    ) : null}
                </div>
            ) : null}
            {editItem.isError ? (
                <span style={{ color: '#c00' }}>
                    {(editItem.error as Error).message}
                </span>
            ) : null}
        </div>
    );
}
