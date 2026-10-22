import { PERSON_PALETTE } from './palette';
import type {
    EnrichedMemoryGraph,
    EnrichedMemoryLink,
    EnrichedMemoryNode,
    MemoryGraphData,
    NameDirectory,
} from './types';

const SOURCE_LABEL: Record<string, string> = {
    rooms: 'Room',
    messenger: 'DM',
    places: 'Place',
    board: 'Board',
    world: 'World',
    import: 'Import',
};

const ROOM_KIND_SOURCE: Record<string, string> = {
    room: 'rooms',
    dm: 'messenger',
    place: 'places',
};

export function sourceLabel(source: string | null | undefined): string {
    return (source && SOURCE_LABEL[source]) ?? 'Room';
}

// Backend stores ids; names come from REST. Legacy Events lacking `source` get one inferred.
export function enrichGraph(
    graph: MemoryGraphData,
    directory: NameDirectory,
): EnrichedMemoryGraph {
    const personasById = new Map(directory.personas.map((p) => [p.id, p.name]));
    const roomsById = new Map(directory.rooms.map((r) => [r.id, r]));
    const threadsById = new Map(
        (directory.boardThreads ?? []).map((t) => [t.id, t.title]),
    );

    const participantIds = graph.nodes
        .filter((n) => n.kind === 'Participant')
        .map((n) => n.id)
        .sort();
    const colourById = new Map(
        participantIds.map((id, i) => [
            id,
            PERSON_PALETTE[i % PERSON_PALETTE.length],
        ]),
    );

    const nodes: EnrichedMemoryNode[] = graph.nodes.map((node) => {
        switch (node.kind) {
            case 'Event': {
                const roomId = node.roomId ?? undefined;
                const room = roomId ? roomsById.get(roomId) : undefined;
                const thread = roomId ? threadsById.get(roomId) : undefined;
                const source =
                    node.source ??
                    (thread
                        ? 'board'
                        : room?.kind
                          ? ROOM_KIND_SOURCE[room.kind]
                          : undefined);
                const name =
                    thread ??
                    room?.name ??
                    (source === 'world' ? 'happening' : undefined);
                const where = `${sourceLabel(source)} · ${name ?? shortId(roomId)}`;
                return { ...node, source, label: where, where };
            }
            case 'Participant': {
                // Id grammar: `part:<personaGuid>:<partyGuid>`.
                const personaId = stripPrefix(node.id, 'part:')?.split(':')[0];
                const name = personaId
                    ? personasById.get(personaId)
                    : undefined;
                return {
                    ...node,
                    label: name ?? fallback('Participant', personaId),
                    colour: colourById.get(node.id),
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

    // Generated DTO types decimals as `number | string`; normalise once here.
    const links: EnrichedMemoryLink[] = graph.links.map((link) => ({
        ...link,
        weight: toNumber(link.weight),
        recallCount: toNumber(link.recallCount),
        valence: toNumber(link.valence),
    }));

    return { nodes, links };
}

function toNumber(v: number | string | null | undefined): number | null {
    if (v === null || v === undefined || v === '') return null;
    const n = typeof v === 'number' ? v : Number(v);
    return Number.isFinite(n) ? n : null;
}

function stripPrefix(
    id: string | undefined,
    prefix: string,
): string | undefined {
    if (!id) return undefined;
    return id.startsWith(prefix) ? id.slice(prefix.length) : undefined;
}

function shortId(id: string | undefined): string {
    return id ? id.slice(0, 8) : '?';
}

function fallback(kind: string, id: string | undefined): string {
    if (!id) return kind;
    return `${kind}(${id.slice(0, 8)})`;
}
