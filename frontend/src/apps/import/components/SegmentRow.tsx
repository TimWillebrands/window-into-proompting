import { useDroppable } from '@dnd-kit/core';
import type { CSSProperties } from 'react';
import { useCharHover } from '../hooks/use-char-hover';
import {
    type MsgId,
    type SegmentDraft,
    useImportStore,
} from '../state/import-store';

type Props = {
    msgId: MsgId;
    segment: SegmentDraft;
    firstOfChar: boolean;
};

export function SegmentRow({ msgId, segment, firstOfChar }: Props) {
    const dropSegment = useImportStore((s) => s.dropSegment);
    const charColor = useImportStore((s) =>
        segment.charId ? (s.roster.chars[segment.charId]?.color ?? null) : null,
    );
    const charName = useImportStore((s) =>
        segment.charId
            ? (s.roster.chars[segment.charId]?.primaryName ?? null)
            : null,
    );
    const hovered = useImportStore((s) => s.ui.hoveredCharId);
    const pinned = useImportStore((s) => s.ui.pinnedCharId);
    const { onMouseEnter, onMouseLeave, onClickPin } = useCharHover(
        segment.charId,
    );

    const { setNodeRef, isOver } = useDroppable({
        id: `segment-drop:${segment.id}`,
        data: { kind: 'segment', msgId, segmentId: segment.id },
    });

    const color = charColor ?? 'rgba(180, 200, 230, 0.3)';
    const style: CSSProperties = {
        '--seg-color': color,
        '--char-color': color,
    } as CSSProperties;

    const active =
        segment.charId !== null &&
        (segment.charId === hovered || segment.charId === pinned);

    return (
        // biome-ignore lint/a11y/useKeyWithClickEvents: dnd-kit drop zone; click is for hover-pin
        // biome-ignore lint/a11y/noStaticElementInteractions: dnd-kit drop zone; click is for hover-pin
        <div
            ref={setNodeRef}
            className="imp-seg-row"
            data-msg-id={msgId}
            data-segment-id={segment.id}
            data-char-id={segment.charId ?? ''}
            data-char-id-active={active ? 'true' : 'false'}
            data-char-id-pinned={
                segment.charId !== null && segment.charId === pinned
                    ? 'true'
                    : 'false'
            }
            data-droppable-over={isOver ? 'true' : 'false'}
            data-segment-first-of-char={
                firstOfChar && segment.charId ? segment.charId : undefined
            }
            style={style}
            onMouseEnter={onMouseEnter}
            onMouseLeave={onMouseLeave}
            onClick={onClickPin}
        >
            <div className="imp-seg-bar" />
            <div className="imp-seg-body">
                <div className="imp-seg-meta">
                    <span className="imp-seg-name">
                        {charName ?? (
                            <em style={{ color: 'rgba(255,200,160,0.85)' }}>
                                unassigned
                            </em>
                        )}
                    </span>
                    <span className="imp-seg-kind">{segment.kind}</span>
                    {segment.manualEdit && (
                        <span
                            title="Manual edit — survives re-classify of other messages"
                            style={{ fontSize: 9, color: 'rgb(255 220 110)' }}
                        >
                            ✎
                        </span>
                    )}
                </div>
                <div className="imp-seg-text">{segment.text}</div>
            </div>
            <div className="imp-seg-actions">
                <button
                    type="button"
                    className="imp-msg-icon-btn"
                    title="Drop segment"
                    onClick={(e) => {
                        e.stopPropagation();
                        dropSegment(msgId, segment.id);
                    }}
                >
                    ✕
                </button>
            </div>
        </div>
    );
}
