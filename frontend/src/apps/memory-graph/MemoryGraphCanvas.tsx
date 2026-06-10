import { useMemo, useRef } from 'react';
import ForceGraph2D from 'react-force-graph-2d';
import type {
    EnrichedMemoryGraph,
    EnrichedMemoryNode,
    MemoryEdgeKind,
    MemoryNodeKind,
} from './types';

interface MemoryGraphCanvasProps {
    graph: EnrichedMemoryGraph;
    width: number;
    height: number;
    onNodeClick?: (node: EnrichedMemoryNode) => void;
    selectedNodeId?: string | null;
}

interface NodeVisual {
    colour: string;
    size: number;
    labelMode: 'always' | 'hover';
}

const NODE_VISUAL: Record<MemoryNodeKind, NodeVisual> = {
    Persona: { colour: '#A684D1', size: 10, labelMode: 'always' },
    Participant: { colour: '#E693B3', size: 5, labelMode: 'always' },
    Event: { colour: '#F0A04B', size: 7, labelMode: 'hover' },
    Concept: { colour: '#7CC4A4', size: 5, labelMode: 'always' },
};

const FALLBACK_VISUAL: NodeVisual = {
    colour: '#888888',
    size: 4,
    labelMode: 'hover',
};

interface EdgeStyle {
    colour: string;
    width: number;
    dashed: boolean;
}

const EDGE_STYLE: Record<MemoryEdgeKind, EdgeStyle> = {
    RECOLLECTS: { colour: '#E91E63', width: 2.2, dashed: false },
    ABOUT: { colour: '#AAAAAA', width: 1.0, dashed: false },
    HAS_PARTICIPANT: { colour: '#CCCCCC', width: 0.7, dashed: false },
};

const FALLBACK_EDGE: EdgeStyle = { colour: '#999', width: 1, dashed: false };

function visualFor(kind: string): NodeVisual {
    return NODE_VISUAL[kind as MemoryNodeKind] ?? FALLBACK_VISUAL;
}

function edgeStyleFor(link: unknown): EdgeStyle {
    const kind = (link as { kind: string }).kind;
    return EDGE_STYLE[kind as MemoryEdgeKind] ?? FALLBACK_EDGE;
}

export default function MemoryGraphCanvas({
    graph,
    width,
    height,
    onNodeClick,
    selectedNodeId,
}: MemoryGraphCanvasProps) {
    // react-force-graph mutates node objects in place (x/y/vx/vy). Defensive copy each
    // render so the canvas can mutate freely without writing back into our enriched data.
    // cooldownTicks caps layout iterations so the canvas stops chewing CPU when idle.
    const graphRef = useRef<{
        nodes: EnrichedMemoryNode[];
        links: typeof graph.links;
    }>({ nodes: [], links: [] });

    const data = useMemo(() => {
        graphRef.current = {
            nodes: graph.nodes.map((n) => ({ ...n })),
            links: graph.links.map((l) => ({ ...l })),
        };
        return graphRef.current;
    }, [graph]);

    return (
        <ForceGraph2D
            graphData={data}
            width={width}
            height={height}
            cooldownTicks={200}
            nodeRelSize={4}
            nodeId="id"
            onNodeClick={(node) => onNodeClick?.(node as EnrichedMemoryNode)}
            nodeCanvasObject={(rawNode, ctx, globalScale) => {
                const node = rawNode as EnrichedMemoryNode & {
                    x?: number;
                    y?: number;
                };
                if (node.x === undefined || node.y === undefined) return;
                const visual = visualFor(node.kind);
                const isSelected = node.id === selectedNodeId;

                ctx.beginPath();
                ctx.arc(node.x, node.y, visual.size, 0, 2 * Math.PI);
                ctx.fillStyle = visual.colour;
                ctx.fill();
                if (isSelected) {
                    ctx.strokeStyle = '#FF0000';
                    ctx.lineWidth = 2 / globalScale;
                    ctx.stroke();
                }

                if (visual.labelMode !== 'always') return;
                const fontSize = 12 / globalScale;
                ctx.font = `${fontSize}px Tahoma, Geneva, sans-serif`;
                ctx.textAlign = 'center';
                ctx.textBaseline = 'top';
                ctx.fillStyle = '#222';
                ctx.fillText(
                    node.label ?? node.id,
                    node.x,
                    node.y + visual.size + 1,
                );
            }}
            nodePointerAreaPaint={(rawNode, color, ctx) => {
                const node = rawNode as EnrichedMemoryNode & {
                    x?: number;
                    y?: number;
                };
                if (node.x === undefined || node.y === undefined) return;
                const { size } = visualFor(node.kind);
                ctx.fillStyle = color;
                ctx.beginPath();
                ctx.arc(node.x, node.y, size + 2, 0, 2 * Math.PI);
                ctx.fill();
            }}
            nodeLabel={(rawNode) => {
                const node = rawNode as EnrichedMemoryNode;
                if (node.kind === 'Event' && node.description) {
                    return node.description;
                }
                return node.label ?? node.id;
            }}
            linkDirectionalArrowLength={(link) =>
                EDGE_STYLE[(link as { kind: string }).kind as MemoryEdgeKind]
                    ? 4
                    : 2
            }
            linkDirectionalArrowRelPos={1}
            linkColor={(link) => edgeStyleFor(link).colour}
            linkWidth={(link) => edgeStyleFor(link).width}
            linkLineDash={(link) => (edgeStyleFor(link).dashed ? [4, 3] : null)}
        />
    );
}
