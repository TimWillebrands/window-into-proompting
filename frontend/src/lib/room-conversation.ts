import type {
    ActiveGenerationPhase,
    DeclinedDecision,
    RealtimeChatMessage,
} from './room-state';

export const isShownInMessenger = (m: RealtimeChatMessage): boolean =>
    !!m.content?.trim() || (!!m.error && m.senderType !== 'user');

export const phaseByPersona = (
    phases: readonly ActiveGenerationPhase[],
): Map<string, ActiveGenerationPhase> => {
    const map = new Map<string, ActiveGenerationPhase>();
    for (const p of phases) {
        if (
            (p.phase === 'deciding' ||
                p.phase === 'typing' ||
                p.phase === 'streaming') &&
            p.personaName
        )
            map.set(p.personaName, p);
    }
    return map;
};

export const heldBy = (
    personaId: string,
    messages: readonly RealtimeChatMessage[],
    declined: readonly DeclinedDecision[],
): boolean => {
    const lastTheirs = messages.reduce(
        (at, m) => (m.senderId === personaId ? m.messageId : at),
        -1,
    );
    return declined.some(
        (d) =>
            d.personaId === personaId &&
            d.messageId > lastTheirs &&
            !!d.reason?.startsWith('reflex: holding:'),
    );
};

export const typingState = (
    phases: readonly ActiveGenerationPhase[],
): 'typing' | 'reading' | 'idle' =>
    phases.some((p) => p.phase === 'typing' || p.phase === 'streaming')
        ? 'typing'
        : phases.some((p) => p.phase === 'deciding')
          ? 'reading'
          : 'idle';

export const typists = (phases: readonly ActiveGenerationPhase[]): string[] => [
    ...new Set(
        phases.flatMap((p) =>
            p.phase === 'typing' || p.phase === 'streaming'
                ? [p.personaName]
                : [],
        ),
    ),
];
