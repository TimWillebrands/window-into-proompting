import { DriverKind, type DriverOverrideEntry } from '../../api/model';

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
