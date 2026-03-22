import { defaultPersonas } from '../defaultPersonas';
import type { Persona, PersonaMetadata } from './types';

let personas = defaultPersonas.map((persona, index) => ({
    id: `persona-${index + 1}`,
    name: persona.name,
    systemPrompt: persona.systemPrompt,
})) satisfies Persona[];

export function listPersonas(): PersonaMetadata[] {
    return personas.map(({ id, name }) => ({ id, name }));
}

export function getPersona(id: string): Persona | undefined {
    return personas.find((persona) => persona.id === id);
}

export function upsertPersona(updated: Persona): Persona {
    const existingIndex = personas.findIndex(
        (persona) => persona.id === updated.id,
    );
    if (existingIndex >= 0) {
        personas[existingIndex] = { ...updated };
    } else {
        personas = [...personas, { ...updated }];
    }
    return updated;
}

export function deletePersona(id: string): void {
    personas = personas.filter((persona) => persona.id !== id);
}

export function createPersona(partial?: Partial<Persona>): Persona {
    const persona: Persona = {
        id: partial?.id ?? crypto.randomUUID(),
        name: partial?.name ?? 'New Persona',
        systemPrompt: partial?.systemPrompt ?? 'You are a helpful persona.',
        bio: partial?.bio,
    };
    personas = [...personas, persona];
    return persona;
}
