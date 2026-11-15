import type { MemoryGraphLink, MemoryGraphNode } from '../../api/model';

export type MemoryNodeKind = 'Event' | 'Concept' | 'Participant';

export type MemoryEdgeKind = 'RECOLLECTS' | 'ABOUT' | 'STANCE';

export interface MemoryGraphData {
    nodes: MemoryGraphNode[];
    links: MemoryGraphLink[];
}

export interface EnrichedMemoryNode extends MemoryGraphNode {
    label: string;
    where?: string;
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
