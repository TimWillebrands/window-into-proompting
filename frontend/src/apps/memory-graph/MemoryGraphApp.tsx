import { useQueryClient } from '@tanstack/react-query';
import {
    lazy,
    Suspense,
    useCallback,
    useDeferredValue,
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
    useGetWorldsPartyIdBoardSuspense,
} from '#api/party-zone';
import { ROOT_PARTY_ID } from '../../lib/chat-api';
import { enrichGraph } from './enrichGraph';
import {
    buildView,
    type ConceptMode,
    indexGraph,
    searchMatches,
} from './graphView';
import MemoryGraphSidePanel from './MemoryGraphSidePanel';
import { KIND_COLOUR, STANCE_COLOUR } from './palette';
import type { EnrichedMemoryNode, MemoryGraphData } from './types';

// force-graph touches `window` at module load → defer until mounted in the browser.
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

    // Snapshot + manual refresh: no focus refetches while the user inspects the graph.
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
    const boardQuery = useGetWorldsPartyIdBoardSuspense(
        partyId,
        snapshotOptions,
    );

    const graph = useMemo(() => {
        const raw: MemoryGraphData = (graphQuery.data
            ?.data as MemoryGraphData) ?? { nodes: [], links: [] };
        const personas = (personasQuery.data?.data ?? [])
            .filter((p) => p.id && p.name)
            .map((p) => ({ id: p.id as string, name: p.name as string }));
        const rooms = (chatGroupsQuery.data?.data ?? [])
            .filter((r) => r.id && r.name)
            .map((r) => ({
                id: r.id as string,
                name: r.name as string,
                kind: r.kind,
            }));
        const boardThreads = (boardQuery.data?.data ?? [])
            .filter((t) => t.id && t.title)
            .map((t) => ({ id: t.id as string, title: t.title as string }));
        return enrichGraph(raw, { personas, rooms, boardThreads });
    }, [
        graphQuery.data,
        personasQuery.data,
        chatGroupsQuery.data,
        boardQuery.data,
    ]);
    const index = useMemo(() => indexGraph(graph), [graph]);
    const people = useMemo(
        () =>
            graph.nodes
                .filter((n) => n.kind === 'Participant')
                .sort((a, b) => a.label.localeCompare(b.label)),
        [graph.nodes],
    );

    const [lens, setLens] = useState<string | null>(null);
    const [concepts, setConcepts] = useState<ConceptMode>('shared');
    const [showStances, setShowStances] = useState(true);
    const [search, setSearch] = useState('');
    const deferredSearch = useDeferredValue(search);

    const view = useMemo(
        () => buildView(graph, { lens, concepts, showStances }),
        [graph, lens, concepts, showStances],
    );
    const matches = useMemo(
        () => searchMatches(view, deferredSearch),
        [view, deferredSearch],
    );

    const [history, setHistory] = useState<string[]>([]);
    const selectedId = history.at(-1) ?? null;
    const selectNode = useCallback((id: string | null) => {
        setHistory((h) =>
            id === null ? [] : h.at(-1) === id ? h : [...h, id].slice(-30),
        );
    }, []);
    const back = useCallback(() => setHistory((h) => h.slice(0, -1)), []);
    const selectedNode = useMemo<EnrichedMemoryNode | null>(
        () => (selectedId ? (index.byId.get(selectedId) ?? null) : null),
        [index, selectedId],
    );

    const refresh = useCallback(() => {
        queryClient.invalidateQueries({
            queryKey: getGetPartiesPartyIdMemoryGraphQueryKey(partyId),
        });
    }, [queryClient, partyId]);

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

    const counts = useMemo(() => {
        const c = { Event: 0, Concept: 0, Participant: 0, STANCE: 0 };
        for (const n of view.nodes) c[n.kind as keyof typeof c]++;
        for (const l of view.links) if (l.kind === 'STANCE') c.STANCE++;
        return c;
    }, [view]);

    return (
        <div
            className="app-surface flex h-full flex-col"
            style={{ background: 'transparent' }}
        >
            <div className="xp-glass-panel" style={toolbarStyle}>
                <button
                    type="button"
                    onClick={refresh}
                    disabled={graphQuery.isFetching}
                    title="Fetch the latest memory snapshot — the view does not update on its own"
                >
                    {graphQuery.isFetching ? 'Refreshing…' : 'Refresh'}
                </button>
                <label style={fieldStyle}>
                    Memories of
                    <select
                        value={lens ?? ''}
                        onChange={(e) => setLens(e.target.value || null)}
                    >
                        <option value="">everyone</option>
                        {people.map((p) => (
                            <option key={p.id} value={p.id}>
                                {p.label}
                            </option>
                        ))}
                    </select>
                </label>
                <label style={fieldStyle}>
                    Topics
                    <select
                        value={concepts}
                        onChange={(e) =>
                            setConcepts(e.target.value as ConceptMode)
                        }
                        title="One-off topics are hidden by default — they only ever touch a single memory"
                    >
                        <option value="shared">recurring (2+)</option>
                        <option value="all">all</option>
                        <option value="none">hidden</option>
                    </select>
                </label>
                <label style={fieldStyle}>
                    <input
                        type="checkbox"
                        checked={showStances}
                        onChange={(e) => setShowStances(e.target.checked)}
                    />
                    Stances
                </label>
                <input
                    type="search"
                    value={search}
                    onChange={(e) => setSearch(e.target.value)}
                    placeholder="Search memories…"
                    style={{ width: 160 }}
                />
                <span style={{ color: '#555', marginLeft: 'auto' }}>
                    {counts.Participant} people · {counts.Event} memories ·{' '}
                    {counts.Concept} topics · {counts.STANCE} stances
                    {matches ? ` · ${matches.size} matches` : ''}
                </span>
            </div>
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
                    {graph.nodes.length === 0 ? (
                        <EmptyState />
                    ) : isClient ? (
                        <Suspense fallback={<LoadingState />}>
                            <MemoryGraphCanvas
                                view={view}
                                lens={lens}
                                width={canvasSize.width}
                                height={canvasSize.height}
                                selectedNodeId={selectedId}
                                matches={matches}
                                onNodeClick={(node) => selectNode(node.id)}
                                onBackgroundClick={() => selectNode(null)}
                            />
                            <Legend
                                people={view.nodes.filter(
                                    (n) => n.kind === 'Participant',
                                )}
                                lens={lens}
                                onLens={setLens}
                            />
                        </Suspense>
                    ) : (
                        <LoadingState />
                    )}
                </div>
                <MemoryGraphSidePanel
                    node={selectedNode}
                    index={index}
                    onSelectNode={selectNode}
                    onBack={history.length > 1 ? back : undefined}
                    lens={lens}
                    onLens={setLens}
                />
            </div>
        </div>
    );
}

function Legend({
    people,
    lens,
    onLens,
}: {
    people: EnrichedMemoryNode[];
    lens: string | null;
    onLens: (id: string | null) => void;
}) {
    return (
        <div className="xp-glass-card" style={legendStyle}>
            {people.map((p) => (
                <button
                    key={p.id}
                    type="button"
                    onClick={() => onLens(lens === p.id ? null : p.id)}
                    title={
                        lens === p.id
                            ? 'Show everyone'
                            : `Only ${p.label}'s memories`
                    }
                    style={legendButtonStyle}
                >
                    <LegendRow label={p.label}>
                        <Dot colour={p.colour} size={10} />
                    </LegendRow>
                </button>
            ))}
            {lens ? (
                <button
                    type="button"
                    onClick={() => onLens(null)}
                    style={legendButtonStyle}
                >
                    <LegendRow label="← everyone">{null}</LegendRow>
                </button>
            ) : null}
            <div style={{ height: 4 }} />
            <LegendRow label="memory — ring = who remembers it">
                <Dot colour={KIND_COLOUR.Event} ring="#7B5CC4" />
            </LegendRow>
            <LegendRow label="topic — bigger = more memories">
                <Dot colour={KIND_COLOUR.Concept} />
            </LegendRow>
            <LegendRow label="likes / dislikes">
                <span style={{ color: STANCE_COLOUR.warm, fontWeight: 700 }}>
                    →
                </span>
                <span style={{ color: STANCE_COLOUR.cold, fontWeight: 700 }}>
                    →
                </span>
            </LegendRow>
        </div>
    );
}

function LegendRow({
    label,
    children,
}: {
    label: string;
    children: React.ReactNode;
}) {
    return (
        <div style={{ display: 'flex', alignItems: 'center', gap: 6 }}>
            <span
                style={{
                    width: 22,
                    display: 'inline-flex',
                    justifyContent: 'center',
                }}
            >
                {children}
            </span>
            {label}
        </div>
    );
}

function Dot({
    colour,
    size = 8,
    ring,
}: {
    colour?: string;
    size?: number;
    ring?: string;
}) {
    return (
        <span
            style={{
                display: 'inline-block',
                width: size,
                height: size,
                borderRadius: '50%',
                background: colour,
                boxShadow: ring ? `0 0 0 2px ${ring}` : undefined,
            }}
        />
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
                    moments show up here as a graph of people, memories and
                    topics.
                </p>
            </div>
        </div>
    );
}

const toolbarStyle: React.CSSProperties = {
    display: 'flex',
    alignItems: 'center',
    gap: 10,
    padding: '4px 8px',
    borderBottom: '1px solid rgba(255,255,255,0.7)',
    fontSize: 11,
    flexWrap: 'wrap',
};

const fieldStyle: React.CSSProperties = {
    display: 'flex',
    alignItems: 'center',
    gap: 4,
};

const legendStyle: React.CSSProperties = {
    position: 'absolute',
    left: 8,
    bottom: 8,
    padding: '6px 8px',
    fontSize: 10,
    display: 'flex',
    flexDirection: 'column',
    gap: 2,
};

const legendButtonStyle: React.CSSProperties = {
    background: 'none',
    border: 'none',
    boxShadow: 'none',
    padding: 0,
    minWidth: 0,
    minHeight: 0,
    textAlign: 'left',
    cursor: 'pointer',
    fontSize: 10,
};
