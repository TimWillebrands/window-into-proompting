import { useDraggable } from '@dnd-kit/core';
import type { CSSProperties } from 'react';
import { useCharHover } from '../hooks/use-char-hover';
import { type MsgId, useImportStore } from '../state/import-store';

type Props = {
    msgId: MsgId;
    mentionId: string;
};

export function ChipPill({ msgId, mentionId }: Props) {
    const participant = useImportStore(
        (s) =>
            s.msgs[msgId]?.participants.find(
                (p) => p.mentionId === mentionId,
            ) ?? null,
    );
    const charColor = useImportStore((s) =>
        participant?.linkedCharId
            ? (s.roster.chars[participant.linkedCharId]?.color ?? null)
            : null,
    );
    const charPrimary = useImportStore((s) =>
        participant?.linkedCharId
            ? s.roster.chars[participant.linkedCharId]?.primaryName
            : null,
    );
    const hovered = useImportStore((s) => s.ui.hoveredCharId);
    const pinned = useImportStore((s) => s.ui.pinnedCharId);
    const charId = participant?.linkedCharId ?? null;
    const { onMouseEnter, onMouseLeave, onClickPin } = useCharHover(charId);

    const { attributes, listeners, setNodeRef, isDragging } = useDraggable({
        id: `chip:${msgId}:${mentionId}`,
        data: { kind: 'chip', msgId, mentionId },
    });

    if (!participant) return null;

    const orphan = !charId;
    const style: CSSProperties = {
        '--chip-color': charColor ?? 'rgba(180, 200, 230, 0.4)',
        '--char-color': charColor ?? 'rgba(180, 200, 230, 0.4)',
        opacity: isDragging ? 0.4 : undefined,
    } as CSSProperties;

    const label = charPrimary ?? participant.rawName;

    return (
        // biome-ignore lint/a11y/useKeyWithClickEvents: dnd-kit drag handle; click is for hover-pin
        // biome-ignore lint/a11y/noStaticElementInteractions: dnd-kit drag handle; click is for hover-pin
        <span
            ref={setNodeRef}
            className="imp-chip"
            data-orphan={orphan}
            data-char-id={charId ?? ''}
            data-char-id-active={
                charId !== null && (charId === hovered || charId === pinned)
                    ? 'true'
                    : 'false'
            }
            data-char-id-pinned={
                charId !== null && charId === pinned ? 'true' : 'false'
            }
            style={style}
            title={`${label} — ${participant.evidence.slice(0, 200)}${participant.evidence.length > 200 ? '…' : ''}`}
            onMouseEnter={onMouseEnter}
            onMouseLeave={onMouseLeave}
            onClick={onClickPin}
            {...attributes}
            {...listeners}
        >
            <span className="imp-chip-dot" />
            {label}
        </span>
    );
}
