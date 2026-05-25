// Mirror of backend PartyTown.Model.DriverKind (Model/Party.cs). Orval emits the enum
// as `number`, so we re-export readable names. The integer values are part of the wire
// contract — keep them in lock-step with backend.
//
// See ADR 0012 and CONTEXT.md (Driver).
export const Driver = {
    User: 0,
    LLM: 1,
    System: 2,
} as const;

export type DriverKindValue = (typeof Driver)[keyof typeof Driver];

// Well-known stable Persona id for the singleton Narrator (matches backend Narrator.PersonaId).
// Used to render the Narrator row distinctly and to hide the driver-flip affordance for it.
export const NARRATOR_PERSONA_ID = '0000aaaa-0000-0000-0000-00000000a17e';

export function isUserDriven(p: { driver?: number }): boolean {
    return p.driver === Driver.User;
}

export function isLlmDriven(p: { driver?: number }): boolean {
    return p.driver === Driver.LLM;
}

export function isSystemDriven(p: { driver?: number }): boolean {
    return p.driver === Driver.System;
}
