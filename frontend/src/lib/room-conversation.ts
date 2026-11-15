import type {
    ActiveGenerationPhase,
    DeclinedDecision,
    RealtimeChatMessage,
} from './room-state';

export interface Appraisal {
    personaId: string | null;
    reason: string | null;
    instruction: string | null;
    stop: boolean;
    raceCancelled: boolean;
}

export const parseAppraisal = (
    raw: string | null | undefined,
): Appraisal | null => {
    if (!raw) return null;
    try {
        const o = JSON.parse(raw);
        return {
            personaId: o.personaId ?? o.PersonaId ?? null,
            reason: o.reason ?? o.Reason ?? null,
            instruction: o.instruction ?? o.Instruction ?? null,
            stop: !!(o.stop ?? o.Stop),
            raceCancelled: o.raceCancelled === true,
        };
    } catch {
        return null;
    }
};

export const pendingInstructionFor = (
    messages: readonly RealtimeChatMessage[],
    userPersonaId: string,
): { instruction: string | null; reason: string | null } | null => {
    if (!userPersonaId) return null;
    let lastStop = -1;
    let pending: Appraisal | null = null;
    messages.forEach((m, i) => {
        const a = parseAppraisal(m.appraisal);
        if (a?.stop && a.personaId === userPersonaId) {
            lastStop = i;
            pending = a;
        }
    });
    if (lastStop < 0 || !pending) return null;
    if (messages.slice(lastStop + 1).some((m) => m.senderType === 'user'))
        return null;
    const { instruction, reason } = pending as Appraisal;
    return { instruction, reason };
};

export const isShownInRoom = (
    m: RealtimeChatMessage,
    generating: boolean,
    userPersonaId: string,
): boolean => {
    if (generating && !m.content && !m.error) return false;
    if (m.content || m.error || !m.appraisal) return true;
    const a = parseAppraisal(m.appraisal);
    return !!a?.stop && a.personaId === userPersonaId;
};

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
