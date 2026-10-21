import { describe, expect, it } from 'vitest';
import { DriverKind } from '../../api/model';
import { speakAsOverrides } from './room-cast';

describe('speakAsOverrides', () => {
    const vladAsLlm = { personaId: 'vlad', kind: DriverKind.LLM };

    it('keeps other Personas’ LLM overrides when picking who to speak as', () => {
        expect(speakAsOverrides([vladAsLlm], 'tim')).toEqual([
            vladAsLlm,
            { personaId: 'tim', kind: DriverKind.User },
        ]);
    });

    it('moves the single User override to the new pick', () => {
        const current = [
            vladAsLlm,
            { personaId: 'tim', kind: DriverKind.User },
        ];
        expect(speakAsOverrides(current, 'denise')).toEqual([
            vladAsLlm,
            { personaId: 'denise', kind: DriverKind.User },
        ]);
    });

    it('replaces an LLM override on the Persona the human takes over', () => {
        expect(speakAsOverrides([vladAsLlm], 'vlad')).toEqual([
            { personaId: 'vlad', kind: DriverKind.User },
        ]);
    });

    it('nobody selected leaves only non-User overrides', () => {
        const current = [
            vladAsLlm,
            { personaId: 'tim', kind: DriverKind.User },
        ];
        expect(speakAsOverrides(current, '')).toEqual([vladAsLlm]);
    });
});
