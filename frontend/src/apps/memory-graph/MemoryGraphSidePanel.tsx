import { type ReactNode, useMemo, useState } from 'react';
import { endpointId, type GraphIndex } from './graphView';
import { KIND_COLOUR, STANCE_COLOUR } from './palette';
import type { EnrichedMemoryLink, EnrichedMemoryNode } from './types';

interface MemoryGraphSidePanelProps {
    node: EnrichedMemoryNode | null;
    index: GraphIndex;
    onSelectNode: (id: string) => void;
    onBack?: () => void;
    lens: string | null;
    onLens: (participantId: string | null) => void;
}

const MEMORY_PAGE = 40;

export default function MemoryGraphSidePanel({
    node,
    index,
    onSelectNode,
    onBack,
    lens,
    onLens,
}: MemoryGraphSidePanelProps) {
    if (!node) {
        return (
            <div className="xp-glass-panel" style={panelStyle}>
                <div style={{ padding: '14px 12px', fontSize: 11 }}>
                    <p style={{ fontWeight: 700, margin: '0 0 4px' }}>
                        Inspector
                    </p>
                    <p style={{ color: '#555', margin: '0 0 8px' }}>
                        Click a person to read what they remember and how they
                        feel about others. Click a memory or topic to see who
                        shares it.
                    </p>
                    <p style={{ color: '#777', margin: 0 }}>
                        People sit on the outer ring; memories shared by several
                        people drift to the middle.
                    </p>
                </div>
            </div>
        );
    }

    const ctx: PanelCtx = { node, index, onSelectNode };
    return (
        <div className="xp-glass-panel" style={panelStyle}>
            <SectionHeader>
                {onBack ? (
                    <button
                        type="button"
                        onClick={onBack}
                        title="Back"
                        style={backButtonStyle}
                    >
                        ◀
                    </button>
                ) : null}
                {node.kind === 'Participant' ? 'Person' : node.kind}
            </SectionHeader>
            {node.kind === 'Participant' ? (
                <PersonPanel {...ctx} lens={lens} onLens={onLens} />
            ) : node.kind === 'Event' ? (
                <EventPanel {...ctx} />
            ) : (
                <ConceptPanel {...ctx} />
            )}
        </div>
    );
}

interface PanelCtx {
    node: EnrichedMemoryNode;
    index: GraphIndex;
    onSelectNode: (id: string) => void;
}

function PersonPanel({
    node,
    index,
    onSelectNode,
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
            <div style={{ padding: 8, fontSize: 11 }}>
                <div style={titleStyle}>
                    <Swatch colour={node.colour} size={12} /> {node.label}
                </div>
                <div style={{ color: '#555', margin: '2px 0 6px' }}>
                    {memories.length} memories · {stances.length} stances
                </div>
                <button
                    type="button"
                    onClick={() => onLens(isLens ? null : node.id)}
                >
                    {isLens ? 'Show everyone' : 'Only their memories'}
                </button>
            </div>

            <SubHeader>Where they stand ({stances.length})</SubHeader>
            <StanceList
                links={stances}
                endpoint="target"
                index={index}
                onSelectNode={onSelectNode}
            />

            {heldBy.length > 0 ? (
                <>
                    <SubHeader>How others see them ({heldBy.length})</SubHeader>
                    <StanceList
                        links={heldBy}
                        endpoint="source"
                        index={index}
                        onSelectNode={onSelectNode}
                    />
                </>
            ) : null}

            {themes.length > 0 ? (
                <>
                    <SubHeader>Recurring themes</SubHeader>
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
                </>
            ) : null}

            <SubHeader>Memories, strongest first</SubHeader>
            <ul style={listStyle}>
                {(showAll ? memories : memories.slice(0, MEMORY_PAGE)).map(
                    (m) => {
                        const event = index.byId.get(m.target);
                        return (
                            <Row
                                key={`${m.target}-${m.ts}`}
                                onClick={() => onSelectNode(m.target)}
                            >
                                <div>“{m.snippet}”</div>
                                <Meta>
                                    <WeightDots weight={m.weight} />
                                    {event?.where} · {formatWhen(m.ts)}
                                    {m.recallCount
                                        ? ` · recalled ×${m.recallCount}`
                                        : ''}
                                </Meta>
                            </Row>
                        );
                    },
                )}
            </ul>
            {!showAll && memories.length > MEMORY_PAGE ? (
                <div style={{ padding: 8 }}>
                    <button type="button" onClick={() => setShowAll(true)}>
                        Show all {memories.length}
                    </button>
                </div>
            ) : null}
        </>
    );
}

function EventPanel({ node, index, onSelectNode }: PanelCtx) {
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
            <div style={{ padding: 8, fontSize: 11 }}>
                <div style={{ fontSize: 12, lineHeight: 1.4 }}>
                    {node.description}
                </div>
                <Meta>
                    {node.where} · {formatWhen(node.createdAt)}
                    {node.anchorMessageId != null
                        ? ` · message #${node.anchorMessageId}`
                        : ''}
                </Meta>
            </div>

            <SubHeader>Remembered by ({recollections.length})</SubHeader>
            <ul style={listStyle}>
                {recollections.map((r) => {
                    const person = index.byId.get(r.source);
                    return (
                        <Row
                            key={r.source}
                            onClick={() => onSelectNode(r.source)}
                        >
                            <div style={{ fontWeight: 700 }}>
                                <Swatch colour={person?.colour} />{' '}
                                {person?.label ?? r.source}
                            </div>
                            <div>“{r.snippet}”</div>
                            <Meta>
                                <WeightDots weight={r.weight} />
                                {r.recallCount
                                    ? `recalled ×${r.recallCount}`
                                    : 'never recalled'}
                            </Meta>
                        </Row>
                    );
                })}
            </ul>

            <SubHeader>About</SubHeader>
            <ChipRow>
                {about.map((n) => (
                    <NodeChip key={n.id} node={n} onSelectNode={onSelectNode} />
                ))}
            </ChipRow>
        </>
    );
}

function ConceptPanel({ node, index, onSelectNode }: PanelCtx) {
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
            <div style={{ padding: 8, fontSize: 11 }}>
                <div style={titleStyle}>{node.label}</div>
                <div style={{ color: '#555', marginTop: 2 }}>
                    {events.length} event{events.length === 1 ? '' : 's'}
                </div>
            </div>
            {rememberers.size > 0 ? (
                <>
                    <SubHeader>On whose mind</SubHeader>
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
                </>
            ) : null}
            {stances.length > 0 ? (
                <>
                    <SubHeader>Stances on it</SubHeader>
                    <StanceList
                        links={stances}
                        endpoint="source"
                        index={index}
                        onSelectNode={onSelectNode}
                    />
                </>
            ) : null}
            <SubHeader>Events</SubHeader>
            <ul style={listStyle}>
                {events.map((e) => (
                    <Row key={e.id} onClick={() => onSelectNode(e.id)}>
                        <div>{e.description}</div>
                        <Meta>
                            {e.where} · {formatWhen(e.createdAt)}
                        </Meta>
                    </Row>
                ))}
            </ul>
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
            <p style={{ color: '#808080', padding: '4px 8px', fontSize: 11 }}>
                (none yet)
            </p>
        );
    }
    return (
        <ul style={listStyle}>
            {links.map((s) => {
                const otherId = endpointId(s[endpoint]);
                const other = index.byId.get(otherId);
                return (
                    <Row
                        key={`${s.source}-${s.target}`}
                        onClick={() => onSelectNode(otherId)}
                    >
                        <div
                            style={{
                                display: 'flex',
                                alignItems: 'center',
                                gap: 6,
                            }}
                        >
                            <span style={{ fontWeight: 700, flex: 1 }}>
                                {other?.colour ? (
                                    <Swatch colour={other.colour} />
                                ) : null}{' '}
                                {other?.label ?? otherId}
                                {s.source === s.target ? ' (self)' : ''}
                            </span>
                            <ValenceBar valence={s.valence ?? 0} />
                        </div>
                        <div style={{ color: '#444' }}>{s.reasoning}</div>
                    </Row>
                );
            })}
        </ul>
    );
}

function ValenceBar({ valence }: { valence: number }) {
    const pct = Math.round(Math.abs(valence) * 50);
    const warm = valence >= 0;
    return (
        <span
            title={`valence ${valence.toFixed(2)}`}
            style={{
                position: 'relative',
                width: 60,
                height: 6,
                background: '#e4e4e4',
                borderRadius: 3,
                flexShrink: 0,
            }}
        >
            <span
                style={{
                    position: 'absolute',
                    top: 0,
                    bottom: 0,
                    left: warm ? '50%' : `${50 - pct}%`,
                    width: `${pct}%`,
                    background: warm ? STANCE_COLOUR.warm : STANCE_COLOUR.cold,
                    borderRadius: 3,
                }}
            />
            <span
                style={{
                    position: 'absolute',
                    left: '50%',
                    top: -1,
                    bottom: -1,
                    width: 1,
                    background: '#999',
                }}
            />
        </span>
    );
}

function WeightDots({ weight }: { weight: number | null | undefined }) {
    const filled = Math.round((weight ?? 0) * 5);
    return (
        <span
            title={`salience ${(weight ?? 0).toFixed(1)}`}
            style={{ marginRight: 4, letterSpacing: -1, color: '#C98A2E' }}
        >
            {'●'.repeat(filled)}
            <span style={{ color: '#ccc' }}>{'●'.repeat(5 - filled)}</span>
        </span>
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
        <button
            type="button"
            className="xp-glass-chip"
            onClick={() => onSelectNode(node.id)}
            style={{
                cursor: 'pointer',
                border: 'none',
                minWidth: 0,
                minHeight: 0,
            }}
        >
            <span
                className="dot"
                style={{
                    background:
                        node.colour ??
                        KIND_COLOUR[node.kind as keyof typeof KIND_COLOUR],
                }}
            />
            {node.label}
            {count ? <span style={{ color: '#777' }}> ×{count}</span> : null}
        </button>
    );
}

function Row({
    children,
    onClick,
}: {
    children: ReactNode;
    onClick: () => void;
}) {
    return (
        <li>
            <button type="button" onClick={onClick} style={rowStyle}>
                {children}
            </button>
        </li>
    );
}

function Meta({ children }: { children: ReactNode }) {
    return (
        <div style={{ color: '#777', marginTop: 2, fontSize: 10 }}>
            {children}
        </div>
    );
}

function ChipRow({ children }: { children: ReactNode }) {
    return (
        <div style={{ display: 'flex', flexWrap: 'wrap', gap: 4, padding: 8 }}>
            {children}
        </div>
    );
}

function Swatch({ colour, size = 8 }: { colour?: string; size?: number }) {
    return (
        <span
            style={{
                display: 'inline-block',
                width: size,
                height: size,
                borderRadius: '50%',
                background: colour ?? '#888',
                verticalAlign: 'middle',
            }}
        />
    );
}

function SectionHeader({ children }: { children: ReactNode }) {
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
                display: 'flex',
                alignItems: 'center',
                gap: 6,
                position: 'sticky',
                top: 0,
                zIndex: 1,
            }}
        >
            {children}
        </div>
    );
}

function SubHeader({ children }: { children: ReactNode }) {
    return (
        <div
            style={{
                background: 'rgba(255,255,255,0.65)',
                boxShadow: 'inset 0 1px 0 rgba(255,255,255,0.8)',
                borderTop: '1px solid rgba(0,0,0,0.08)',
                padding: '3px 8px',
                fontSize: 11,
                fontWeight: 700,
                color: '#1F3F7A',
            }}
        >
            {children}
        </div>
    );
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

const panelStyle: React.CSSProperties = {
    width: 300,
    borderLeft: '1px solid rgba(255,255,255,0.7)',
    overflowY: 'auto',
    height: '100%',
    flexShrink: 0,
};

const titleStyle: React.CSSProperties = { fontSize: 14, fontWeight: 700 };

const listStyle: React.CSSProperties = {
    listStyle: 'none',
    margin: 0,
    padding: 0,
    fontSize: 11,
};

const rowStyle: React.CSSProperties = {
    background: 'none',
    border: 'none',
    borderBottom: '1px solid rgba(0,0,0,0.06)',
    padding: '5px 8px',
    width: '100%',
    textAlign: 'left',
    cursor: 'pointer',
    fontSize: 11,
    lineHeight: 1.35,
    minWidth: 0,
    minHeight: 0,
    boxShadow: 'none',
    borderRadius: 0,
};

const backButtonStyle: React.CSSProperties = {
    minWidth: 0,
    minHeight: 0,
    padding: '0 5px',
    fontSize: 9,
    lineHeight: '14px',
};
