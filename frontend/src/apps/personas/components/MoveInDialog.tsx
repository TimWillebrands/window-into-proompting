import { CommandButton, Dialog, Icon, Stack, StatusText } from '@partytown/ui';
import type { Persona } from '../../../api/model';
import { useResidency } from '../residence';

export default function MoveInDialog({
    persona,
    onClose,
}: {
    persona: Persona;
    onClose: () => void;
}) {
    const { moveIn, isPending, error } = useResidency();

    return (
        <Dialog
            contained
            title="Move into the World?"
            icon={<Icon name="house" />}
            onClose={onClose}
            className="w-[340px]"
            buttons={
                <>
                    <CommandButton
                        isDefault
                        type="button"
                        disabled={isPending || !persona.id}
                        onClick={() =>
                            persona.id && moveIn(persona.id, onClose)
                        }
                    >
                        {isPending ? 'Moving in...' : 'Move in'}
                    </CommandButton>
                    <CommandButton onClick={onClose} disabled={isPending}>
                        Not now
                    </CommandButton>
                </>
            }
        >
            <Stack gap="md">
                <p className="m-0">
                    Should <strong>{persona.name}</strong> live in the World?
                    They get a Home, show up in Places and can be hired. You can
                    change this later in their editor.
                </p>
                {error ? <StatusText tone="danger">{error}</StatusText> : null}
            </Stack>
        </Dialog>
    );
}
