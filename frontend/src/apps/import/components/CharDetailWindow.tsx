import { useEffect, useMemo, useRef, useState } from 'react';
import {
    type RegenerateCharDetailResponse,
    regenerateCharDetail,
} from '../../../lib/import-api';
import { type CharId, useImportStore } from '../state/import-store';

type Props = { charId: CharId };

/**
 * XP-styled floating panel — visually a window, but bypasses the desktop store
 * (lives inside the Import app's bounds). Draggable via the titlebar.
 */
export function CharDetailWindow({ charId }: Props) {
    const c = useImportStore((s) => s.roster.chars[charId]);
    const setField = useImportStore((s) => s.setCharField);
    const revert = useImportStore((s) => s.revertCharField);
    const deleteChar = useImportStore((s) => s.deleteChar);
    const openDetail = useImportStore((s) => s.openCharDetail);
    const sysInstruction = useImportStore(
        (s) => s.file?.systemInstruction ?? '',
    );
    const onIdentifyReduceProgress = useImportStore(
        (s) => s.onIdentifyReduceProgress,
    );

    const msgs = useImportStore((s) => s.msgs);
    const evidenceQuotes = useMemo(() => {
        if (!c) return [] as { msgId: string; quote: string }[];
        const out: { msgId: string; quote: string }[] = [];
        const accepted = new Set<string>();
        const names = new Set([
            c.primaryName.toLowerCase(),
            ...c.names.map((n) => n.toLowerCase()),
        ]);
        for (const m of Object.values(msgs)) {
            for (const p of m.participants) {
                if (
                    (p.linkedCharId === charId ||
                        names.has(p.rawName.toLowerCase())) &&
                    !accepted.has(p.evidence)
                ) {
                    accepted.add(p.evidence);
                    out.push({ msgId: m.id, quote: p.evidence });
                    if (out.length >= 30) return out;
                }
            }
        }
        return out;
    }, [c, msgs, charId]);

    const [pos, setPos] = useState<{ x: number; y: number }>({ x: 60, y: 70 });
    const dragRef = useRef<{
        startX: number;
        startY: number;
        baseX: number;
        baseY: number;
    } | null>(null);
    const [regenBusy, setRegenBusy] = useState<
        'prompt' | 'bio' | 'both' | null
    >(null);

    useEffect(() => {
        function onMove(e: MouseEvent) {
            if (!dragRef.current) return;
            const dx = e.clientX - dragRef.current.startX;
            const dy = e.clientY - dragRef.current.startY;
            setPos({
                x: Math.max(0, dragRef.current.baseX + dx),
                y: Math.max(0, dragRef.current.baseY + dy),
            });
        }
        function onUp() {
            dragRef.current = null;
        }
        window.addEventListener('mousemove', onMove);
        window.addEventListener('mouseup', onUp);
        return () => {
            window.removeEventListener('mousemove', onMove);
            window.removeEventListener('mouseup', onUp);
        };
    }, []);

    const onRegen = useMemo(() => {
        return async (mode: 'prompt' | 'bio' | 'both') => {
            if (!c) return;
            setRegenBusy(mode);
            try {
                const evidence = evidenceQuotes
                    .map((e) => e.quote)
                    .filter(Boolean)
                    .slice(0, 8);
                const res: RegenerateCharDetailResponse =
                    await regenerateCharDetail({
                        primaryName: c.primaryName,
                        names: c.names,
                        evidence,
                        archetype: c.archetype,
                        systemInstructionText: sysInstruction,
                        mode,
                    });
                if (res.prompt !== null) {
                    onIdentifyReduceProgress(
                        charId,
                        res.prompt,
                        res.bio ?? c.bio,
                    );
                    revert(charId, 'prompt');
                }
                if (res.bio !== null && mode !== 'prompt') {
                    revert(charId, 'bio');
                }
                if (
                    mode === 'both' &&
                    res.bio !== null &&
                    res.prompt !== null
                ) {
                    onIdentifyReduceProgress(charId, res.prompt, res.bio);
                }
            } catch (err) {
                console.error('regenerate-char-detail failed', err);
            } finally {
                setRegenBusy(null);
            }
        };
    }, [
        c,
        charId,
        evidenceQuotes,
        onIdentifyReduceProgress,
        revert,
        sysInstruction,
    ]);

    if (!c) return null;

    return (
        <div
            className="imp-detail-window"
            style={{
                left: pos.x,
                top: pos.y,
                ['--char-color' as string]: c.color,
                borderColor: c.color,
            }}
        >
            {/* biome-ignore lint/a11y/noStaticElementInteractions: titlebar drag handle */}
            <div
                className="imp-detail-titlebar"
                onMouseDown={(e) => {
                    dragRef.current = {
                        startX: e.clientX,
                        startY: e.clientY,
                        baseX: pos.x,
                        baseY: pos.y,
                    };
                }}
                style={{
                    background: `linear-gradient(180deg, ${c.color}, color-mix(in oklab, ${c.color} 50%, #062060))`,
                }}
            >
                <span
                    style={{
                        width: 8,
                        height: 8,
                        borderRadius: 999,
                        background: 'white',
                        opacity: 0.7,
                    }}
                />
                <span
                    style={{
                        flex: 1,
                        overflow: 'hidden',
                        textOverflow: 'ellipsis',
                        whiteSpace: 'nowrap',
                    }}
                >
                    {c.primaryName} — character detail
                </span>
                <button
                    type="button"
                    className="imp-detail-close"
                    onClick={() => openDetail(null)}
                    aria-label="close"
                >
                    ✕
                </button>
            </div>
            <div className="imp-detail-body">
                <div className="imp-detail-row">
                    <span className="imp-detail-label">
                        Primary name
                        {c.dirty.primaryName && (
                            <button
                                type="button"
                                className="imp-detail-label-revert"
                                onClick={() => revert(charId, 'primaryName')}
                            >
                                revert
                            </button>
                        )}
                    </span>
                    <input
                        className="imp-detail-input"
                        value={c.primaryName}
                        onChange={(e) =>
                            setField(
                                charId,
                                'primaryName',
                                e.currentTarget.value,
                            )
                        }
                    />
                </div>
                <div className="imp-detail-row">
                    <span className="imp-detail-label">
                        Archetype
                        {c.dirty.archetype && (
                            <button
                                type="button"
                                className="imp-detail-label-revert"
                                onClick={() => revert(charId, 'archetype')}
                            >
                                revert
                            </button>
                        )}
                    </span>
                    <input
                        className="imp-detail-input"
                        value={c.archetype ?? ''}
                        placeholder="e.g. anxious archivist"
                        onChange={(e) =>
                            setField(charId, 'archetype', e.currentTarget.value)
                        }
                    />
                </div>
                <div className="imp-detail-row">
                    <span className="imp-detail-label">
                        Name variants
                        <span
                            style={{
                                marginLeft: 'auto',
                                color: 'rgba(180,200,230,0.5)',
                            }}
                        >
                            {c.names.join(', ')}
                        </span>
                    </span>
                </div>
                <div className="imp-detail-row">
                    <span className="imp-detail-label">
                        Prompt
                        {c.dirty.prompt && (
                            <button
                                type="button"
                                className="imp-detail-label-revert"
                                onClick={() => revert(charId, 'prompt')}
                            >
                                revert
                            </button>
                        )}
                    </span>
                    <textarea
                        className="imp-detail-textarea"
                        rows={5}
                        value={c.prompt}
                        onChange={(e) =>
                            setField(charId, 'prompt', e.currentTarget.value)
                        }
                    />
                </div>
                <div className="imp-detail-row">
                    <span className="imp-detail-label">
                        Bio
                        {c.dirty.bio && (
                            <button
                                type="button"
                                className="imp-detail-label-revert"
                                onClick={() => revert(charId, 'bio')}
                            >
                                revert
                            </button>
                        )}
                    </span>
                    <textarea
                        className="imp-detail-textarea"
                        rows={3}
                        value={c.bio ?? ''}
                        onChange={(e) =>
                            setField(charId, 'bio', e.currentTarget.value)
                        }
                    />
                </div>
                <div className="imp-detail-row">
                    <span className="imp-detail-label">
                        Evidence ({evidenceQuotes.length})
                    </span>
                    <div className="imp-detail-evidence">
                        {evidenceQuotes.length === 0 ? (
                            <em style={{ color: 'rgba(180,200,230,0.5)' }}>
                                No evidence quotes captured yet.
                            </em>
                        ) : (
                            evidenceQuotes.map((e) => (
                                <div
                                    key={`${e.msgId}-${e.quote.slice(0, 24)}`}
                                    className="imp-detail-evidence-q"
                                >
                                    <span
                                        style={{
                                            color: 'rgba(180,200,230,0.55)',
                                            marginRight: 4,
                                        }}
                                    >
                                        #{e.msgId}
                                    </span>
                                    {e.quote}
                                </div>
                            ))
                        )}
                    </div>
                </div>
                <div className="imp-detail-actions">
                    <button
                        type="button"
                        className="imp-detail-btn"
                        disabled={regenBusy !== null}
                        onClick={() => onRegen('prompt')}
                    >
                        {regenBusy === 'prompt'
                            ? 'Regen prompt…'
                            : 'Regen prompt'}
                    </button>
                    <button
                        type="button"
                        className="imp-detail-btn"
                        disabled={regenBusy !== null}
                        onClick={() => onRegen('bio')}
                    >
                        {regenBusy === 'bio' ? 'Regen bio…' : 'Regen bio'}
                    </button>
                    <button
                        type="button"
                        className="imp-detail-btn"
                        disabled={regenBusy !== null}
                        onClick={() => onRegen('both')}
                    >
                        Regen both
                    </button>
                    <button
                        type="button"
                        className="imp-detail-btn"
                        data-tone="danger"
                        onClick={() => {
                            if (
                                confirm(
                                    `Delete character "${c.primaryName}"? Linked mentions and segments will lose their assignment.`,
                                )
                            ) {
                                deleteChar(charId);
                            }
                        }}
                        style={{ marginLeft: 'auto' }}
                    >
                        Delete
                    </button>
                </div>
            </div>
        </div>
    );
}
