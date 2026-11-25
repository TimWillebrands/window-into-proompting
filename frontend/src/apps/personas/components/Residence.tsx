import { CommandButton, GroupBox, Stack, StatusText } from '@partytown/ui';
import type { Persona } from '../../../api/model';
import { useIsAdmin } from '../../../lib/world';
import { canMoveIn, useResidency, useResidents } from '../residence';

export default function Residence({
    persona,
    unsaved,
}: {
    persona: Persona;
    unsaved: boolean;
}) {
    const residents = useResidents();
    const { moveIn, moveOut, isPending, error } = useResidency();
    const isAdmin = useIsAdmin();
    const id = persona.id;
    if (!id || !canMoveIn(persona)) return null;
    const lives = residents.has(id);

    return (
        <GroupBox title="World">
            <Stack direction="row" align="center" justify="between" gap="md">
                <span>
                    {lives
                        ? 'Lives in the World: has a Home and can be hired.'
                        : 'Library only: not in Places and not for hire.'}
                </span>
                {isAdmin && lives ? (
                    <CommandButton
                        tone="caution"
                        disabled={isPending}
                        onClick={() => {
                            if (
                                confirm(
                                    `Move ${persona.name} out of the World? Their Home goes with them.`,
                                )
                            )
                                moveOut(id);
                        }}
                    >
                        Move out
                    </CommandButton>
                ) : null}
                {isAdmin && !lives ? (
                    <CommandButton
                        tone="go"
                        disabled={isPending || unsaved}
                        title={unsaved ? 'Save your changes first' : undefined}
                        onClick={() => moveIn(id)}
                    >
                        Move in
                    </CommandButton>
                ) : null}
            </Stack>
            {error ? <StatusText tone="danger">{error}</StatusText> : null}
        </GroupBox>
    );
}
