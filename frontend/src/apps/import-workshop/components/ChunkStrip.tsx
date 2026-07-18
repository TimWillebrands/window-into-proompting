import { useQueryClient } from '@tanstack/react-query';
import { useMemo, useState } from 'react';
import { usePostImportIdScenes } from '#api/party-zone';
import type {
    ChunkRouting,
    ChunkSummary,
    ImportScene,
} from '../../../api/model';
import {
    categoryColor,
    dispositionColor,
    invalidateImportSession,
    num,
} from '../lib/workshop-utils';

/// The chunk strip: every IR chunk with category colouring and its conservation
/// disposition. Click two chunks to select a range, then create a Scene over it.
export default function ChunkStrip({
    sessionId,
    chunks,
    scenes,
    ledgerChunks,
    selectedSceneId,
    onSelectScene,
}: {
    sessionId: string;
    chunks: ChunkSummary[];
    scenes: ImportScene[];
    ledgerChunks: ChunkRouting[];
    selectedSceneId: string | null;
    onSelectScene: (sceneId: string | null) => void;
}) {
    const queryClient = useQueryClient();
    const [anchorIndex, setAnchorIndex] = useState<number | null>(null);
    const [range, setRange] = useState<[number, number] | null>(null);
    const [note, setNote] = useState('');
    const [includeDossier, setIncludeDossier] = useState(false);

    const dispositionByChunk = useMemo(() => {
        const map = new Map<number, ChunkRouting>();
        for (const routing of ledgerChunks)
            map.set(num(routing.chunkIndex), routing);
        return map;
    }, [ledgerChunks]);

    const sceneByChunk = useMemo(() => {
        const map = new Map<number, ImportScene>();
        for (const scene of scenes) {
            for (let i = num(scene.fromChunk); i <= num(scene.toChunk); i++) {
                if (!map.has(i)) map.set(i, scene);
            }
        }
        return map;
    }, [scenes]);

    const createScene = usePostImportIdScenes({
        mutation: {
            onSuccess: (result) => {
                setRange(null);
                setAnchorIndex(null);
                setNote('');
                setIncludeDossier(false);
                invalidateImportSession(queryClient, sessionId);
                const created = result.data as ImportScene;
                if (created?.id) onSelectScene(created.id);
            },
        },
    });

    const clickChunk = (index: number) => {
        if (anchorIndex === null) {
            setAnchorIndex(index);
            setRange([index, index]);
            return;
        }
        setRange([Math.min(anchorIndex, index), Math.max(anchorIndex, index)]);
        setAnchorIndex(null);
    };

    return (
        <div
            className="xp-glass-panel flex flex-col"
            style={{
                width: '240px',
                borderRight: '1px solid rgba(255,255,255,0.7)',
            }}
        >
            <div className="flex items-center justify-between px-2 py-1 font-semibold">
                <span>Chunks</span>
                <span className="xp-glass-chip">{chunks.length}</span>
            </div>
            {range ? (
                <div
                    className="xp-glass-card mx-1 mb-1 flex flex-col gap-1 p-2"
                    style={{ border: '1px solid #316ac5' }}
                >
                    <span className="font-semibold">
                        {anchorIndex !== null
                            ? `From chunk ${range[0]} — click the end chunk`
                            : `Scene over chunks ${range[0]}–${range[1]}`}
                    </span>
                    <input
                        type="text"
                        placeholder="Note for the extractor (optional)"
                        value={note}
                        onChange={(e) => setNote(e.target.value)}
                    />
                    <label className="flex items-center gap-1">
                        <input
                            type="checkbox"
                            checked={includeDossier}
                            onChange={(e) =>
                                setIncludeDossier(e.target.checked)
                            }
                        />
                        Include character dossier (system instruction)
                    </label>
                    <div className="flex gap-1">
                        <button
                            type="button"
                            className="xp-glass-chip cursor-pointer font-semibold"
                            disabled={
                                anchorIndex !== null || createScene.isPending
                            }
                            onClick={() =>
                                createScene.mutate({
                                    id: sessionId,
                                    data: {
                                        fromChunk: range[0],
                                        toChunk: range[1],
                                        note: note.trim() || null,
                                        includeDossier,
                                    },
                                })
                            }
                        >
                            {createScene.isPending
                                ? 'Creating…'
                                : '+ Create scene'}
                        </button>
                        <button
                            type="button"
                            className="xp-glass-chip cursor-pointer"
                            onClick={() => {
                                setRange(null);
                                setAnchorIndex(null);
                            }}
                        >
                            Cancel
                        </button>
                    </div>
                    {createScene.isError ? (
                        <span style={{ color: '#c00' }}>
                            {(createScene.error as Error).message}
                        </span>
                    ) : null}
                </div>
            ) : (
                <p className="px-2 pb-1" style={{ color: '#555' }}>
                    Click a start chunk, then an end chunk, to carve a scene.
                </p>
            )}
            <div className="min-h-0 flex-1 overflow-y-auto">
                {chunks.map((chunk) => {
                    const index = num(chunk.index);
                    const scene = sceneByChunk.get(index);
                    const routing = dispositionByChunk.get(index);
                    const inPendingRange =
                        range !== null &&
                        index >= range[0] &&
                        index <= range[1];
                    const inSelectedScene =
                        scene != null && scene.id === selectedSceneId;
                    return (
                        <button
                            key={index}
                            type="button"
                            className="flex w-full cursor-pointer items-center gap-1 px-1 py-[1px] text-left"
                            style={{
                                background: inPendingRange
                                    ? 'rgba(49,106,197,0.25)'
                                    : inSelectedScene
                                      ? 'rgba(49,106,197,0.12)'
                                      : undefined,
                                opacity: chunk.category === 'empty' ? 0.5 : 1,
                            }}
                            title={[
                                `#${index} ${chunk.category} (${num(chunk.chars)} chars)`,
                                routing
                                    ? `${routing.disposition}${routing.reason ? ` — ${routing.reason}` : ''}`
                                    : 'unprocessed',
                                scene
                                    ? `scene ${num(scene.fromChunk)}–${num(scene.toChunk)}${scene.committed ? ' (committed)' : ''}`
                                    : null,
                            ]
                                .filter(Boolean)
                                .join('\n')}
                            onClick={(e) => {
                                if (e.shiftKey && scene?.id) {
                                    onSelectScene(scene.id);
                                    return;
                                }
                                clickChunk(index);
                            }}
                        >
                            <span
                                className="inline-block h-[10px] w-[10px] flex-none rounded-[2px]"
                                style={{
                                    background: categoryColor(chunk.category),
                                }}
                            />
                            <span
                                className="w-7 flex-none text-right"
                                style={{ color: '#666' }}
                            >
                                {index}
                            </span>
                            <span className="min-w-0 flex-1 truncate">
                                {chunk.head || <i>(empty)</i>}
                            </span>
                            {scene?.committed ? (
                                <span title="part of a committed scene">
                                    🔒
                                </span>
                            ) : null}
                            <span
                                className="inline-block h-[7px] w-[7px] flex-none rounded-full"
                                style={{
                                    background: dispositionColor(
                                        routing?.disposition,
                                    ),
                                }}
                            />
                        </button>
                    );
                })}
            </div>
            <div
                className="flex flex-wrap gap-x-2 px-2 py-1"
                style={{ color: '#555' }}
            >
                {Object.entries({
                    message: 'message',
                    recap: 'recap',
                    thought: 'thought',
                    media: 'media',
                }).map(([key, label]) => (
                    <span key={key} className="flex items-center gap-1">
                        <span
                            className="inline-block h-[8px] w-[8px] rounded-[2px]"
                            style={{ background: categoryColor(key) }}
                        />
                        {label}
                    </span>
                ))}
            </div>
        </div>
    );
}
