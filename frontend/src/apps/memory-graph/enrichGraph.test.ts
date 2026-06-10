import { describe, expect, it } from 'vitest';
import { enrichGraph } from './enrichGraph';
import type { MemoryGraphData } from './types';

const personaA = 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa';
const personaB = 'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb';
const partyId = 'cccccccc-cccc-cccc-cccc-cccccccccccc';
const roomA = '11111111-1111-1111-1111-111111111111';

const baseGraph: MemoryGraphData = {
    nodes: [
        {
            id: 'event:e1',
            kind: 'Event',
            description: 'Hana defended Lisp.',
            roomId: roomA,
            anchorMessageId: 42,
        },
        { id: 'concept:lisp', kind: 'Concept', display: 'Lisp' },
        { id: `part:${personaA}:${partyId}`, kind: 'Participant' },
        { id: `persona:${personaA}`, kind: 'Persona' },
        { id: `persona:${personaB}`, kind: 'Persona' },
    ],
    links: [],
};

describe('enrichGraph', () => {
    it('joins Persona display names by id', () => {
        const enriched = enrichGraph(baseGraph, {
            personas: [
                { id: personaA, name: 'Hana' },
                { id: personaB, name: 'Vlad' },
            ],
            rooms: [],
        });

        const hana = enriched.nodes.find((n) => n.id === `persona:${personaA}`);
        const vlad = enriched.nodes.find((n) => n.id === `persona:${personaB}`);
        expect(hana?.label).toBe('Hana');
        expect(vlad?.label).toBe('Vlad');
    });

    it('falls back to short guid when no Persona match', () => {
        const enriched = enrichGraph(baseGraph, {
            personas: [],
            rooms: [],
        });

        const hana = enriched.nodes.find((n) => n.id === `persona:${personaA}`);
        expect(hana?.label).toBe(`Persona(${personaA.slice(0, 8)})`);
    });

    it('labels Event nodes with their Room name (from the Event roomId property)', () => {
        const enriched = enrichGraph(baseGraph, {
            personas: [],
            rooms: [{ id: roomA, name: 'general' }],
        });

        const event = enriched.nodes.find((n) => n.id === 'event:e1');
        expect(event?.label).toBe('general');
    });

    it('falls back to short room guid when no Room match for an Event', () => {
        const enriched = enrichGraph(baseGraph, {
            personas: [],
            rooms: [],
        });

        const event = enriched.nodes.find((n) => n.id === 'event:e1');
        expect(event?.label).toBe(`Room(${roomA.slice(0, 8)})`);
    });

    it('labels Concept nodes from their display value', () => {
        const enriched = enrichGraph(baseGraph, {
            personas: [],
            rooms: [],
        });

        const concept = enriched.nodes.find((n) => n.id === 'concept:lisp');
        expect(concept?.label).toBe('Lisp');
    });

    it('labels Participant nodes by their backing Persona name', () => {
        const enriched = enrichGraph(baseGraph, {
            personas: [{ id: personaA, name: 'Hana' }],
            rooms: [],
        });

        const part = enriched.nodes.find(
            (n) => n.id === `part:${personaA}:${partyId}`,
        );
        expect(part?.label).toBe('Hana');
    });

    it('passes unknown node kinds through unchanged (only a fallback label is added)', () => {
        const graph: MemoryGraphData = {
            nodes: [{ id: 'mystery:xyz', kind: 'Mystery' }],
            links: [],
        };
        const enriched = enrichGraph(graph, {
            personas: [],
            rooms: [],
        });

        const node = enriched.nodes.find((n) => n.id === 'mystery:xyz');
        expect(node?.kind).toBe('Mystery');
        expect(node?.label).toBe('mystery:xyz');
    });

    it('passes links through unmodified', () => {
        const graph: MemoryGraphData = {
            nodes: baseGraph.nodes,
            links: [
                {
                    source: 'event:e1',
                    target: 'concept:lisp',
                    kind: 'ABOUT',
                },
                {
                    source: `part:${personaA}:${partyId}`,
                    target: 'event:e1',
                    kind: 'RECOLLECTS',
                    snippet: 'you defended Lisp',
                },
            ],
        };
        const enriched = enrichGraph(graph, {
            personas: [],
            rooms: [],
        });

        expect(enriched.links).toEqual(graph.links);
    });
});
