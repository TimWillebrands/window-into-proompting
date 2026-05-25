import { useDraggable, useDroppable } from '@dnd-kit/core';
import type { CSSProperties } from 'react';
import { useCharHover } from '../hooks/use-char-hover';
import { type CharId, useImportStore } from '../state/import-store';

type Props = { charId: CharId };

export function CharCard({ charId }: Props) {
    const c = useImportStore((s) => s.roster.chars[charId]);
    const openDetail = useImportStore((s) => s.openCharDetail);
    const hovered = useImportStore((s) => s.ui.hoveredCharId);
    const pinned = useImportStore((s) => s.ui.pinnedCharId);
    const { onMouseEnter, onMouseLeave, onClickPin } = useCharHover(charId);

    const {
        attributes,
        listeners,
        setNodeRef: setDragRef,
        isDragging,
    } = useDraggable({
        id: `char:${charId}`,
        data: { kind: 'char', charId },
    });
    const { setNodeRef: setDropRef, isOver } = useDroppable({
        id: `char-drop:${charId}`,
        data: { kind: 'char-drop', charId },
    });

    if (!c) return null;
    const active = charId === hovered || charId === pinned;

    const style: CSSProperties = {
        '--card-color': c.color,
        '--char-color': c.color,
        opacity: isDragging ? 0.4 : undefined,
    } as CSSProperties;

    const setRef = (node: HTMLDivElement | null) => {
        setDragRef(node);
        setDropRef(node);
    };

    return (
        // biome-ignore lint/a11y/useKeyWithClickEvents: dnd-kit drag handle; click is for hover-pin
        // biome-ignore lint/a11y/noStaticElementInteractions: dnd-kit drag handle; click is for hover-pin
        <div
            ref={setRef}
            className="imp-card"
            data-char-id={charId}
            data-char-id-active={active ? 'true' : 'false'}
            data-char-id-pinned={charId === pinned ? 'true' : 'false'}
            data-droppable-over={isOver ? 'true' : 'false'}
            data-orphan={c.isOrphan ? 'true' : 'false'}
            style={style}
            onMouseEnter={onMouseEnter}
            onMouseLeave={onMouseLeave}
            onClick={onClickPin}
            {...attributes}
            {...listeners}
        >
            {/* biome-ignore lint/a11y/useKeyWithClickEvents: title is also a clickable opener */}
            {/* biome-ignore lint/a11y/noStaticElementInteractions: title is also a clickable opener */}
            <div
                className="imp-card-name"
                onClick={(e) => {
                    e.stopPropagation();
                    openDetail(charId);
                }}
            >
                {c.primaryName || <em>unnamed</em>}
            </div>
            {c.archetype && (
                <div className="imp-card-archetype">{c.archetype}</div>
            )}
            <div className="imp-card-body">
                {c.reduceStatus === 'pending' ? (
                    <>
                        <div
                            className="imp-card-skel"
                            style={{ width: '90%' }}
                        />
                        <div
                            className="imp-card-skel"
                            style={{ width: '70%', marginTop: 4 }}
                        />
                    </>
                ) : (
                    c.bio ||
                    c.prompt.split('\n')[0] || (
                        <em style={{ color: 'rgba(180,200,230,0.5)' }}>
                            (no bio yet)
                        </em>
                    )
                )}
            </div>
            <div className="imp-card-meta">
                <span>
                    {c.names.length} name{c.names.length === 1 ? '' : 's'}
                </span>
                {Object.keys(c.dirty).length > 0 && (
                    <span style={{ color: 'rgb(255 220 110)' }}>· edited</span>
                )}
                {c.isOrphan && (
                    <span style={{ color: 'rgb(255 200 160)' }}>· orphan</span>
                )}
            </div>
        </div>
    );
}
