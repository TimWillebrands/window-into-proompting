import {
    CheckBox,
    ComboBox,
    FieldRow,
    GroupBox,
    Icon,
    ProgressBar,
    Stack,
    StatusBar,
    StatusBarField,
    Swatch,
    TaskPaneLink,
    TaskPaneSection,
    Text,
    TextBox,
    Toolbar,
    ToolbarButton,
    WindowLayout,
} from '@partytown/ui';
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
import { WORLD_ID } from '../../lib/world';
import { enrichGraph } from './enrichGraph';
import {
    buildView,
    type ConceptMode,
    indexGraph,
    searchMatches,
} from './graphView';
import MemoryGraphSidePanel from './MemoryGraphSidePanel';
import { KIND_COLOUR, PERSON_PALETTE, STANCE_COLOUR } from './palette';
import type { EnrichedMemoryNode, MemoryGraphData } from './types';

const MemoryGraphCanvas = lazy(() => import('./MemoryGraphCanvas'));

export default function MemoryGraphApp() {
    return (
        <Suspense fallback={<LoadingState />}>
            <MemoryGraphInner />
        </Suspense>
    );
}

function MemoryGraphInner() {
    const partyId = WORLD_ID;
    const queryClient = useQueryClient();

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
        <WindowLayout
            scroll="none"
            contentLabel="Memory graph"
            detailsLabel="Inspector"
            detailsWidth="wide"
            detailsPane={
                <MemoryGraphSidePanel
                    node={selectedNode}
                    index={index}
                    onSelectNode={selectNode}
                    onBack={history.length > 1 ? back : undefined}
                    lens={lens}
                    onLens={setLens}
                >
                    {graph.nodes.length > 0 ? (
                        <Legend
                            people={view.nodes.filter(
                                (n) => n.kind === 'Participant',
                            )}
                            lens={lens}
                            onLens={setLens}
                        />
                    ) : null}
                </MemoryGraphSidePanel>
            }
            toolbar={
                <Stack
                    direction="row"
                    wrap
                    align="center"
                    gap="lg"
                    rowGap="xs"
                    className="pe-2"
                >
                    <Toolbar aria-label="Memory Graph">
                        <ToolbarButton
                            icon={<Icon name="refresh" />}
                            onClick={refresh}
                            disabled={graphQuery.isFetching}
                            title="Fetch the latest memory snapshot — the view does not update on its own"
                        >
                            {graphQuery.isFetching ? 'Refreshing…' : 'Refresh'}
                        </ToolbarButton>
                    </Toolbar>
                    <FieldRow label="Memories of" labelWidth="auto">
                        <ComboBox
                            value={lens ?? ''}
                            onChange={(e) => setLens(e.target.value || null)}
                            options={[
                                { value: '', label: 'everyone' },
                                ...people.map((p) => ({
                                    value: p.id,
                                    label: p.label,
                                })),
                            ]}
                        />
                    </FieldRow>
                    <FieldRow label="Topics" labelWidth="auto">
                        <ComboBox
                            value={concepts}
                            onChange={(e) =>
                                setConcepts(e.target.value as ConceptMode)
                            }
                            title="One-off topics are hidden by default — they only ever touch a single memory"
                            options={[
                                { value: 'shared', label: 'recurring (2+)' },
                                { value: 'all', label: 'all' },
                                { value: 'none', label: 'hidden' },
                            ]}
                        />
                    </FieldRow>
                    <CheckBox
                        checked={showStances}
                        onChange={(e) => setShowStances(e.target.checked)}
                    >
                        Stances
                    </CheckBox>
                    <TextBox
                        type="search"
                        aria-label="Search memories"
                        value={search}
                        onChange={(e) => setSearch(e.target.value)}
                        placeholder="Search memories…"
                        className="w-40"
                    />
                </Stack>
            }
            statusBar={
                <StatusBar>
                    <StatusBarField>{counts.Participant} people</StatusBarField>
                    <StatusBarField>{counts.Event} memories</StatusBarField>
                    <StatusBarField>{counts.Concept} topics</StatusBarField>
                    <StatusBarField grow>
                        {counts.STANCE} stances
                        {matches ? ` · ${matches.size} matches` : ''}
                    </StatusBarField>
                </StatusBar>
            }
        >
            <div ref={containerRef} className="relative">
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
                    </Suspense>
                ) : (
                    <LoadingState />
                )}
            </div>
        </WindowLayout>
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
        <TaskPaneSection title="Key">
            {people.map((p) => (
                <TaskPaneLink
                    key={p.id}
                    icon={<Swatch colour={p.colour} shape="dot" />}
                    aria-current={lens === p.id ? 'true' : undefined}
                    onClick={() => onLens(lens === p.id ? null : p.id)}
                    title={
                        lens === p.id
                            ? 'Show everyone'
                            : `Only ${p.label}'s memories`
                    }
                >
                    {p.label}
                </TaskPaneLink>
            ))}
            {lens ? (
                <TaskPaneLink
                    icon={<Icon name="users" />}
                    onClick={() => onLens(null)}
                >
                    Everyone
                </TaskPaneLink>
            ) : null}
            <KeyRow label="memory — ring = who remembers it">
                <Swatch
                    colour={KIND_COLOUR.Event}
                    ring={PERSON_PALETTE[0]}
                    shape="dot"
                    size="small"
                />
            </KeyRow>
            <KeyRow label="topic — bigger = more memories">
                <Swatch colour={KIND_COLOUR.Concept} shape="dot" />
            </KeyRow>
            <KeyRow label="likes / dislikes">
                <Swatch colour={STANCE_COLOUR.warm} size="small" />
                <Swatch colour={STANCE_COLOUR.cold} size="small" />
            </KeyRow>
        </TaskPaneSection>
    );
}

function KeyRow({
    label,
    children,
}: {
    label: string;
    children: React.ReactNode;
}) {
    return (
        <Text as="div" tone="muted">
            <Stack direction="row" align="center" gap="md">
                <Stack
                    as="span"
                    inline
                    direction="row"
                    justify="center"
                    gap="xxs"
                    className="w-6"
                >
                    {children}
                </Stack>
                {label}
            </Stack>
        </Text>
    );
}

function LoadingState() {
    return (
        <Stack
            align="center"
            justify="center"
            gap="md"
            pad="xl"
            className="h-full"
        >
            <ProgressBar marquee label="Loading memory" className="w-40" />
            <Text as="p" tone="muted">
                Loading memory…
            </Text>
        </Stack>
    );
}

function EmptyState() {
    return (
        <Stack align="center" justify="center" pad="xl" className="h-full">
            <GroupBox
                title="No memories captured yet"
                icon={<Icon name="memory-graph" />}
                className="max-w-[360px]"
            >
                <Text as="p" tone="muted">
                    Chat in a room, then use “Remember” on a message. Captured
                    moments show up here as a graph of people, memories and
                    topics.
                </Text>
            </GroupBox>
        </Stack>
    );
}
