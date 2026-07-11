import { useMemo } from 'react';
import type { MemoryGraphLink } from '../../api/model';
import type { EnrichedMemoryGraph, EnrichedMemoryNode } from './types';

interface MemoryGraphSidePanelProps {
    node: EnrichedMemoryNode | null;
    graph: EnrichedMemoryGraph;
    onSelectNode: (id: string) => void;
}

export default function MemoryGraphSidePanel({
    node,
    graph,
    onSelectNode,
}: MemoryGraphSidePanelProps) {
    const nodeById = useMemo(
        () => new Map(graph.nodes.map((n) => [n.id, n])),
        [graph.nodes],
    );

    const { outgoing, incoming } = useMemo(() => {
        const out: MemoryGraphLink[] = [];
        const inc: MemoryGraphLink[] = [];
        if (!node) return { outgoing: out, incoming: inc };
        for (const link of graph.links) {
            if (link.source === node.id) out.push(link);
            else if (link.target === node.id) inc.push(link);
        }
        return { outgoing: out, incoming: inc };
    }, [node, graph.links]);

    if (!node) {
        return (
            <div className="xp-glass-panel" style={panelStyle}>
                <div style={{ padding: '14px 12px', fontSize: 11 }}>
                    <p style={{ fontWeight: 700, margin: '0 0 4px' }}>
                        Inspector
                    </p>
                    <p style={{ color: '#666', margin: 0 }}>
                        Click a node in the graph to see its details and how it
                        connects to the rest of the memory.
                    </p>
                </div>
            </div>
        );
    }

    const fields: Array<
        [string, string | null | undefined, React.CSSProperties?]
    > = [
        ['Label', node.label],
        ['Id', node.id, { wordBreak: 'break-all' }],
        ['Description', node.description],
        ['Display', node.display],
        ['Created', node.createdAt],
        // Event anchor (formerly a Message vertex), now properties on the Event node.
        ['Room', node.roomId, { wordBreak: 'break-all' }],
        [
            'Anchor message',
            node.anchorMessageId != null ? `#${node.anchorMessageId}` : null,
        ],
    ];

    return (
        <div className="xp-glass-panel" style={panelStyle}>
            <SectionHeader>{node.kind}</SectionHeader>
            <div style={{ padding: 8, fontSize: 11 }}>
                {fields.map(([k, v, style]) =>
                    v ? (
                        <div key={k} style={style}>
                            <strong>{k}:</strong> {v}
                        </div>
                    ) : null,
                )}
            </div>

            <EdgeList
                title="Points to"
                links={outgoing}
                endpointKey="target"
                nodeById={nodeById}
                onSelectNode={onSelectNode}
            />
            <EdgeList
                title="Referenced by"
                links={incoming}
                endpointKey="source"
                nodeById={nodeById}
                onSelectNode={onSelectNode}
            />
        </div>
    );
}

function EdgeList({
    title,
    links,
    endpointKey,
    nodeById,
    onSelectNode,
}: {
    title: string;
    links: MemoryGraphLink[];
    endpointKey: 'source' | 'target';
    nodeById: Map<string, EnrichedMemoryNode>;
    onSelectNode: (id: string) => void;
}) {
    const grouped = useMemo(() => {
        const m = new Map<string, MemoryGraphLink[]>();
        for (const link of links) {
            const arr = m.get(link.kind) ?? [];
            arr.push(link);
            m.set(link.kind, arr);
        }
        return [...m.entries()];
    }, [links]);

    if (grouped.length === 0) {
        return (
            <>
                <SectionHeader>{title}</SectionHeader>
                <p style={{ color: '#808080', padding: 8, fontSize: 11 }}>
                    (none)
                </p>
            </>
        );
    }

    return (
        <>
            <SectionHeader>{title}</SectionHeader>
            <ul
                style={{
                    listStyle: 'none',
                    margin: 0,
                    padding: 0,
                    fontSize: 11,
                }}
            >
                {grouped.map(([kind, edges]) => (
                    <li key={kind} style={{ marginBottom: 4 }}>
                        <div
                            style={{
                                background: 'rgba(255,255,255,0.65)',
                                boxShadow:
                                    'inset 0 1px 0 rgba(255,255,255,0.8)',
                                padding: '2px 6px',
                                fontWeight: 600,
                            }}
                        >
                            {kind} ({edges.length})
                        </div>
                        <ul
                            style={{ listStyle: 'none', margin: 0, padding: 0 }}
                        >
                            {edges.map((edge, i) => {
                                const otherId = edge[endpointKey];
                                const other = nodeById.get(otherId);
                                return (
                                    <li
                                        key={`${edge.source}-${edge.target}-${edge.kind}-${i}`}
                                    >
                                        <button
                                            type="button"
                                            onClick={() =>
                                                onSelectNode(otherId)
                                            }
                                            style={{
                                                background: 'none',
                                                border: 'none',
                                                padding: '2px 8px',
                                                width: '100%',
                                                textAlign: 'left',
                                                cursor: 'pointer',
                                                color: '#003399',
                                                textDecoration: 'underline',
                                            }}
                                        >
                                            {other
                                                ? `${other.kind}: ${other.label}`
                                                : otherId}
                                        </button>
                                        {edge.snippet ? (
                                            <div
                                                style={{
                                                    padding: '0 8px 4px 20px',
                                                    color: '#555',
                                                    fontStyle: 'italic',
                                                }}
                                            >
                                                “{edge.snippet}”
                                            </div>
                                        ) : null}
                                    </li>
                                );
                            })}
                        </ul>
                    </li>
                ))}
            </ul>
        </>
    );
}

function SectionHeader({ children }: { children: React.ReactNode }) {
    return (
        <div
            style={{
                background:
                    'linear-gradient(to bottom, #0058E1 0%, #002C77 100%)',
                boxShadow: 'inset 0 1px 0 rgba(255,255,255,0.35)',
                color: 'white',
                fontSize: 11,
                fontWeight: 600,
                padding: '3px 6px',
            }}
        >
            {children}
        </div>
    );
}

const panelStyle: React.CSSProperties = {
    width: 240,
    borderLeft: '1px solid rgba(255,255,255,0.7)',
    overflowY: 'auto',
    height: '100%',
};
