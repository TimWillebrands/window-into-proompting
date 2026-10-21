import { describe, expect, it } from 'vitest';
import {
    type ChatGroupRealtimeState,
    type PartyRealtimeEnvelope,
    phaseOf,
    type RealtimeChatMessage,
    reduceRooms,
} from './room-state';

const ROOM = 'room-1';
const VLAD = 'vlad';

const env = (
    type: string,
    data: unknown,
    sequence = 1,
    timestamp = 1000,
): PartyRealtimeEnvelope => ({ type, sequence, timestamp, data });

const msg = (
    messageId: number,
    over: Partial<RealtimeChatMessage> = {},
): RealtimeChatMessage => ({
    chatGroupId: ROOM,
    messageId,
    content: `m${messageId}`,
    reasoning: null,
    appraisal: null,
    error: null,
    senderType: 'assistant',
    senderId: VLAD,
    sendAt: null,
    metadata: null,
    generationEvents: [],
    ...over,
});

const delta = (
    messageId: number,
    event: string,
    data = '',
    done = false,
    sequence = 1,
) =>
    env(
        'party.generation.delta',
        { chatGroupId: ROOM, messageId, event, data, done },
        sequence,
    );

const fold = (envelopes: PartyRealtimeEnvelope[], now = 5000) =>
    envelopes.reduce<Record<string, ChatGroupRealtimeState>>(
        (rooms, e) => reduceRooms(rooms, e, now),
        {},
    );

describe('reduceRooms', () => {
    it('ignores envelopes that are not about a Room', () => {
        const rooms = {};
        expect(reduceRooms(rooms, env('world.activity', {}))).toBe(rooms);
        expect(reduceRooms(rooms, env('party.message.created', {}))).toBe(
            rooms,
        );
    });

    it('snapshot seeds sorted messages, skipped turns and race outcomes', () => {
        const rooms = fold([
            env(
                'party.snapshot',
                {
                    chatGroupId: ROOM,
                    messages: [msg(3), msg(1)],
                    skippedTurns: [
                        {
                            personaId: VLAD,
                            triggeredByMessageId: 1,
                            reason: 'meh',
                            sendAt: 7,
                        },
                    ],
                    raceEvaluations: [],
                },
                4,
            ),
        ]);

        expect(rooms[ROOM].messages.map((m) => m.messageId)).toEqual([1, 3]);
        expect(rooms[ROOM].declinedDecisions).toEqual([
            {
                personaId: VLAD,
                reason: 'meh',
                decisionText: '',
                messageId: 1,
                timestamp: 7,
            },
        ]);
        expect(rooms[ROOM].lastSequence).toBe(4);
    });

    it('message.created upserts in id order and keeps the high-water sequence', () => {
        const rooms = fold([
            env(
                'party.message.created',
                { chatGroupId: ROOM, message: msg(2) },
                9,
            ),
            env(
                'party.message.created',
                { chatGroupId: ROOM, message: msg(1) },
                3,
            ),
            env(
                'party.message.created',
                { chatGroupId: ROOM, message: msg(2, { content: 'edited' }) },
                10,
            ),
        ]);

        expect(
            rooms[ROOM].messages.map((m) => [m.messageId, m.content]),
        ).toEqual([
            [1, 'm1'],
            [2, 'edited'],
        ]);
        expect(rooms[ROOM].lastSequence).toBe(10);
    });

    it('streams deltas into a phantom message and clears it from active on done', () => {
        const rooms = fold([
            delta(5, 'attend', VLAD),
            delta(5, 'message', 'Hel'),
            delta(5, 'message', 'lo'),
        ]);
        expect(rooms[ROOM].messages[0]).toMatchObject({
            messageId: 5,
            senderId: VLAD,
            content: 'Hello',
        });
        expect(rooms[ROOM].activeGenerationMessageIds).toEqual([5]);

        const done = reduceRooms(rooms, delta(5, 'message', '!', true));
        expect(done[ROOM].messages[0].content).toBe('Hello!');
        expect(done[ROOM].activeGenerationMessageIds).toEqual([]);
    });

    it('retry wipes the partial draft', () => {
        const rooms = fold([
            delta(5, 'message', 'half a tho'),
            delta(5, 'retry'),
        ]);
        expect(rooms[ROOM].messages[0].content).toBe('');
    });

    it('declined drops the phantom and records the decision at `now`', () => {
        const rooms = fold(
            [
                delta(5, 'attend', VLAD),
                delta(
                    5,
                    'declined',
                    JSON.stringify({ personaId: VLAD, reason: 'not my topic' }),
                    true,
                ),
            ],
            4242,
        );

        expect(rooms[ROOM].messages).toEqual([]);
        expect(rooms[ROOM].activeGenerationMessageIds).toEqual([]);
        expect(rooms[ROOM].declinedDecisions).toEqual([
            {
                personaId: VLAD,
                reason: 'not my topic',
                decisionText: '',
                messageId: 5,
                timestamp: 4242,
            },
        ]);
    });

    it('truncation keeps messages up to the cut', () => {
        const rooms = fold([
            ...[1, 2, 3].map((id) =>
                env('party.message.created', {
                    chatGroupId: ROOM,
                    message: msg(id),
                }),
            ),
            env('party.messages.truncated', {
                chatGroupId: ROOM,
                messageId: 1,
            }),
        ]);
        expect(rooms[ROOM].messages.map((m) => m.messageId)).toEqual([1]);
    });
});

describe('phaseOf', () => {
    const room = (message: RealtimeChatMessage): ChatGroupRealtimeState => ({
        chatGroupId: ROOM,
        messages: [message],
        activeGenerationMessageIds: [message.messageId],
        declinedDecisions: [],
        raceEvaluations: [],
        lastSequence: 0,
    });
    const ev = (event: string, data = '') => ({ event, data, at: 0 });

    it('waits until a persona attends, then decides, types and streams', () => {
        expect(phaseOf(room(msg(1)), 1).phase).toBe('waiting');
        expect(
            phaseOf(room(msg(1, { generationEvents: [ev('attend', VLAD)] })), 1)
                .phase,
        ).toBe('deciding');

        const typing = msg(1, {
            generationEvents: [
                ev('attend', VLAD),
                ev('appraisalComplete'),
                ev('message'),
            ],
        });
        expect(phaseOf(room(typing), 1)).toMatchObject({
            phase: 'typing',
            personaName: VLAD,
        });

        const streaming = msg(1, {
            content: 'abcd',
            generationEvents: [
                ev('attend', VLAD),
                ev('appraisalComplete'),
                ev('message'),
                ev('message'),
                ev('message'),
            ],
        });
        expect(phaseOf(room(streaming), 1)).toMatchObject({
            phase: 'streaming',
            charCount: 4,
        });
    });
});
