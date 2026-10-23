import type { BoardRespect } from '../../api/model/boardRespect';
import { hasRespectFrom, respectLabel } from './board-lib';

interface Props {
    respect: BoardRespect[] | undefined;
    playerId: string | null;
    /** Your own lines can't get your Respect!. */
    own: boolean;
    onToggle: (given: boolean) => void;
    small?: boolean;
}

/** Hyves' Respect!: a like with a name on it. */
export default function RespectButton({
    respect,
    playerId,
    own,
    onToggle,
    small,
}: Props) {
    const count = respect?.length ?? 0;
    const given = hasRespectFrom(respect, playerId);
    const label = respectLabel(respect, playerId);
    return (
        <button
            type="button"
            className={`pb-respect ${given ? 'pb-respect-on' : ''} ${small ? 'pb-respect-small' : ''}`}
            aria-pressed={given}
            disabled={own || !playerId}
            title={
                own
                    ? count
                        ? label
                        : 'Respect! from others shows here'
                    : label
            }
            onClick={() => onToggle(!given)}
        >
            <span aria-hidden>★</span>
            <span>Respect!</span>
            {count > 0 && <span className="pb-respect-count">{count}</span>}
        </button>
    );
}
