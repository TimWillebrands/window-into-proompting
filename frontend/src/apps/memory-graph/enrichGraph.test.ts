import { describe, expect, it } from 'vitest';
import { enrichGraph } from './enrichGraph';
import { PERSON_PALETTE } from './palette';
import type { MemoryGraphData } from './types';

const personaA = 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa';
const personaB = 'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb';
const partyId = 'cccccccc-cccc-cccc-cccc-cccccccccccc';
const roomA = '11111111-1111-1111-1111-111111111111';
const threadA = '22222222-2222-2222-2222-222222222222';

const baseGraph: MemoryGraphData = {
    nodes: [
        {
            id: 'event:e1',
            kind: 'Event',
            description: 'Hana defended Lisp.',
            roomId: roomA,
            anchorMessageId: 42,
            source: 'rooms',
        },
        { id: 'concept:lisp', kind: 'Concept', display: 'Lisp' },
        { id: `part:${personaA}:${partyId}`, kind: 'Participant' },
        { id: `part:${personaB}:${partyId}`, kind: 'Participant' },
    ],
    links: [],
};

const empty = { personas: [], rooms: [] };

describe('enrichGraph', () => {
    it('labels Participants by their backing Persona name', () => {
        const enriched = enrichGraph(baseGraph, {
            personas: [{ id: personaA, name: 'Hana' }],
            rooms: [],
        });

        const part = enriched.nodes.find(
            (n) => n.id === `part:${personaA}:${partyId}`,
        );
        expect(part?.label).toBe('Hana');
    });

    it('falls back to a short guid when no Persona matches', () => {
        const enriched = enrichGraph(baseGraph, empty);

        const part = enriched.nodes.find(
            (n) => n.id === `part:${personaB}:${partyId}`,
        );
        expect(part?.label).toBe(`Participant(${personaB.slice(0, 8)})`);
    });

    it('gives each Participant a distinct palette colour', () => {
        const enriched = enrichGraph(baseGraph, empty);

        const colours = enriched.nodes
            .filter((n) => n.kind === 'Participant')
            .map((n) => n.colour);
        expect(colours).toEqual([PERSON_PALETTE[0], PERSON_PALETTE[1]]);
    });

    it('labels Events with their App and thread name', () => {
        const enriched = enrichGraph(baseGraph, {
            personas: [],
            rooms: [{ id: roomA, name: 'general', kind: 'room' }],
        });

        const event = enriched.nodes.find((n) => n.id === 'event:e1');
        expect(event?.where).toBe('Room · general');
        expect(event?.label).toBe('Room · general');
    });

    it('resolves Board threads by title and infers the source for legacy Events', () => {
        const graph: MemoryGraphData = {
            nodes: [{ id: 'event:e2', kind: 'Event', roomId: threadA }],
            links: [],
        };
        const enriched = enrichGraph(graph, {
            ...empty,
            boardThreads: [{ id: threadA, title: 'Ginkgo watch' }],
        });

        expect(enriched.nodes[0]).toMatchObject({
            source: 'board',
            where: 'Board · Ginkgo watch',
        });
    });

    it('falls back to a short thread guid when nothing resolves', () => {
        const enriched = enrichGraph(baseGraph, empty);

        const event = enriched.nodes.find((n) => n.id === 'event:e1');
        expect(event?.where).toBe(`Room · ${roomA.slice(0, 8)}`);
    });

    it('labels Concepts from their display value', () => {
        const enriched = enrichGraph(baseGraph, empty);

        const concept = enriched.nodes.find((n) => n.id === 'concept:lisp');
        expect(concept?.label).toBe('Lisp');
    });

    it('passes unknown node kinds through with the id as label', () => {
        const graph: MemoryGraphData = {
            nodes: [{ id: 'mystery:xyz', kind: 'Mystery' }],
            links: [],
        };
        const enriched = enrichGraph(graph, empty);

        expect(enriched.nodes[0]).toMatchObject({
            kind: 'Mystery',
            label: 'mystery:xyz',
        });
    });

    it('normalises numeric link fields the DTO types as number | string', () => {
        const graph: MemoryGraphData = {
            nodes: baseGraph.nodes,
            links: [
                {
                    source: `part:${personaA}:${partyId}`,
                    target: 'event:e1',
                    kind: 'RECOLLECTS',
                    snippet: 'you defended Lisp',
                    weight: '0.7',
                    recallCount: 2,
                },
                {
                    source: `part:${personaA}:${partyId}`,
                    target: `part:${personaB}:${partyId}`,
                    kind: 'STANCE',
                    valence: -0.4,
                },
            ],
        };
        const enriched = enrichGraph(graph, empty);

        expect(enriched.links[0]).toMatchObject({
            snippet: 'you defended Lisp',
            weight: 0.7,
            recallCount: 2,
            valence: null,
        });
        expect(enriched.links[1].valence).toBe(-0.4);
    });
});
