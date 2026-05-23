import type {
    EnrichedMemoryGraph,
    EnrichedMemoryNode,
    MemoryGraphData,
    NameDirectory,
} from './types';

// Backend stores ids — Personas/Rooms/Messages live in EF/Orleans. Resolving display
// names here keeps the memory subsystem from growing a cross-grain query layer just for
// this debug viz (PRD: "display-name enrichment is client-side via the existing
// Personas / Parties / Rooms / Messages API hooks").
//
// Pure function: no React, no fetching, no module-level state. The caller hands in the
// reference data; we walk nodes once and attach a `label` plus, for Messages, an
// `authorName`. Unknown node kinds are passed through with a fallback label (the raw id)
// so future backend additions stay visible without a frontend change.
export function enrichGraph(
    graph: MemoryGraphData,
    directory: NameDirectory,
): EnrichedMemoryGraph {
    const personasById = new Map(directory.personas.map((p) => [p.id, p.name]));
    const roomsById = new Map(directory.rooms.map((r) => [r.id, r.name]));
    const messagesById = new Map(
        directory.messages.map((m) => [m.id, m] as const),
    );

    const nodes: EnrichedMemoryNode[] = graph.nodes.map((node) => {
        switch (node.kind) {
            case 'Room': {
                const roomId = stripPrefix(node.id, 'room:');
                const name = roomId ? roomsById.get(roomId) : undefined;
                return { ...node, label: name ?? fallback('Room', roomId) };
            }
            case 'Persona': {
                const personaId = stripPrefix(node.id, 'persona:');
                const name = personaId ? personasById.get(personaId) : undefined;
                return {
                    ...node,
                    label: name ?? fallback('Persona', personaId),
                };
            }
            case 'Participant': {
                // Participant id is `part:<personaGuid>:<partyGuid>` — the persona name
                // is the right display, since a Participant in this Party is a Persona
                // wearing a Party-scoped hat.
                const rest = stripPrefix(node.id, 'part:');
                const personaId = rest?.split(':')[0];
                const name = personaId ? personasById.get(personaId) : undefined;
                return {
                    ...node,
                    label: name ?? fallback('Participant', personaId),
                };
            }
            case 'Concept': {
                // Backend emits display when set; fall back to the canonical name.
                const fallbackName = stripPrefix(node.id, 'concept:') ?? node.id;
                return {
                    ...node,
                    label: node.display ?? fallbackName,
                };
            }
            case 'Message': {
                const rest = stripPrefix(node.id, 'msg:');
                const parts = rest?.split(':');
                const msgIdRaw = parts?.[1];
                const msgId = msgIdRaw ? Number(msgIdRaw) : Number.NaN;
                const msg = Number.isFinite(msgId)
                    ? messagesById.get(msgId)
                    : undefined;
                return {
                    ...node,
                    // Hard requirement from the PRD: Message labels are `#<id>` ONLY,
                    // never the message body. The body would drown the canvas.
                    label: Number.isFinite(msgId) ? `#${msgId}` : node.id,
                    authorName: msg?.authorName,
                };
            }
            case 'Event':
                // Events are labelled on hover by description; the canvas leaves the
                // label off entirely (visual encoding handled by MemoryGraphCanvas).
                return { ...node, label: node.description ?? '' };
            default:
                // Unknown kinds pass through; reader still gets the raw id as label
                // so the node isn't anonymous on the canvas.
                return { ...node, label: node.id };
        }
    });

    return { nodes, links: graph.links };
}

function stripPrefix(id: string | undefined, prefix: string): string | undefined {
    if (!id) return undefined;
    return id.startsWith(prefix) ? id.slice(prefix.length) : undefined;
}

function fallback(kind: string, id: string | undefined): string {
    if (!id) return kind;
    return `${kind}(${id.slice(0, 8)})`;
}
