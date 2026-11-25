import type { Meta, StoryObj } from '@storybook/react-vite';
import { fn } from 'storybook/test';
import { indexGraph } from './graphView';
import MemoryGraphSidePanel from './MemoryGraphSidePanel';
import { PERSON_PALETTE } from './palette';
import type { EnrichedMemoryLink, EnrichedMemoryNode } from './types';

const partyId = '00000000-0000-0000-0000-000000000000';

const eventNode: EnrichedMemoryNode = {
    id: 'event:ev-1',
    kind: 'Event',
    description: 'Denise pitched a stealth horticulture demo.',
    createdAt: '2026-05-10T10:00:00Z',
    roomId: 'cg-1',
    anchorMessageId: 42,
    label: 'Group · general',
    where: 'Group · general',
};

const allNodes: EnrichedMemoryNode[] = [
    eventNode,
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
        colour: PERSON_PALETTE[0],
    },
    {
        id: `part:persona-2:${partyId}`,
        kind: 'Participant',
        label: 'Vlad',
        colour: PERSON_PALETTE[1],
    },
];

const allLinks: EnrichedMemoryLink[] = [
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
        weight: 0.8,
        recallCount: 2,
    },
    {
        source: `part:persona-1:${partyId}`,
        target: `part:persona-2:${partyId}`,
        kind: 'STANCE',
        valence: -0.4,
        reasoning: 'Vlad keeps shooting down your demos',
    },
];

const index = indexGraph({ nodes: allNodes, links: allLinks });

const meta = {
    title: 'Apps/MemoryGraphSidePanel',
    component: MemoryGraphSidePanel,
    parameters: { layout: 'centered' },
    decorators: [
        (Story) => (
            <div className="h-[560px] w-[320px]">
                <Story />
            </div>
        ),
    ],
    args: {
        node: eventNode,
        index,
        onSelectNode: fn(),
        lens: null,
        onLens: fn(),
    },
} satisfies Meta<typeof MemoryGraphSidePanel>;

export default meta;
type Story = StoryObj<typeof meta>;

export const EventSelected: Story = {};

export const NothingSelected: Story = {
    args: { node: null },
};

export const PersonSelected: Story = {
    args: { node: allNodes[2] },
};

export const ConceptSelected: Story = {
    args: { node: allNodes[1] },
};
