import { useMemo, useRef } from 'react';
import ForceGraph2D from 'react-force-graph-2d';
import type { EnrichedMemoryGraph, EnrichedMemoryNode } from './types';

interface MemoryGraphCanvasProps {
    graph: EnrichedMemoryGraph;
    width: number;
    height: number;
    onNodeClick?: (node: EnrichedMemoryNode) => void;
    selectedNodeId?: string | null;
}

// Per-kind visual encoding from the PRD. Centralised so MemoryGraphSidePanel can use the
// same colours if it ever needs swatches.
export const KIND_COLOUR: Record<string, string> = {
    Room: '#9ED2F2',
    Persona: '#A684D1',
    Participant: '#E693B3',
    Event: '#F0A04B',
    Concept: '#7CC4A4',
    Message: '#BDBDBD',
};

const KIND_SIZE: Record<string, number> = {
    Room: 8,
    Persona: 10,
    Participant: 5,
    Event: 7,
    Concept: 5,
    Message: 3,
};

const EDGE_STYLE: Record<
    string,
    { colour: string; width: number; dashed: boolean }
> = {
    RECOLLECTS: { colour: '#E91E63', width: 2.2, dashed: false },
    ABOUT: { colour: '#AAAAAA', width: 1.0, dashed: false },
    ANCHORED_TO: { colour: '#888888', width: 1.0, dashed: true },
    HAS_PARTICIPANT: { colour: '#CCCCCC', width: 0.7, dashed: false },
};

// react-force-graph mutates node objects in place (x/y/vx/vy). Wrap in a fresh array each
// render so React's reconciler sees stable identity for the rest of the tree, but the
// canvas can mutate freely. cooldownTicks caps the layout iterations — the PRD requires
// "the app doesn't peg my CPU while idle".
export default function MemoryGraphCanvas({
    graph,
    width,
    height,
    onNodeClick,
    selectedNodeId,
}: MemoryGraphCanvasProps) {
    const graphRef = useRef<{ nodes: EnrichedMemoryNode[]; links: typeof graph.links }>(
        { nodes: [], links: [] },
    );

    const data = useMemo(() => {
        // Defensive copy: react-force-graph mutates inputs.
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
                const colour = KIND_COLOUR[node.kind] ?? '#888888';
                const size = KIND_SIZE[node.kind] ?? 4;
                const isSelected = node.id === selectedNodeId;

                ctx.beginPath();
                ctx.arc(node.x, node.y, size, 0, 2 * Math.PI);
                ctx.fillStyle = colour;
                ctx.fill();
                if (isSelected) {
                    ctx.strokeStyle = '#FF0000';
                    ctx.lineWidth = 2 / globalScale;
                    ctx.stroke();
                }

                // Labels: always-on for Room/Persona/Participant/Concept and Message.
                // Event description is hover-only (PRD user-story #5–7).
                const alwaysOn =
                    node.kind === 'Room' ||
                    node.kind === 'Persona' ||
                    node.kind === 'Participant' ||
                    node.kind === 'Concept' ||
                    node.kind === 'Message';
                if (!alwaysOn) return;
                const fontSize = 12 / globalScale;
                ctx.font = `${fontSize}px Tahoma, Geneva, sans-serif`;
                ctx.textAlign = 'center';
                ctx.textBaseline = 'top';
                ctx.fillStyle = '#222';
                ctx.fillText(node.label ?? node.id, node.x, node.y + size + 1);
            }}
            nodePointerAreaPaint={(rawNode, color, ctx) => {
                const node = rawNode as EnrichedMemoryNode & {
                    x?: number;
                    y?: number;
                };
                if (node.x === undefined || node.y === undefined) return;
                const size = KIND_SIZE[node.kind] ?? 4;
                ctx.fillStyle = color;
                ctx.beginPath();
                ctx.arc(node.x, node.y, size + 2, 0, 2 * Math.PI);
                ctx.fill();
            }}
            nodeLabel={(rawNode) => {
                // The library uses this as the HTML hover tooltip. PRD: Event description
                // and Recollection snippets surface here; otherwise show label.
                const node = rawNode as EnrichedMemoryNode;
                if (node.kind === 'Event' && node.description) {
                    return node.description;
                }
                return node.label ?? node.id;
            }}
            linkDirectionalArrowLength={(link) => {
                const style = EDGE_STYLE[(link as { kind: string }).kind];
                return style ? 4 : 2;
            }}
            linkDirectionalArrowRelPos={1}
            linkColor={(link) =>
                EDGE_STYLE[(link as { kind: string }).kind]?.colour ?? '#999'
            }
            linkWidth={(link) =>
                EDGE_STYLE[(link as { kind: string }).kind]?.width ?? 1
            }
            linkLineDash={(link) => {
                const style = EDGE_STYLE[(link as { kind: string }).kind];
                return style?.dashed ? [4, 3] : null;
            }}
        />
    );
}
