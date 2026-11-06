import type { Persona } from '../../../api/model';
import { useIsAdmin } from '../../../lib/world';
import { canMoveIn, useResidency, useResidents } from '../residence';

/** Whether this Persona lives in the World, and the way in or out. */
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
        <fieldset className="border border-[#9db2c8] p-2 m-0">
            <legend className="text-xp-legend font-semibold text-[11px] px-1">
                World
            </legend>
            <div
                className="flex items-center justify-between"
                style={{ gap: 8 }}
            >
                <span>
                    {lives
                        ? 'Lives in the World: has a Home and can be hired.'
                        : 'Library only: not in Places and not for hire.'}
                </span>
                {isAdmin && lives ? (
                    <button
                        type="button"
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
                    </button>
                ) : null}
                {isAdmin && !lives ? (
                    <button
                        type="button"
                        // Name and look are fixed on arrival.
                        disabled={isPending || unsaved}
                        title={unsaved ? 'Save your changes first' : undefined}
                        onClick={() => moveIn(id)}
                    >
                        Move in
                    </button>
                ) : null}
            </div>
            {error ? <p style={{ color: '#c00' }}>{error}</p> : null}
        </fieldset>
    );
}
