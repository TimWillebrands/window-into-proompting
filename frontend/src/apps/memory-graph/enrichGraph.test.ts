import { describe, expect, it } from 'vitest';
import { enrichGraph } from './enrichGraph';
import type { MemoryGraphData } from './types';

const personaA = 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa';
const personaB = 'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb';
const partyId = 'cccccccc-cccc-cccc-cccc-cccccccccccc';
const roomA = '11111111-1111-1111-1111-111111111111';

const baseGraph: MemoryGraphData = {
    nodes: [
        { id: `room:${roomA}`, kind: 'Room' },
        { id: `msg:${roomA}:42`, kind: 'Message' },
        { id: 'event:e1', kind: 'Event', description: 'Hana defended Lisp.' },
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
            messages: [],
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
            messages: [],
        });

        const hana = enriched.nodes.find((n) => n.id === `persona:${personaA}`);
        expect(hana?.label).toBe(`Persona(${personaA.slice(0, 8)})`);
    });

    it('joins Room display names by id', () => {
        const enriched = enrichGraph(baseGraph, {
            personas: [],
            rooms: [{ id: roomA, name: 'general' }],
            messages: [],
        });

        const room = enriched.nodes.find((n) => n.id === `room:${roomA}`);
        expect(room?.label).toBe('general');
    });

    it('labels Concept nodes from their display value', () => {
        const enriched = enrichGraph(baseGraph, {
            personas: [],
            rooms: [],
            messages: [],
        });

        const concept = enriched.nodes.find((n) => n.id === 'concept:lisp');
        expect(concept?.label).toBe('Lisp');
    });

    it('labels Message nodes with #<id> only (never the body)', () => {
        const enriched = enrichGraph(baseGraph, {
            personas: [],
            rooms: [],
            messages: [{ id: 42, body: 'Lisp is elegant.', authorName: 'Hana' }],
        });

        const msg = enriched.nodes.find((n) => n.id === `msg:${roomA}:42`);
        expect(msg?.label).toBe('#42');
        expect(msg?.authorName).toBe('Hana');
    });

    it('labels Participant nodes by their backing Persona name', () => {
        const enriched = enrichGraph(baseGraph, {
            personas: [{ id: personaA, name: 'Hana' }],
            rooms: [],
            messages: [],
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
            messages: [],
        });

        const node = enriched.nodes.find((n) => n.id === 'mystery:xyz');
        expect(node?.kind).toBe('Mystery');
        expect(node?.label).toBe('mystery:xyz');
    });

    it('passes links through unmodified', () => {
        const graph: MemoryGraphData = {
            nodes: baseGraph.nodes,
            links: [
                { source: 'event:e1', target: `msg:${roomA}:42`, kind: 'ANCHORED_TO' },
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
            messages: [],
        });

        expect(enriched.links).toEqual(graph.links);
    });
});
