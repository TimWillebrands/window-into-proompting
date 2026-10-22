import { useEffect, useMemo, useRef, useState } from 'react';
import ForceGraph2D, {
    type ForceGraphMethods,
    type LinkObject,
    type NodeObject,
} from 'react-force-graph-2d';
import { endpointId, type MemoryView, neighbourhood } from './graphView';
import { KIND_COLOUR, STANCE_COLOUR } from './palette';
import type { EnrichedMemoryLink, EnrichedMemoryNode } from './types';

interface MemoryGraphCanvasProps {
    view: MemoryView;
    lens: string | null;
    width: number;
    height: number;
    selectedNodeId: string | null;
    matches: Set<string> | null;
    onNodeClick: (node: EnrichedMemoryNode) => void;
    onBackgroundClick: () => void;
}

type Node = NodeObject<EnrichedMemoryNode>;
type Link = LinkObject<EnrichedMemoryNode, EnrichedMemoryLink>;

const EVENT_STROKE = '#C98A2E';
const DIM_ALPHA = 0.1;
const FONT = 'Tahoma, Geneva, sans-serif';

interface EventStyle {
    radius: number;
    rememberedBy: string[];
}

export default function MemoryGraphCanvas({
    view,
    lens,
    width,
    height,
    selectedNodeId,
    matches,
    onNodeClick,
    onBackgroundClick,
}: MemoryGraphCanvasProps) {
    const fgRef = useRef<ForceGraphMethods<Node, Link> | undefined>(undefined);
    const [hoverId, setHoverId] = useState<string | null>(null);
    const fittedRef = useRef(false);

    const colourById = useMemo(
        () => new Map(view.nodes.map((n) => [n.id, n.colour])),
        [view.nodes],
    );

    // Event radius from its strongest Recollection; the ring shows who remembers it.
    const eventStyle = useMemo(() => {
        const styles = new Map<string, EventStyle>();
        for (const link of view.links) {
            if (link.kind !== 'RECOLLECTS') continue;
            const target = endpointId(link.target);
            const style = styles.get(target) ?? { radius: 3, rememberedBy: [] };
            style.radius = Math.max(style.radius, 3 + 4 * (link.weight ?? 0.5));
            style.rememberedBy.push(
                colourById.get(endpointId(link.source)) ?? '#888',
            );
            styles.set(target, style);
        }
        return styles;
    }, [view.links, colourById]);

    // Defensive copy for force-graph; people pinned on a ring (lens in the middle) so shared memories settle between them.
    const data = useMemo(() => {
        const people = view.nodes.filter(
            (n) => n.kind === 'Participant' && n.id !== lens,
        );
        const ring = Math.max(
            160,
            14 * Math.sqrt(view.nodes.length) * Math.sqrt(people.length + 1),
        );
        const nodes: Node[] = view.nodes.map((n) => {
            const node: Node = { ...n };
            const i = people.indexOf(n);
            if (n.id === lens || (i >= 0 && people.length === 1 && !lens)) {
                node.fx = 0;
                node.fy = 0;
            } else if (i >= 0) {
                const angle = (2 * Math.PI * i) / people.length - Math.PI / 2;
                node.fx = ring * Math.cos(angle);
                node.fy = ring * Math.sin(angle);
            }
            return node;
        });
        fittedRef.current = false;
        return { nodes, links: view.links.map((l) => ({ ...l })) as Link[] };
    }, [view, lens]);

    useEffect(() => {
        const fg = fgRef.current;
        if (!fg || data.nodes.length === 0) return;
        fg.d3Force('charge')?.strength?.((n: Node) =>
            n.kind === 'Concept' ? -45 : -32,
        );
        const link = fg.d3Force('link');
        link?.distance?.((l: Link) => (l.kind === 'ABOUT' ? 28 : 90));
        link?.strength?.((l: Link) =>
            l.kind === 'STANCE' ? 0.08 : l.kind === 'ABOUT' ? 0.25 : 0.06,
        );
        fg.d3ReheatSimulation();
        // People are pinned, so the frame is known early; refit again once the engine stops.
        const fit = setTimeout(() => fg.zoomToFit(400, 30), 600);
        return () => clearTimeout(fit);
    }, [data]);

    const focusId = hoverId ?? selectedNodeId;
    const focus = useMemo(
        () => (focusId ? neighbourhood(view, focusId) : null),
        [view, focusId],
    );

    // Focus (hover/selection) wins over search so a picked memory still shows who holds it.
    const isLit = (id: string) =>
        focus ? focus.has(id) : !matches || matches.has(id);

    useEffect(() => {
        if (!selectedNodeId) return;
        const node = data.nodes.find((n) => n.id === selectedNodeId);
        const fg = fgRef.current;
        if (!fg || node?.x === undefined || node.y === undefined) return;
        // Only pan when the pick (e.g. from the inspector) is off-screen.
        const at = fg.graph2ScreenCoords(node.x, node.y);
        const margin = 40;
        if (
            at.x < margin ||
            at.y < margin ||
            at.x > width - margin ||
            at.y > height - margin
        ) {
            fg.centerAt(node.x, node.y, 500);
        }
    }, [selectedNodeId, data, width, height]);

    return (
        <ForceGraph2D<EnrichedMemoryNode, EnrichedMemoryLink>
            ref={fgRef}
            graphData={data}
            width={width}
            height={height}
            backgroundColor="#FAFAF6"
            nodeId="id"
            cooldownTicks={250}
            d3VelocityDecay={0.35}
            onEngineStop={() => {
                if (fittedRef.current) return;
                fittedRef.current = true;
                fgRef.current?.zoomToFit(400, 30);
            }}
            onNodeClick={(node) => onNodeClick(node as EnrichedMemoryNode)}
            onNodeHover={(node) =>
                setHoverId(node?.id ? String(node.id) : null)
            }
            onBackgroundClick={onBackgroundClick}
            nodeCanvasObject={(node, ctx, scale) => {
                if (node.x === undefined || node.y === undefined) return;
                const lit = isLit(node.id);
                ctx.globalAlpha = lit ? 1 : DIM_ALPHA;
                const selected = node.id === selectedNodeId;
                if (node.kind === 'Participant') {
                    drawPerson(ctx, node, scale, selected);
                } else if (node.kind === 'Event') {
                    drawEvent(
                        ctx,
                        node,
                        eventStyle.get(node.id),
                        scale,
                        selected,
                    );
                } else {
                    const degree = view.degree.get(node.id) ?? 1;
                    drawConcept(ctx, node, degree, scale, selected);
                }
                ctx.globalAlpha = 1;
            }}
            onRenderFramePost={(ctx, scale) => {
                const candidates: LabelCandidate[] = [];
                for (const node of data.nodes) {
                    if (node.x === undefined || node.y === undefined) continue;
                    const priority = labelPriority(node, {
                        selected: node.id === selectedNodeId,
                        lit: isLit(node.id),
                        narrowed: focus !== null || matches !== null,
                        searching: matches !== null && focus === null,
                        degree: view.degree.get(node.id) ?? 0,
                    });
                    if (priority !== null) candidates.push({ node, priority });
                }
                drawLabels(ctx, candidates, scale, (n) =>
                    hitRadius(n, view, eventStyle),
                );
            }}
            nodePointerAreaPaint={(node, colour, ctx) => {
                if (node.x === undefined || node.y === undefined) return;
                ctx.fillStyle = colour;
                ctx.beginPath();
                ctx.arc(
                    node.x,
                    node.y,
                    hitRadius(node, view, eventStyle) + 2,
                    0,
                    2 * Math.PI,
                );
                ctx.fill();
            }}
            nodeLabel={(node) => tooltip(node, view)}
            linkColor={(link) => {
                const source = endpointId(link.source);
                const target = endpointId(link.target);
                // Focused: only the focus node's own edges, plus its memories' topics.
                const touches =
                    !focusId ||
                    source === focusId ||
                    target === focusId ||
                    link.kind === 'ABOUT';
                const lit = touches && isLit(source) && isLit(target);
                return linkColour(link, colourById, lit, focus !== null);
            }}
            linkWidth={(link) => {
                if (link.kind === 'STANCE')
                    return 0.5 + 1.5 * Math.abs(link.valence ?? 0);
                if (link.kind === 'RECOLLECTS')
                    return 0.4 + 1.4 * (link.weight ?? 0.5);
                return 0.5;
            }}
            linkCurvature={(link) => (link.kind === 'STANCE' ? 0.25 : 0)}
            linkDirectionalArrowLength={(link) =>
                link.kind === 'STANCE' ? 6 : 0
            }
            linkDirectionalArrowRelPos={0.9}
        />
    );
}

function drawPerson(
    ctx: CanvasRenderingContext2D,
    node: Node,
    scale: number,
    selected: boolean,
) {
    const x = node.x as number;
    const y = node.y as number;
    const r = 13;
    ctx.beginPath();
    ctx.arc(x, y, r, 0, 2 * Math.PI);
    ctx.fillStyle = node.colour ?? '#888';
    ctx.fill();
    ctx.lineWidth = (selected ? 4 : 2) / scale;
    ctx.strokeStyle = selected ? '#111' : '#fff';
    ctx.stroke();
}

function drawEvent(
    ctx: CanvasRenderingContext2D,
    node: Node,
    style: EventStyle | undefined,
    scale: number,
    selected: boolean,
) {
    const x = node.x as number;
    const y = node.y as number;
    const r = style?.radius ?? 4;
    ctx.beginPath();
    ctx.arc(x, y, r, 0, 2 * Math.PI);
    ctx.fillStyle = KIND_COLOUR.Event;
    ctx.fill();
    // One ring segment per person who remembers it.
    const owners = style?.rememberedBy ?? [];
    const ring = Math.max(1.4, 2.2 / scale);
    if (owners.length === 0) {
        ctx.lineWidth = 1 / scale;
        ctx.strokeStyle = EVENT_STROKE;
        ctx.stroke();
    }
    owners.forEach((colour, i) => {
        const start = (2 * Math.PI * i) / owners.length - Math.PI / 2;
        const end = (2 * Math.PI * (i + 1)) / owners.length - Math.PI / 2;
        ctx.beginPath();
        ctx.arc(x, y, r + ring / 2, start, end);
        ctx.lineWidth = ring;
        ctx.strokeStyle = colour;
        ctx.stroke();
    });
    if (selected) selectionRing(ctx, x, y, r + ring + 2, scale);
}

function drawConcept(
    ctx: CanvasRenderingContext2D,
    node: Node,
    degree: number,
    scale: number,
    selected: boolean,
) {
    const x = node.x as number;
    const y = node.y as number;
    const r = conceptRadius(degree);
    ctx.beginPath();
    ctx.arc(x, y, r, 0, 2 * Math.PI);
    ctx.fillStyle = KIND_COLOUR.Concept;
    ctx.fill();
    if (selected) selectionRing(ctx, x, y, r + 2, scale);
}

function selectionRing(
    ctx: CanvasRenderingContext2D,
    x: number,
    y: number,
    r: number,
    scale: number,
) {
    ctx.beginPath();
    ctx.arc(x, y, r, 0, 2 * Math.PI);
    ctx.lineWidth = 2.5 / scale;
    ctx.strokeStyle = '#111';
    ctx.stroke();
}

interface LabelCandidate {
    node: Node;
    priority: number;
}

/// <summary>Lower draws first and wins collisions; null = no label.</summary>
function labelPriority(
    node: Node,
    s: {
        selected: boolean;
        lit: boolean;
        narrowed: boolean;
        searching: boolean;
        degree: number;
    },
): number | null {
    if (node.kind === 'Participant') return 0;
    if (s.selected) return 1;
    if (!s.lit) return null;
    if (node.kind === 'Event') return s.searching ? 50 : null;
    return (s.narrowed ? 19 : 109) - Math.min(s.degree, 9);
}

// Screen-constant labels with a paper halo; greedy placement skips any that would overlap.
function drawLabels(
    ctx: CanvasRenderingContext2D,
    candidates: LabelCandidate[],
    scale: number,
    radius: (n: Node) => number,
) {
    candidates.sort((a, b) => a.priority - b.priority);
    const placed: Box[] = [];
    ctx.textAlign = 'center';
    ctx.textBaseline = 'top';
    ctx.lineWidth = 3 / scale;
    ctx.strokeStyle = 'rgba(250,250,246,0.9)';
    ctx.fillStyle = '#222';
    for (const { node, priority } of candidates) {
        const person = node.kind === 'Participant';
        const size = (person ? 13 : 11) / scale;
        ctx.font = `${person || priority < 20 ? 'bold ' : ''}${size}px ${FONT}`;
        const text =
            node.kind === 'Event'
                ? truncate(node.description ?? node.label, 48)
                : node.label;
        const w = ctx.measureText(text).width;
        const x = node.x as number;
        const y = (node.y as number) + radius(node) + 2 / scale;
        const box: Box = [x - w / 2, y, x + w / 2, y + size];
        if (priority > 1 && placed.some((p) => overlaps(p, box))) continue;
        placed.push(box);
        ctx.strokeText(text, x, y);
        ctx.fillText(text, x, y);
    }
}

type Box = [number, number, number, number];

function overlaps(a: Box, b: Box) {
    return a[0] < b[2] && b[0] < a[2] && a[1] < b[3] && b[1] < a[3];
}

function truncate(s: string, n: number) {
    return s.length > n ? `${s.slice(0, n - 1)}…` : s;
}

function conceptRadius(degree: number) {
    return 2.5 + 1.6 * Math.sqrt(degree);
}

function hitRadius(
    node: Node,
    view: MemoryView,
    eventStyle: Map<string, EventStyle>,
) {
    if (node.kind === 'Participant') return 13;
    if (node.kind === 'Event') return eventStyle.get(node.id)?.radius ?? 4;
    return conceptRadius(view.degree.get(node.id) ?? 1);
}

function linkColour(
    link: Link,
    colourById: Map<string, string | undefined>,
    lit: boolean,
    focused: boolean,
) {
    if (!lit) return 'rgba(120,120,120,0.04)';
    if (link.kind === 'STANCE') {
        const hue =
            (link.valence ?? 0) >= 0 ? STANCE_COLOUR.warm : STANCE_COLOUR.cold;
        return withAlpha(hue, focused ? 0.9 : 0.45);
    }
    if (link.kind === 'RECOLLECTS') {
        const alpha = focused
            ? 0.45 + 0.4 * (link.weight ?? 0.5)
            : 0.04 + 0.12 * (link.weight ?? 0.5);
        return withAlpha(
            colourById.get(endpointId(link.source)) ?? '#888888',
            alpha,
        );
    }
    return focused ? 'rgba(80,140,120,0.6)' : 'rgba(80,140,120,0.22)';
}

function withAlpha(hex: string, alpha: number) {
    const n = Number.parseInt(hex.slice(1), 16);
    return `rgba(${(n >> 16) & 255},${(n >> 8) & 255},${n & 255},${alpha.toFixed(2)})`;
}

function tooltip(node: Node, view: MemoryView) {
    if (node.kind === 'Event') {
        return `<div style="max-width:280px;font:11px ${FONT}"><b>${escapeHtml(node.where ?? '')}</b><br/>${escapeHtml(node.description ?? '')}</div>`;
    }
    if (node.kind === 'Concept') {
        const n = view.degree.get(node.id) ?? 0;
        return `<span style="font:11px ${FONT}">${escapeHtml(node.label)} · ${n} link${n === 1 ? '' : 's'}</span>`;
    }
    return `<span style="font:11px ${FONT}">${escapeHtml(node.label)}</span>`;
}

function escapeHtml(s: string) {
    return s
        .replaceAll('&', '&amp;')
        .replaceAll('<', '&lt;')
        .replaceAll('>', '&gt;');
}
