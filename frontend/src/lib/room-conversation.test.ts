import { describe, expect, it } from 'vitest';
import {
    isShownInMessenger,
    isShownInRoom,
    parseAppraisal,
    pendingInstructionFor,
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
const stopFor = (personaId: string, instruction = 'your call') =>
    JSON.stringify({ personaId, stop: true, instruction, reason: 'r' });

describe('parseAppraisal', () => {
    it('normalises legacy PascalCase keys', () => {
        expect(
            parseAppraisal('{"PersonaId":"tim","Stop":true,"Reason":"x"}'),
        ).toEqual({
            personaId: 'tim',
            reason: 'x',
            instruction: null,
            stop: true,
            raceCancelled: false,
        });
    });

    it('null for missing or malformed JSON', () => {
        expect(parseAppraisal(null)).toBeNull();
        expect(parseAppraisal('{nope')).toBeNull();
    });
});

describe('pendingInstructionFor', () => {
    it('surfaces the latest unanswered stop aimed at the human', () => {
        const messages = [
            msg(1, { appraisal: stopFor('tim', 'first') }),
            msg(2, { appraisal: stopFor('tim', 'second') }),
        ];
        expect(pendingInstructionFor(messages, 'tim')).toEqual({
            instruction: 'second',
            reason: 'r',
        });
    });

    it('clears once the human answers, and ignores stops for others', () => {
        const answered = [
            msg(1, { appraisal: stopFor('tim') }),
            msg(2, { senderType: 'user', content: 'ok' }),
        ];
        expect(pendingInstructionFor(answered, 'tim')).toBeNull();
        expect(
            pendingInstructionFor(
                [msg(1, { appraisal: stopFor('denise') })],
                'tim',
            ),
        ).toBeNull();
        expect(pendingInstructionFor(answered, '')).toBeNull();
    });
});

describe('isShownInRoom', () => {
    it('hides empty in-flight slots and appraisal-only declines', () => {
        expect(isShownInRoom(msg(1), true, 'tim')).toBe(false);
        expect(
            isShownInRoom(
                msg(1, { appraisal: stopFor('denise') }),
                false,
                'tim',
            ),
        ).toBe(false);
    });

    it('keeps text, errors and the human’s “your turn” card', () => {
        expect(isShownInRoom(msg(1, { content: 'hi' }), true, 'tim')).toBe(
            true,
        );
        expect(isShownInRoom(msg(1, { error: 'boom' }), false, 'tim')).toBe(
            true,
        );
        expect(
            isShownInRoom(msg(1, { appraisal: stopFor('tim') }), false, 'tim'),
        ).toBe(true);
    });
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
