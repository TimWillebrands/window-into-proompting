import type { MemoryGraphLink, MemoryGraphNode } from '../../api/model';

export type MemoryNodeKind =
    | 'Room'
    | 'Message'
    | 'Event'
    | 'Concept'
    | 'Participant'
    | 'Persona'
    | string;

export type MemoryEdgeKind =
    | 'RECOLLECTS'
    | 'ABOUT'
    | 'ANCHORED_TO'
    | 'HAS_PARTICIPANT'
    | string;

export interface MemoryGraphData {
    nodes: MemoryGraphNode[];
    links: MemoryGraphLink[];
}

// React-force-graph mutates the node objects it renders (adds x/y/vx/vy fields).
// Keep our enriched node shape distinct from the wire DTO so consumers never accidentally
// send rendering state back to the backend.
export interface EnrichedMemoryNode extends MemoryGraphNode {
    label: string;
    authorName?: string;
}

export interface EnrichedMemoryGraph {
    nodes: EnrichedMemoryNode[];
    links: MemoryGraphLink[];
}

export interface NameDirectory {
    personas: Array<{ id: string; name: string }>;
    rooms: Array<{ id: string; name: string }>;
    messages: Array<{ id: number; body?: string; authorName?: string }>;
}
