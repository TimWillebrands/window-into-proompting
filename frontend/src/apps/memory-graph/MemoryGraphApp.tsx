import { useQueryClient } from '@tanstack/react-query';
import {
    lazy,
    Suspense,
    useCallback,
    useEffect,
    useMemo,
    useRef,
    useState,
} from 'react';
import {
    getGetPartiesPartyIdMemoryGraphQueryKey,
    useGetPartiesPartyIdMemoryGraphSuspense,
    useGetPartyIdChatGroupsSuspense,
    useGetPersonaSuspense,
} from '#api/party-zone';
import { ROOT_PARTY_ID } from '../../lib/chat-api';
import { enrichGraph } from './enrichGraph';
import MemoryGraphSidePanel from './MemoryGraphSidePanel';
import type { EnrichedMemoryNode, MemoryGraphData } from './types';

// force-graph touches `window` at module load → blow up under SSR. Defer the
// import until after the component mounts in the browser.
const MemoryGraphCanvas = lazy(() => import('./MemoryGraphCanvas'));

export default function MemoryGraphApp() {
    return (
        <Suspense fallback={<LoadingState />}>
            <MemoryGraphInner />
        </Suspense>
    );
}

function MemoryGraphInner() {
    const partyId = ROOT_PARTY_ID;
    const queryClient = useQueryClient();

    // PRD: snapshot + manual refresh only. Disable focus refetches so window/tab
    // switches don't quietly re-fire the queries while the user inspects the graph.
    const snapshotOptions = {
        query: {
            staleTime: Number.POSITIVE_INFINITY,
            refetchOnWindowFocus: false,
        },
    };
    const graphQuery = useGetPartiesPartyIdMemoryGraphSuspense(
        partyId,
        snapshotOptions,
    );
    const personasQuery = useGetPersonaSuspense(snapshotOptions);
    const chatGroupsQuery = useGetPartyIdChatGroupsSuspense(
        partyId,
        snapshotOptions,
    );

    const enriched = useMemo(() => {
        const raw: MemoryGraphData = (graphQuery.data
            ?.data as MemoryGraphData) ?? { nodes: [], links: [] };
        const personas = (personasQuery.data?.data ?? [])
            .filter((p) => p.id && p.name)
            .map((p) => ({ id: p.id as string, name: p.name as string }));
        const rooms = (chatGroupsQuery.data?.data ?? [])
            .filter((r) => r.id && r.name)
            .map((r) => ({ id: r.id as string, name: r.name as string }));
        return enrichGraph(raw, { personas, rooms });
    }, [graphQuery.data, personasQuery.data, chatGroupsQuery.data]);

    const [selectedId, setSelectedId] = useState<string | null>(null);
    const selectedNode = useMemo<EnrichedMemoryNode | null>(
        () => enriched.nodes.find((n) => n.id === selectedId) ?? null,
        [enriched.nodes, selectedId],
    );

    const refresh = useCallback(() => {
        queryClient.invalidateQueries({
            queryKey: getGetPartiesPartyIdMemoryGraphQueryKey(partyId),
        });
    }, [queryClient, partyId]);

    const isEmpty = enriched.nodes.length === 0;

    const [isClient, setIsClient] = useState(false);
    useEffect(() => {
        setIsClient(true);
    }, []);

    const containerRef = useRef<HTMLDivElement>(null);
    const [canvasSize, setCanvasSize] = useState({ width: 600, height: 400 });
    useEffect(() => {
        const el = containerRef.current;
        if (!el) return;
        const ro = new ResizeObserver((entries) => {
            const entry = entries[0];
            if (!entry) return;
            const { width, height } = entry.contentRect;
            setCanvasSize({
                width: Math.max(200, width),
                height: Math.max(200, height),
            });
        });
        ro.observe(el);
        return () => ro.disconnect();
    }, []);

    return (
        <div
            className="app-surface flex h-full flex-col"
            style={{ background: 'transparent' }}
        >
            <Toolbar onRefresh={refresh} isFetching={graphQuery.isFetching} />
            <div className="flex flex-1 min-h-0">
                <div
                    ref={containerRef}
                    style={{
                        flex: 1,
                        minWidth: 0,
                        position: 'relative',
                        background: '#FAFAF6',
                    }}
                >
                    {isEmpty ? (
                        <EmptyState />
                    ) : isClient ? (
                        <Suspense fallback={<LoadingState />}>
                            <MemoryGraphCanvas
                                graph={enriched}
                                width={canvasSize.width}
                                height={canvasSize.height}
                                selectedNodeId={selectedId}
                                onNodeClick={(node) => setSelectedId(node.id)}
                            />
                        </Suspense>
                    ) : (
                        <LoadingState />
                    )}
                </div>
                <MemoryGraphSidePanel
                    node={selectedNode}
                    graph={enriched}
                    onSelectNode={setSelectedId}
                />
            </div>
        </div>
    );
}

// Mirrors NODE_VISUAL in MemoryGraphCanvas — keep the two in sync.
const LEGEND = [
    { label: 'Persona', colour: '#A684D1' },
    { label: 'Participant', colour: '#E693B3' },
    { label: 'Event', colour: '#F0A04B' },
    { label: 'Concept', colour: '#7CC4A4' },
] as const;

function Toolbar({
    onRefresh,
    isFetching,
}: {
    onRefresh: () => void;
    isFetching: boolean;
}) {
    return (
        <div
            className="xp-glass-panel"
            style={{
                display: 'flex',
                alignItems: 'center',
                gap: 8,
                padding: '4px 8px',
                borderBottom: '1px solid rgba(255,255,255,0.7)',
                fontSize: 11,
                flexWrap: 'wrap',
            }}
        >
            <button
                type="button"
                onClick={onRefresh}
                disabled={isFetching}
                title="Fetch the latest memory snapshot — the view does not update on its own"
            >
                {isFetching ? 'Refreshing…' : 'Refresh'}
            </button>
            <span style={{ display: 'flex', gap: 4, flexWrap: 'wrap' }}>
                {LEGEND.map((entry) => (
                    <span key={entry.label} className="xp-glass-chip">
                        <span
                            className="dot"
                            style={{ background: entry.colour }}
                        />
                        {entry.label}
                    </span>
                ))}
            </span>
            <span style={{ color: '#666', marginLeft: 'auto' }}>
                Click a node to inspect it · scroll to zoom · drag to pan
            </span>
        </div>
    );
}

function LoadingState() {
    return (
        <div
            className="app-surface flex h-full items-center justify-center"
            style={{ background: '#ECE9D8' }}
        >
            <p style={{ color: '#808080', fontSize: 11 }}>Loading memory…</p>
        </div>
    );
}

function EmptyState() {
    return (
        <div
            className="flex h-full items-center justify-center"
            style={{ fontSize: 12, padding: 24 }}
        >
            <div
                className="xp-glass-card"
                style={{
                    maxWidth: 360,
                    padding: '20px 24px',
                    textAlign: 'center',
                }}
            >
                <div style={{ fontSize: 28, marginBottom: 6 }}>🕸️</div>
                <p style={{ fontWeight: 700, margin: '0 0 4px' }}>
                    No memories captured yet
                </p>
                <p style={{ color: '#555', margin: 0 }}>
                    Chat in a room, then use “Remember” on a message. Captured
                    moments show up here as a graph of personas, events and
                    concepts.
                </p>
            </div>
        </div>
    );
}
