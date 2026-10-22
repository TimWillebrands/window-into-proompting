import type { MemoryGraphLink, MemoryGraphNode } from '../../api/model';

export type MemoryNodeKind = 'Event' | 'Concept' | 'Participant';

export type MemoryEdgeKind = 'RECOLLECTS' | 'ABOUT' | 'STANCE';

export interface MemoryGraphData {
    nodes: MemoryGraphNode[];
    links: MemoryGraphLink[];
}

// react-force-graph mutates rendered node objects (x/y/vx/vy). Keep the enriched shape
// distinct from the wire DTO so rendering state never round-trips back to the backend.
export interface EnrichedMemoryNode extends MemoryGraphNode {
    label: string;
    /// <summary>Event only: where it happened, e.g. "Board · Ginkgo".</summary>
    where?: string;
    /// <summary>Participant only: stable per-person hue.</summary>
    colour?: string;
}

export interface EnrichedMemoryLink extends MemoryGraphLink {
    weight?: number | null;
    recallCount?: number | null;
    valence?: number | null;
}

export interface EnrichedMemoryGraph {
    nodes: EnrichedMemoryNode[];
    links: EnrichedMemoryLink[];
}

export interface NameDirectory {
    personas: Array<{ id: string; name: string }>;
    rooms: Array<{ id: string; name: string; kind?: string | null }>;
    boardThreads?: Array<{ id: string; title: string }>;
}
