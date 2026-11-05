import type { Persona } from '../../../api/model';
import { useResidency } from '../residence';

/** Asked once a new Persona has a name and bio worth arriving with. */
export default function MoveInDialog({
    persona,
    onClose,
}: {
    persona: Persona;
    onClose: () => void;
}) {
    const { moveIn, isPending, error } = useResidency();

    return (
        <div
            style={{
                position: 'absolute',
                inset: 0,
                background: 'rgba(0,0,0,0.4)',
                display: 'flex',
                alignItems: 'center',
                justifyContent: 'center',
                zIndex: 200,
            }}
        >
            <div className="border border-[#9db2c8] bg-xp-face p-4 w-[340px] shadow-[4px_4px_8px_rgba(0,0,0,0.3)] flex flex-col gap-2">
                <div className="bg-gradient-to-b from-xp-section-from to-xp-section-to text-white text-[11px] font-semibold px-2 py-[3px]">
                    Move into the World?
                </div>
                <p style={{ margin: 0 }}>
                    Should <strong>{persona.name}</strong> live in the World?
                    They get a Home, show up in Places and can be hired. You can
                    change this later in their editor.
                </p>
                {error ? (
                    <p style={{ color: '#c00', fontSize: '11px' }}>{error}</p>
                ) : null}
                <div className="flex justify-between" style={{ gap: '6px' }}>
                    <button
                        type="button"
                        onClick={onClose}
                        disabled={isPending}
                    >
                        Not now
                    </button>
                    <button
                        type="button"
                        disabled={isPending || !persona.id}
                        onClick={() =>
                            persona.id && moveIn(persona.id, onClose)
                        }
                    >
                        {isPending ? 'Moving in...' : 'Move in'}
                    </button>
                </div>
            </div>
        </div>
    );
}
