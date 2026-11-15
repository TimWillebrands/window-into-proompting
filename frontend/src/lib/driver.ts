export const Driver = {
    User: 0,
    LLM: 1,
    System: 2,
} as const;

export type DriverKindValue = (typeof Driver)[keyof typeof Driver];

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
