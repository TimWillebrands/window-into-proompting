import {
    CommandButton,
    Icon,
    Meter,
    Swatch,
    TaskPane,
    TaskPaneLink,
    TaskPaneSection,
    Text,
} from '@partytown/ui';
import { type ReactNode, useMemo, useState } from 'react';
import { endpointId, type GraphIndex } from './graphView';
import { KIND_COLOUR } from './palette';
import type { EnrichedMemoryLink, EnrichedMemoryNode } from './types';

interface MemoryGraphSidePanelProps {
    node: EnrichedMemoryNode | null;
    index: GraphIndex;
    onSelectNode: (id: string) => void;
    onBack?: () => void;
    lens: string | null;
    onLens: (participantId: string | null) => void;
    children?: ReactNode;
}

const MEMORY_PAGE = 40;

export default function MemoryGraphSidePanel({
    node,
    index,
    onSelectNode,
    onBack,
    lens,
    onLens,
    children,
}: MemoryGraphSidePanelProps) {
    const ctx: PanelCtx | null = node ? { node, index, onSelectNode } : null;
    return (
        <TaskPane className="h-full w-[300px] flex-none" aria-label="Inspector">
            {!ctx ? (
                <TaskPaneSection title="Inspector" special collapsible={false}>
                    <Text as="p" tone="muted">
                        Click a person to read what they remember and how they
                        feel about others. Click a memory or topic to see who
                        shares it.
                    </Text>
                    <Text as="p" tone="muted">
                        People sit on the outer ring; memories shared by several
                        people drift to the middle.
                    </Text>
                </TaskPaneSection>
            ) : ctx.node.kind === 'Participant' ? (
                <PersonPanel
                    {...ctx}
                    onBack={onBack}
                    lens={lens}
                    onLens={onLens}
                />
            ) : ctx.node.kind === 'Event' ? (
                <EventPanel {...ctx} onBack={onBack} />
            ) : (
                <ConceptPanel {...ctx} onBack={onBack} />
            )}
            {children}
        </TaskPane>
    );
}

function Lead({
    title,
    onBack,
    children,
}: {
    title: string;
    onBack?: () => void;
    children: ReactNode;
}) {
    return (
        <TaskPaneSection title={title} special collapsible={false}>
            {onBack ? (
                <TaskPaneLink icon={<Icon name="back" />} onClick={onBack}>
                    Back
                </TaskPaneLink>
            ) : null}
            {children}
        </TaskPaneSection>
    );
}

interface PanelCtx {
    node: EnrichedMemoryNode;
    index: GraphIndex;
    onSelectNode: (id: string) => void;
    onBack?: () => void;
}

function PersonPanel({
    node,
    index,
    onSelectNode,
    onBack,
    lens,
    onLens,
}: PanelCtx & {
    lens: string | null;
    onLens: (id: string | null) => void;
}) {
    const [showAll, setShowAll] = useState(false);
    const outgoing = index.outgoing.get(node.id) ?? [];
    const memories = useMemo(
        () =>
            outgoing
                .filter((l) => l.kind === 'RECOLLECTS')
                .sort(
                    (a, b) =>
                        (b.weight ?? 0) - (a.weight ?? 0) ||
                        (b.ts ?? '').localeCompare(a.ts ?? ''),
                ),
        [outgoing],
    );
    const stances = byStrength(outgoing.filter((l) => l.kind === 'STANCE'));
    const heldBy = byStrength(
        (index.incoming.get(node.id) ?? []).filter((l) => l.kind === 'STANCE'),
    );
    const themes = useMemo(() => {
        const counts = new Map<string, number>();
        for (const m of memories) {
            for (const about of index.outgoing.get(m.target) ?? []) {
                if (index.byId.get(about.target)?.kind !== 'Concept') continue;
                counts.set(about.target, (counts.get(about.target) ?? 0) + 1);
            }
        }
        return [...counts]
            .filter(([, n]) => n > 1)
            .sort((a, b) => b[1] - a[1])
            .slice(0, 12);
    }, [memories, index]);
    const isLens = lens === node.id;

    return (
        <>
            <Lead title="Person" onBack={onBack}>
                <strong className="flex items-center gap-1.5">
                    <Swatch colour={node.colour} shape="dot" size="large" />
                    {node.label}
                </strong>
                <Text as="p" tone="muted">
                    {memories.length} memories · {stances.length} stances
                </Text>
                <CommandButton
                    size="small"
                    onClick={() => onLens(isLens ? null : node.id)}
                >
                    {isLens ? 'Show everyone' : 'Only their memories'}
                </CommandButton>
            </Lead>

            <TaskPaneSection title="Where they stand" aside={stances.length}>
                <StanceList
                    links={stances}
                    endpoint="target"
                    index={index}
                    onSelectNode={onSelectNode}
                />
            </TaskPaneSection>

            {heldBy.length > 0 ? (
                <TaskPaneSection
                    title="How others see them"
                    aside={heldBy.length}
                >
                    <StanceList
                        links={heldBy}
                        endpoint="source"
                        index={index}
                        onSelectNode={onSelectNode}
                    />
                </TaskPaneSection>
            ) : null}

            {themes.length > 0 ? (
                <TaskPaneSection title="Recurring themes">
                    <ChipRow>
                        {themes.map(([id, n]) => (
                            <NodeChip
                                key={id}
                                node={index.byId.get(id)}
                                count={n}
                                onSelectNode={onSelectNode}
                            />
                        ))}
                    </ChipRow>
                </TaskPaneSection>
            ) : null}

            <TaskPaneSection title="Memories, strongest first">
                {(showAll ? memories : memories.slice(0, MEMORY_PAGE)).map(
                    (m) => {
                        const event = index.byId.get(m.target);
                        return (
                            <Row
                                key={`${m.target}-${m.ts}`}
                                onClick={() => onSelectNode(m.target)}
                            >
                                <Text as="span" size="reading">
                                    “{m.snippet}”
                                </Text>
                                <Meta>
                                    <Salience weight={m.weight} />
                                    {event?.where} · {formatWhen(m.ts)}
                                    {m.recallCount
                                        ? ` · recalled ×${m.recallCount}`
                                        : ''}
                                </Meta>
                            </Row>
                        );
                    },
                )}
                {!showAll && memories.length > MEMORY_PAGE ? (
                    <CommandButton
                        size="small"
                        onClick={() => setShowAll(true)}
                    >
                        Show all {memories.length}
                    </CommandButton>
                ) : null}
            </TaskPaneSection>
        </>
    );
}

function EventPanel({ node, index, onSelectNode, onBack }: PanelCtx) {
    const recollections = (index.incoming.get(node.id) ?? [])
        .filter((l) => l.kind === 'RECOLLECTS')
        .sort((a, b) => (b.weight ?? 0) - (a.weight ?? 0));
    const about = (index.outgoing.get(node.id) ?? [])
        .filter((l) => l.kind === 'ABOUT')
        .map((l) => index.byId.get(l.target))
        .filter((n): n is EnrichedMemoryNode => !!n)
        .sort((a, b) => kindRank(a) - kindRank(b));

    return (
        <>
            <Lead title="Event" onBack={onBack}>
                <Text as="p" size="reading">
                    {node.description}
                </Text>
                <Meta>
                    {node.where} · {formatWhen(node.createdAt)}
                    {node.anchorMessageId != null
                        ? ` · message #${node.anchorMessageId}`
                        : ''}
                </Meta>
            </Lead>

            <TaskPaneSection title="Remembered by" aside={recollections.length}>
                {recollections.map((r) => {
                    const person = index.byId.get(r.source);
                    return (
                        <Row
                            key={r.source}
                            icon={
                                <Swatch
                                    colour={person?.colour}
                                    shape="dot"
                                    size="small"
                                />
                            }
                            onClick={() => onSelectNode(r.source)}
                        >
                            <strong>{person?.label ?? r.source}</strong>
                            <Text as="span" size="reading">
                                “{r.snippet}”
                            </Text>
                            <Meta>
                                <Salience weight={r.weight} />
                                {r.recallCount
                                    ? `recalled ×${r.recallCount}`
                                    : 'never recalled'}
                            </Meta>
                        </Row>
                    );
                })}
            </TaskPaneSection>

            <TaskPaneSection title="About">
                <ChipRow>
                    {about.map((n) => (
                        <NodeChip
                            key={n.id}
                            node={n}
                            onSelectNode={onSelectNode}
                        />
                    ))}
                </ChipRow>
            </TaskPaneSection>
        </>
    );
}

function ConceptPanel({ node, index, onSelectNode, onBack }: PanelCtx) {
    const incoming = index.incoming.get(node.id) ?? [];
    const events = incoming
        .filter((l) => l.kind === 'ABOUT')
        .map((l) => index.byId.get(l.source))
        .filter((n): n is EnrichedMemoryNode => !!n)
        .sort((a, b) => (b.createdAt ?? '').localeCompare(a.createdAt ?? ''));
    const stances = byStrength(incoming.filter((l) => l.kind === 'STANCE'));
    const rememberers = new Map<string, number>();
    for (const e of events) {
        for (const r of index.incoming.get(e.id) ?? []) {
            if (r.kind !== 'RECOLLECTS') continue;
            rememberers.set(r.source, (rememberers.get(r.source) ?? 0) + 1);
        }
    }

    return (
        <>
            <Lead title="Concept" onBack={onBack}>
                <strong>{node.label}</strong>
                <Text as="p" tone="muted">
                    {events.length} event{events.length === 1 ? '' : 's'}
                </Text>
            </Lead>
            {rememberers.size > 0 ? (
                <TaskPaneSection title="On whose mind">
                    <ChipRow>
                        {[...rememberers]
                            .sort((a, b) => b[1] - a[1])
                            .map(([id, n]) => (
                                <NodeChip
                                    key={id}
                                    node={index.byId.get(id)}
                                    count={n}
                                    onSelectNode={onSelectNode}
                                />
                            ))}
                    </ChipRow>
                </TaskPaneSection>
            ) : null}
            {stances.length > 0 ? (
                <TaskPaneSection title="Stances on it">
                    <StanceList
                        links={stances}
                        endpoint="source"
                        index={index}
                        onSelectNode={onSelectNode}
                    />
                </TaskPaneSection>
            ) : null}
            <TaskPaneSection title="Events">
                {events.map((e) => (
                    <Row key={e.id} onClick={() => onSelectNode(e.id)}>
                        <Text as="span" size="reading">
                            {e.description}
                        </Text>
                        <Meta>
                            {e.where} · {formatWhen(e.createdAt)}
                        </Meta>
                    </Row>
                ))}
            </TaskPaneSection>
        </>
    );
}

function StanceList({
    links,
    endpoint,
    index,
    onSelectNode,
}: {
    links: EnrichedMemoryLink[];
    endpoint: 'source' | 'target';
    index: GraphIndex;
    onSelectNode: (id: string) => void;
}) {
    if (links.length === 0) {
        return (
            <Text as="p" tone="muted">
                (none yet)
            </Text>
        );
    }
    return (
        <>
            {links.map((s) => {
                const otherId = endpointId(s[endpoint]);
                const other = index.byId.get(otherId);
                return (
                    <Row
                        key={`${s.source}-${s.target}`}
                        icon={
                            other?.colour ? (
                                <Swatch
                                    colour={other.colour}
                                    shape="dot"
                                    size="small"
                                />
                            ) : undefined
                        }
                        onClick={() => onSelectNode(otherId)}
                    >
                        <span className="flex items-center gap-1.5">
                            <strong className="min-w-0 flex-1">
                                {other?.label ?? otherId}
                                {s.source === s.target ? ' (self)' : ''}
                            </strong>
                            <Meter
                                diverging
                                value={s.valence ?? 0}
                                label={`valence ${(s.valence ?? 0).toFixed(2)}`}
                                title={`valence ${(s.valence ?? 0).toFixed(2)}`}
                            />
                        </span>
                        <Text as="span" size="reading">
                            {s.reasoning}
                        </Text>
                    </Row>
                );
            })}
        </>
    );
}

function Salience({ weight }: { weight: number | null | undefined }) {
    const value = weight ?? 0;
    return (
        <Meter
            value={value}
            tone="caution"
            label={`salience ${value.toFixed(1)}`}
            title={`salience ${value.toFixed(1)}`}
            className="w-10"
        />
    );
}

function NodeChip({
    node,
    count,
    onSelectNode,
}: {
    node: EnrichedMemoryNode | undefined;
    count?: number;
    onSelectNode: (id: string) => void;
}) {
    if (!node) return null;
    return (
        <TaskPaneLink
            icon={
                <Swatch
                    shape="dot"
                    colour={
                        node.colour ??
                        KIND_COLOUR[node.kind as keyof typeof KIND_COLOUR]
                    }
                />
            }
            onClick={() => onSelectNode(node.id)}
        >
            {node.label}
            {count ? (
                <Text as="span" tone="muted">
                    {' '}
                    ×{count}
                </Text>
            ) : null}
        </TaskPaneLink>
    );
}

function Row({
    icon,
    children,
    onClick,
}: {
    icon?: ReactNode;
    children: ReactNode;
    onClick: () => void;
}) {
    return (
        <TaskPaneLink icon={icon} onClick={onClick}>
            <span className="flex flex-col gap-0.5">{children}</span>
        </TaskPaneLink>
    );
}

function Meta({ children }: { children: ReactNode }) {
    return (
        <Text
            as="span"
            tone="muted"
            className="flex flex-wrap items-center gap-1"
        >
            {children}
        </Text>
    );
}

function ChipRow({ children }: { children: ReactNode }) {
    return <div className="flex flex-wrap gap-x-3">{children}</div>;
}

function byStrength(links: EnrichedMemoryLink[]) {
    return [...links].sort(
        (a, b) => Math.abs(b.valence ?? 0) - Math.abs(a.valence ?? 0),
    );
}

function kindRank(n: EnrichedMemoryNode) {
    return n.kind === 'Participant' ? 0 : 1;
}

export function formatWhen(iso: string | null | undefined): string {
    if (!iso) return '';
    const d = new Date(iso);
    if (Number.isNaN(d.getTime())) return iso;
    return d.toLocaleString(undefined, {
        day: 'numeric',
        month: 'short',
        hour: '2-digit',
        minute: '2-digit',
    });
}
