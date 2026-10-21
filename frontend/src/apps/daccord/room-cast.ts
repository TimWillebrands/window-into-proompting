import { DriverKind, type DriverOverrideEntry } from '../../api/model';

/**
 * Room overrides after picking who the human speaks as: other Personas' LLM/System overrides
 * survive; User overrides collapse to the one selected (or none).
 */
export const speakAsOverrides = (
    current: readonly DriverOverrideEntry[],
    userPersonaId: string,
): DriverOverrideEntry[] => {
    const kept = current.filter(
        (o) =>
            !!o.personaId &&
            o.kind !== DriverKind.User &&
            o.personaId !== userPersonaId,
    );
    return userPersonaId
        ? [...kept, { personaId: userPersonaId, kind: DriverKind.User }]
        : kept;
};
