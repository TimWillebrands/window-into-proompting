import { useMemo } from 'react';
import type { MemoryGraphLink } from '../../api/model';
import type { EnrichedMemoryNode } from './types';

interface MemoryGraphSidePanelProps {
    node: EnrichedMemoryNode | null;
    allNodes: EnrichedMemoryNode[];
    allLinks: MemoryGraphLink[];
    onSelectNode: (id: string) => void;
}

// Read-only inspection panel. Groups in/out edges by kind so the user can navigate the
// graph relationally instead of squinting at the canvas (PRD user-story #13–14).
export default function MemoryGraphSidePanel({
    node,
    allNodes,
    allLinks,
    onSelectNode,
}: MemoryGraphSidePanelProps) {
    const nodeById = useMemo(
        () => new Map(allNodes.map((n) => [n.id, n])),
        [allNodes],
    );

    const { outgoing, incoming } = useMemo(() => {
        const out: MemoryGraphLink[] = [];
        const inc: MemoryGraphLink[] = [];
        if (!node) return { outgoing: out, incoming: inc };
        for (const link of allLinks) {
            if (link.source === node.id) out.push(link);
            else if (link.target === node.id) inc.push(link);
        }
        return { outgoing: out, incoming: inc };
    }, [node, allLinks]);

    if (!node) {
        return (
            <div style={panelStyle}>
                <p style={{ color: '#808080', padding: 8, fontSize: 11 }}>
                    Click a node to inspect.
                </p>
            </div>
        );
    }

    return (
        <div style={panelStyle}>
            <SectionHeader>{node.kind}</SectionHeader>
            <div style={{ padding: 8, fontSize: 11 }}>
                <div>
                    <strong>label:</strong> {node.label}
                </div>
                <div style={{ wordBreak: 'break-all' }}>
                    <strong>id:</strong> {node.id}
                </div>
                {node.description ? (
                    <div>
                        <strong>description:</strong> {node.description}
                    </div>
                ) : null}
                {node.display ? (
                    <div>
                        <strong>display:</strong> {node.display}
                    </div>
                ) : null}
                {node.createdAt ? (
                    <div>
                        <strong>created_at:</strong> {node.createdAt}
                    </div>
                ) : null}
                {node.authorName ? (
                    <div>
                        <strong>author:</strong> {node.authorName}
                    </div>
                ) : null}
            </div>

            <EdgeList
                title="Outgoing"
                links={outgoing}
                endpointKey="target"
                nodeById={nodeById}
                onSelectNode={onSelectNode}
            />
            <EdgeList
                title="Incoming"
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
                                background: '#F1EFE2',
                                padding: '2px 6px',
                                fontWeight: 600,
                            }}
                        >
                            {kind} ({edges.length})
                        </div>
                        <ul style={{ listStyle: 'none', margin: 0, padding: 0 }}>
                            {edges.map((edge, i) => {
                                const otherId = edge[endpointKey];
                                const other = nodeById.get(otherId);
                                return (
                                    <li
                                        key={`${edge.source}-${edge.target}-${edge.kind}-${i}`}
                                    >
                                        <button
                                            type="button"
                                            onClick={() => onSelectNode(otherId)}
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
    borderLeft: '1px solid #ACA899',
    background: '#ECE9D8',
    overflowY: 'auto',
    height: '100%',
};
