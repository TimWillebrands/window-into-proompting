import type { MemoryGraphLink, MemoryGraphNode } from '../../api/model';

export type MemoryNodeKind =
    | 'Room'
    | 'Message'
    | 'Event'
    | 'Concept'
    | 'Participant'
    | 'Persona';

export type MemoryEdgeKind =
    | 'RECOLLECTS'
    | 'ABOUT'
    | 'ANCHORED_TO'
    | 'HAS_PARTICIPANT';

export interface MemoryGraphData {
    nodes: MemoryGraphNode[];
    links: MemoryGraphLink[];
}

// react-force-graph mutates rendered node objects (x/y/vx/vy). Keep the enriched shape
// distinct from the wire DTO so rendering state never round-trips back to the backend.
export interface EnrichedMemoryNode extends MemoryGraphNode {
    label: string;
}

export interface EnrichedMemoryGraph {
    nodes: EnrichedMemoryNode[];
    links: MemoryGraphLink[];
}

export interface NameDirectory {
    personas: Array<{ id: string; name: string }>;
    rooms: Array<{ id: string; name: string }>;
}
