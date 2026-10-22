import type {
    EnrichedMemoryGraph,
    EnrichedMemoryLink,
    EnrichedMemoryNode,
} from './types';

export type ConceptMode = 'shared' | 'all' | 'none';

export interface ViewFilters {
    /// <summary>Participant node id whose memories to show; null = everyone.</summary>
    lens: string | null;
    concepts: ConceptMode;
    showStances: boolean;
}

export interface MemoryView extends EnrichedMemoryGraph {
    /// <summary>Visible-link count per node id — drives Concept size and label priority.</summary>
    degree: Map<string, number>;
}

export interface GraphIndex {
    byId: Map<string, EnrichedMemoryNode>;
    outgoing: Map<string, EnrichedMemoryLink[]>;
    incoming: Map<string, EnrichedMemoryLink[]>;
}

export function indexGraph(graph: EnrichedMemoryGraph): GraphIndex {
    const byId = new Map(graph.nodes.map((n) => [n.id, n]));
    const outgoing = new Map<string, EnrichedMemoryLink[]>();
    const incoming = new Map<string, EnrichedMemoryLink[]>();
    for (const link of graph.links) {
        push(outgoing, link.source, link);
        push(incoming, link.target, link);
    }
    return { byId, outgoing, incoming };
}

/// <summary>The drawable subgraph: lens + concept threshold + stances; ABOUT→Participant stays panel-only.</summary>
export function buildView(
    graph: EnrichedMemoryGraph,
    filters: ViewFilters,
): MemoryView {
    const kindById = new Map(graph.nodes.map((n) => [n.id, n.kind]));
    const visibleEvents = new Set<string>();
    const recollections: EnrichedMemoryLink[] = [];
    for (const link of graph.links) {
        if (link.kind !== 'RECOLLECTS' || !kindById.has(link.target)) continue;
        if (filters.lens && link.source !== filters.lens) continue;
        visibleEvents.add(link.target);
        recollections.push(link);
    }

    const conceptEvents = new Map<string, number>();
    const abouts: EnrichedMemoryLink[] = [];
    for (const link of graph.links) {
        if (link.kind !== 'ABOUT' || !visibleEvents.has(link.source)) continue;
        if (kindById.get(link.target) !== 'Concept') continue;
        conceptEvents.set(
            link.target,
            (conceptEvents.get(link.target) ?? 0) + 1,
        );
        abouts.push(link);
    }
    const minEvents =
        filters.concepts === 'all'
            ? 1
            : filters.concepts === 'shared'
              ? 2
              : Number.POSITIVE_INFINITY;
    const visibleConcepts = new Set(
        [...conceptEvents].filter(([, n]) => n >= minEvents).map(([id]) => id),
    );

    const visible = new Set<string>([...visibleEvents, ...visibleConcepts]);
    for (const link of recollections) visible.add(link.source);
    if (filters.lens) visible.add(filters.lens);

    const stances: EnrichedMemoryLink[] = [];
    if (filters.showStances) {
        for (const link of graph.links) {
            if (link.kind !== 'STANCE' || link.source === link.target) continue;
            if (
                filters.lens &&
                link.source !== filters.lens &&
                link.target !== filters.lens
            )
                continue;
            stances.push(link);
            visible.add(link.source);
            visible.add(link.target);
        }
    }

    const links = [
        ...recollections,
        ...abouts.filter((l) => visible.has(l.target)),
        ...stances.filter(
            (l) => kindById.has(l.source) && kindById.has(l.target),
        ),
    ];
    const degree = new Map<string, number>();
    for (const link of links) {
        degree.set(link.source, (degree.get(link.source) ?? 0) + 1);
        degree.set(link.target, (degree.get(link.target) ?? 0) + 1);
    }
    const nodes = graph.nodes.filter((n) => visible.has(n.id));
    return { nodes, links, degree };
}

/// <summary>The node, its direct neighbours, and the topics of any memories among them.</summary>
export function neighbourhood(
    view: EnrichedMemoryGraph,
    id: string,
): Set<string> {
    const ids = new Set([id]);
    for (const link of view.links) {
        const source = endpointId(link.source);
        const target = endpointId(link.target);
        if (source === id) ids.add(target);
        else if (target === id) ids.add(source);
    }
    const topics: string[] = [];
    for (const link of view.links) {
        if (link.kind === 'ABOUT' && ids.has(endpointId(link.source))) {
            topics.push(endpointId(link.target));
        }
    }
    for (const topic of topics) ids.add(topic);
    return ids;
}

/// <summary>Case-insensitive hit on labels, Event descriptions and Recollection snippets; null when the query is blank.</summary>
export function searchMatches(
    graph: EnrichedMemoryGraph,
    query: string,
): Set<string> | null {
    const q = query.trim().toLowerCase();
    if (!q) return null;
    const hits = new Set<string>();
    for (const node of graph.nodes) {
        if (
            node.label.toLowerCase().includes(q) ||
            node.description?.toLowerCase().includes(q)
        ) {
            hits.add(node.id);
        }
    }
    for (const link of graph.links) {
        if (link.snippet?.toLowerCase().includes(q)) hits.add(link.target);
    }
    return hits;
}

// force-graph swaps link endpoints for node objects once the simulation starts.
export function endpointId(end: unknown): string {
    return typeof end === 'object' && end !== null
        ? (end as { id: string }).id
        : (end as string);
}

function push<K, V>(map: Map<K, V[]>, key: K, value: V) {
    const arr = map.get(key);
    if (arr) arr.push(value);
    else map.set(key, [value]);
}
