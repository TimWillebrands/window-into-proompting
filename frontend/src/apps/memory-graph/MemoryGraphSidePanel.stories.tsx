import type { Meta, StoryObj } from '@storybook/react-vite';
import { fn } from 'storybook/test';
import type { MemoryGraphLink } from '../../api/model';
import MemoryGraphSidePanel from './MemoryGraphSidePanel';
import type { EnrichedMemoryGraph, EnrichedMemoryNode } from './types';

const partyId = '00000000-0000-0000-0000-000000000000';

const eventNode: EnrichedMemoryNode = {
    id: 'event:ev-1',
    kind: 'Event',
    description: 'Denise pitched a stealth horticulture demo.',
    createdAt: '2026-05-10T10:00:00Z',
    label: 'Denise pitched a stealth horticulture demo.',
};

const allNodes: EnrichedMemoryNode[] = [
    eventNode,
    { id: 'msg:cg-1:42', kind: 'Message', label: '#42' },
    {
        id: 'concept:horticulture',
        kind: 'Concept',
        display: 'Horticulture',
        label: 'Horticulture',
    },
    {
        id: `part:persona-1:${partyId}`,
        kind: 'Participant',
        label: 'Denise',
    },
    {
        id: `part:persona-2:${partyId}`,
        kind: 'Participant',
        label: 'Vlad',
    },
];

const allLinks: MemoryGraphLink[] = [
    { source: 'event:ev-1', target: 'msg:cg-1:42', kind: 'ANCHORED_TO' },
    { source: 'event:ev-1', target: 'concept:horticulture', kind: 'ABOUT' },
    {
        source: 'event:ev-1',
        target: `part:persona-2:${partyId}`,
        kind: 'ABOUT',
    },
    {
        source: `part:persona-1:${partyId}`,
        target: 'event:ev-1',
        kind: 'RECOLLECTS',
        snippet: 'you pitched horticulture to a sceptical Vlad',
        ts: '2026-05-10T10:00:01Z',
    },
];

const meta = {
    title: 'Apps/MemoryGraphSidePanel',
    component: MemoryGraphSidePanel,
    parameters: { layout: 'centered' },
    decorators: [
        (Story) => (
            <div
                style={{
                    width: 280,
                    height: 520,
                    background: '#5C87C2',
                    padding: 8,
                }}
            >
                <Story />
            </div>
        ),
    ],
    args: {
        node: eventNode,
        graph: {
            nodes: allNodes,
            links: allLinks,
        } satisfies EnrichedMemoryGraph,
        onSelectNode: fn(),
    },
} satisfies Meta<typeof MemoryGraphSidePanel>;

export default meta;
type Story = StoryObj<typeof meta>;

export const EventSelected: Story = {};

export const NothingSelected: Story = {
    args: { node: null },
};
