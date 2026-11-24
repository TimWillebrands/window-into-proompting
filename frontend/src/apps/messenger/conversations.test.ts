import { describe, expect, it } from 'vitest';
import { ProblemDetailsError } from '../../api/custom-fetch';
import type { DirectChatSummary } from '../../api/model/directChatSummary';
import {
    conversationName,
    conversationTitle,
    hasOwnName,
    names,
    othersIn,
    refusal,
} from './conversations';

const member = (id: string, name: string, human = false) => ({
    id,
    name,
    human,
});

const dm: DirectChatSummary = {
    chatGroupId: 'dm-1',
    withId: 'ada',
    withName: 'Ada',
    lastMessage: null,
    lastSenderId: null,
    lastAt: 0,
    kind: 'dm',
    name: 'dm',
    members: [member('me', 'Grace', true), member('ada', 'Ada')],
};

const group: DirectChatSummary = {
    ...dm,
    chatGroupId: 'g-1',
    withId: '00000000-0000-0000-0000-000000000000',
    withName: 'Grace, Ada and Linus',
    kind: 'room',
    name: 'Grace, Ada and Linus',
    members: [
        member('me', 'Grace', true),
        member('ada', 'Ada'),
        member('linus', 'Linus'),
    ],
};

describe('names', () => {
    it('reads like the backend default', () => {
        expect(names([])).toBe('nobody');
        expect(names(['Ada'])).toBe('Ada');
        expect(names(['Ada', 'Linus'])).toBe('Ada and Linus');
        expect(names(['Ada', 'Linus', 'Hedy'])).toBe('Ada, Linus and Hedy');
        expect(names(['a', 'b', 'c', 'd', 'e'])).toBe('a, b, c and 2 others');
    });
});

describe('conversation names and titles', () => {
    it('names a DM after the other person', () => {
        expect(conversationName(dm, 'me')).toBe('Ada');
        expect(conversationTitle(dm, 'me')).toBe('Ada — Conversation');
    });

    it('names a group after the others in it, whatever its default name', () => {
        expect(othersIn(group, 'me').map((m) => m.name)).toEqual([
            'Ada',
            'Linus',
        ]);
        expect(hasOwnName(group)).toBe(false);
        expect(conversationName(group, 'me')).toBe('Ada and Linus');
        expect(conversationTitle(group, 'me')).toBe(
            'Conversation with Ada, Linus',
        );
    });

    it('keeps a group name that is its own', () => {
        const named = { ...group, name: 'Picnic club' };
        expect(conversationName(named, 'me')).toBe('Picnic club');
        expect(conversationTitle(named, 'me')).toBe(
            'Picnic club — Conversation',
        );
    });
});

describe('refusal', () => {
    it('shows the reason the API gave', () => {
        const e = new ProblemDetailsError({
            status: 400,
            title: 'Bad Request',
            detail: 'A group holds at most 8 people.',
        });
        expect(refusal(e, 'nope')).toBe('A group holds at most 8 people.');
        expect(refusal(new Error('x'), 'nope')).toBe('nope');
    });
});
