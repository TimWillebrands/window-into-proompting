// Room conversation rules shared by D'Accord and Messenger (a DM is a two-person Room).
import type { ActiveGenerationPhase, RealtimeChatMessage } from './room-state';

/** A persona turn's appraisal JSON, normalised (older messages used PascalCase keys). */
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

/** Latest "stop — your turn" aimed at the human's Persona that no user line has answered yet. */
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

/** D'Accord bubble rule: no empty in-flight slots; appraisal-only slots live in the Thought Log
 * unless they hand the turn to the human's Persona. */
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

/** Messenger rule: only lines with text, plus persona-side errors. */
export const isShownInMessenger = (m: RealtimeChatMessage): boolean =>
    !!m.content?.trim() || (!!m.error && m.senderType !== 'user');

/** Who is visibly busy in the Room, by persona: deciding, typing or streaming (latest phase wins). */
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

/** Messenger's one-line status for the other side: typing beats reading. */
export const typingState = (
    phases: readonly ActiveGenerationPhase[],
): 'typing' | 'reading' | 'idle' =>
    phases.some((p) => p.phase === 'typing' || p.phase === 'streaming')
        ? 'typing'
        : phases.some((p) => p.phase === 'deciding')
          ? 'reading'
          : 'idle';
