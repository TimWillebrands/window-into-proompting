import { CommandButton, Icon } from '@partytown/ui';
import type { BoardRespect } from '../../api/model/boardRespect';
import { hasRespectFrom, respectLabel } from './board-lib';

interface Props {
    respect: BoardRespect[] | undefined;
    playerId: string | null;
    own: boolean;
    onToggle: (given: boolean) => void;
}

export default function RespectButton({
    respect,
    playerId,
    own,
    onToggle,
}: Props) {
    const count = respect?.length ?? 0;
    const given = hasRespectFrom(respect, playerId);
    const label = respectLabel(respect, playerId);
    return (
        <CommandButton
            size="small"
            icon={<Icon name="star" />}
            pressed={given}
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
            Respect!{count > 0 ? ` ${count}` : ''}
        </CommandButton>
    );
}
