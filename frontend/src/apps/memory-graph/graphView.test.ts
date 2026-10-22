import { describe, expect, it } from 'vitest';
import {
    buildView,
    neighbourhood,
    searchMatches,
    type ViewFilters,
} from './graphView';
import type {
    EnrichedMemoryGraph,
    EnrichedMemoryLink,
    EnrichedMemoryNode,
} from './types';

const hana = 'part:hana:p';
const vlad = 'part:vlad:p';

const node = (
    id: string,
    kind: string,
    extra: Partial<EnrichedMemoryNode> = {},
) => ({ id, kind, label: id, ...extra }) as EnrichedMemoryNode;
const link = (
    source: string,
    target: string,
    kind: string,
    extra: Partial<EnrichedMemoryLink> = {},
) => ({ source, target, kind, ...extra }) as EnrichedMemoryLink;

// Hana remembers e1+e2, Vlad e2+e3; "lisp" spans e1+e2, "tea" only e3.
const graph: EnrichedMemoryGraph = {
    nodes: [
        node(hana, 'Participant', { label: 'Hana' }),
        node(vlad, 'Participant', { label: 'Vlad' }),
        node('event:e1', 'Event', { description: 'Hana defended Lisp.' }),
        node('event:e2', 'Event', { description: 'Both argued about macros.' }),
        node('event:e3', 'Event', { description: 'Vlad brewed tea.' }),
        node('concept:lisp', 'Concept', { label: 'Lisp' }),
        node('concept:tea', 'Concept', { label: 'tea' }),
    ],
    links: [
        link(hana, 'event:e1', 'RECOLLECTS', {
            snippet: 'you stood your ground',
        }),
        link(hana, 'event:e2', 'RECOLLECTS'),
        link(vlad, 'event:e2', 'RECOLLECTS'),
        link(vlad, 'event:e3', 'RECOLLECTS'),
        link('event:e1', 'concept:lisp', 'ABOUT'),
        link('event:e2', 'concept:lisp', 'ABOUT'),
        link('event:e3', 'concept:tea', 'ABOUT'),
        link('event:e2', vlad, 'ABOUT'),
        link(hana, vlad, 'STANCE', { valence: -0.5 }),
        link(vlad, vlad, 'STANCE', { valence: 0.2 }),
    ],
};

const everyone: ViewFilters = {
    lens: null,
    concepts: 'shared',
    showStances: true,
};
const ids = (v: EnrichedMemoryGraph) => v.nodes.map((n) => n.id).sort();

describe('buildView', () => {
    it('hides one-off concepts by default and keeps recurring ones', () => {
        const view = buildView(graph, everyone);

        expect(ids(view)).toContain('concept:lisp');
        expect(ids(view)).not.toContain('concept:tea');
    });

    it('shows every concept in "all" mode and none in "none" mode', () => {
        expect(
            ids(buildView(graph, { ...everyone, concepts: 'all' })),
        ).toContain('concept:tea');
        expect(
            buildView(graph, { ...everyone, concepts: 'none' }).nodes.some(
                (n) => n.kind === 'Concept',
            ),
        ).toBe(false);
    });

    it('keeps ABOUT→Participant edges and self-stances off the canvas', () => {
        const view = buildView(graph, everyone);

        expect(
            view.links.some((l) => l.kind === 'ABOUT' && l.target === vlad),
        ).toBe(false);
        expect(view.links.some((l) => l.source === l.target)).toBe(false);
        expect(
            view.links.some((l) => l.kind === 'STANCE' && l.source === hana),
        ).toBe(true);
    });

    it('lens narrows to one person’s memories and recounts concept recurrence inside it', () => {
        const view = buildView(graph, {
            ...everyone,
            lens: vlad,
            showStances: false,
        });

        expect(ids(view)).toEqual(['event:e2', 'event:e3', vlad].sort());
        expect(
            view.links.every(
                (l) => l.kind !== 'RECOLLECTS' || l.source === vlad,
            ),
        ).toBe(true);
    });

    it('lens keeps stances pointing at the lensed person', () => {
        const view = buildView(graph, { ...everyone, lens: vlad });

        expect(ids(view)).toContain(hana);
        expect(
            view.links.filter(
                (l) => l.kind === 'RECOLLECTS' && l.source === hana,
            ),
        ).toHaveLength(0);
    });

    it('counts visible links per node', () => {
        const view = buildView(graph, everyone);

        expect(view.degree.get('concept:lisp')).toBe(2);
        expect(view.degree.get('event:e2')).toBe(3);
    });
});

describe('neighbourhood', () => {
    it('returns the node plus its direct neighbours in the view', () => {
        const view = buildView(graph, everyone);

        expect([...neighbourhood(view, 'event:e1')].sort()).toEqual(
            ['concept:lisp', 'event:e1', hana].sort(),
        );
    });

    it('includes the topics of a person’s memories', () => {
        const view = buildView(graph, everyone);

        expect(neighbourhood(view, hana).has('concept:lisp')).toBe(true);
    });
});

describe('searchMatches', () => {
    it('is null for a blank query', () => {
        expect(searchMatches(graph, '  ')).toBeNull();
    });

    it('matches labels, descriptions and recollection snippets case-insensitively', () => {
        expect(searchMatches(graph, 'LISP')).toEqual(
            new Set(['event:e1', 'concept:lisp']),
        );
        expect(searchMatches(graph, 'ground')).toEqual(new Set(['event:e1']));
    });
});
