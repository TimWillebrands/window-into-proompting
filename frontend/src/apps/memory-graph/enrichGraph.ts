import type {
    EnrichedMemoryGraph,
    EnrichedMemoryNode,
    MemoryGraphData,
    NameDirectory,
} from './types';

// Backend stores ids — Personas / Rooms live in EF/Orleans. Resolving display names here
// keeps the memory subsystem from growing a cross-grain query layer just for the debug
// viz. Pure function: caller hands in reference data; we walk nodes once and attach a
// `label`. Unknown node kinds pass through with a fallback label so future backend
// additions stay visible without a frontend change.
export function enrichGraph(
    graph: MemoryGraphData,
    directory: NameDirectory,
): EnrichedMemoryGraph {
    const personasById = new Map(directory.personas.map((p) => [p.id, p.name]));
    const roomsById = new Map(directory.rooms.map((r) => [r.id, r.name]));

    const nodes: EnrichedMemoryNode[] = graph.nodes.map((node) => {
        switch (node.kind) {
            case 'Event': {
                // Room/Message vertices are gone; the Event carries roomId as a property.
                // Label it with the Room name so the viz still shows where it happened —
                // the body lives in the hover/side-panel (description), never the label.
                const roomId = node.roomId ?? undefined;
                const name = roomId ? roomsById.get(roomId) : undefined;
                return {
                    ...node,
                    label: name ?? fallback('Room', roomId),
                };
            }
            case 'Persona': {
                const personaId = stripPrefix(node.id, 'persona:');
                const name = personaId
                    ? personasById.get(personaId)
                    : undefined;
                return {
                    ...node,
                    label: name ?? fallback('Persona', personaId),
                };
            }
            case 'Participant': {
                // A Participant is a Persona wearing a Party-scoped hat; the Persona name
                // is the right display. Id grammar: `part:<personaGuid>:<partyGuid>`.
                const rest = stripPrefix(node.id, 'part:');
                const personaId = rest?.split(':')[0];
                const name = personaId
                    ? personasById.get(personaId)
                    : undefined;
                return {
                    ...node,
                    label: name ?? fallback('Participant', personaId),
                };
            }
            case 'Concept': {
                const fallbackName =
                    stripPrefix(node.id, 'concept:') ?? node.id;
                return {
                    ...node,
                    label: node.display ?? fallbackName,
                };
            }
            default:
                return { ...node, label: node.id };
        }
    });

    return { nodes, links: graph.links };
}

function stripPrefix(
    id: string | undefined,
    prefix: string,
): string | undefined {
    if (!id) return undefined;
    return id.startsWith(prefix) ? id.slice(prefix.length) : undefined;
}

function fallback(kind: string, id: string | undefined): string {
    if (!id) return kind;
    return `${kind}(${id.slice(0, 8)})`;
}
