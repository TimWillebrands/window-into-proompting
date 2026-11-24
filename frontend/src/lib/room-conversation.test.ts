import { describe, expect, it } from 'vitest';
import {
    heldBy,
    isShownInMessenger,
    phaseByPersona,
    typingState,
    typists,
} from './room-conversation';
import type { RealtimeChatMessage } from './room-state';

const msg = (
    messageId: number,
    over: Partial<RealtimeChatMessage> = {},
): RealtimeChatMessage => ({
    chatGroupId: 'r',
    messageId,
    content: null,
    reasoning: null,
    appraisal: null,
    error: null,
    senderType: 'assistant',
    senderId: 'vlad',
    sendAt: null,
    metadata: null,
    generationEvents: [],
    ...over,
});
describe('isShownInMessenger', () => {
    it('shows text and persona errors, never blank or user errors', () => {
        expect(isShownInMessenger(msg(1, { content: '  ' }))).toBe(false);
        expect(isShownInMessenger(msg(1, { content: 'yo' }))).toBe(true);
        expect(isShownInMessenger(msg(1, { error: 'x' }))).toBe(true);
        expect(
            isShownInMessenger(msg(1, { error: 'x', senderType: 'user' })),
        ).toBe(false);
    });
});

describe('phaseByPersona / typingState', () => {
    const deciding = {
        phase: 'deciding' as const,
        personaName: 'vlad',
        decisionText: '',
    };
    const typing = { phase: 'typing' as const, personaName: 'denise' };

    it('maps busy personas, ignoring idle and waiting slots', () => {
        const map = phaseByPersona([
            deciding,
            typing,
            { phase: 'waiting' },
            { phase: 'idle' },
        ]);
        expect([...map.keys()]).toEqual(['vlad', 'denise']);
    });

    it('typing beats reading beats idle', () => {
        expect(typingState([deciding, typing])).toBe('typing');
        expect(typingState([deciding])).toBe('reading');
        expect(typingState([])).toBe('idle');
    });

    it('lists who is typing once each', () => {
        const streaming = {
            phase: 'streaming' as const,
            personaName: 'denise',
            charCount: 3,
        };
        expect(typists([deciding, typing, streaming])).toEqual(['denise']);
    });
});

describe('heldBy', () => {
    const held = (
        messageId: number,
        reason = 'reflex: holding: finishing up first',
    ) => ({
        personaId: 'vlad',
        reason,
        decisionText: '',
        messageId,
        timestamp: 0,
    });

    it('shows when they saw the latest line and put it off', () => {
        const messages = [msg(1, { senderId: 'me' })];
        expect(heldBy('vlad', messages, [held(1)])).toBe(true);
    });

    it('clears once they have spoken', () => {
        const messages = [
            msg(1, { senderId: 'me' }),
            msg(2, { senderId: 'vlad' }),
        ];
        expect(heldBy('vlad', messages, [held(1)])).toBe(false);
    });

    it('ignores other skips', () => {
        const messages = [msg(1, { senderId: 'me' })];
        expect(heldBy('vlad', messages, [held(1, 'reflex: listen')])).toBe(
            false,
        );
        expect(heldBy('vlad', messages, [held(1, 'obvious-skip')])).toBe(false);
    });

    it('is about that person only', () => {
        const messages = [msg(1, { senderId: 'me' })];
        expect(heldBy('denise', messages, [held(1)])).toBe(false);
    });
});
