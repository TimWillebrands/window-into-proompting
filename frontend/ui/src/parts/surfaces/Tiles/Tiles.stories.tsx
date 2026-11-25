import type { Meta, StoryObj } from '@storybook/react-vite';
import type { CSSProperties } from 'react';
import {
    noir,
    reducedMotion,
    reducedTransparency,
    stage,
} from '../../../story-kit/story-kit';
import { CommandButton } from '../../controls/CommandButton/CommandButton';
import { Text } from '../../controls/Text/Text';
import { Icon } from '../../icons';
import { GroupBox } from '../GroupBox/GroupBox';
import { Tiles } from './Tiles';

const meta = {
    title: 'Surfaces/Tiles',
    component: Tiles,
    decorators: [stage],
    parameters: { stage: { width: 'wide' } },
} satisfies Meta<typeof Tiles>;

export default meta;

type Story = StoryObj<typeof meta>;

const spots = [
    { name: 'Bar', here: 2, of: 4, mine: false },
    { name: 'Window seat', here: 1, of: 2, mine: true },
    { name: 'Sofa', here: 0, of: 3, mine: false },
    { name: 'Terrace', here: 3, of: 6, mine: false },
    { name: 'Kitchen', here: 0, of: 2, mine: false },
    { name: 'Piano', here: 1, of: 1, mine: false },
];

const tiles = spots.map((spot) => (
    <GroupBox
        key={spot.name}
        title={`${spot.name} · ${spot.here}/${spot.of}`}
        current={spot.mine}
        icon={spot.mine ? <Icon name="location" /> : undefined}
    >
        <Text>{spot.here === 0 ? 'nobody' : `${spot.here} here`}</Text>
        {spot.here < spot.of && !spot.mine ? (
            <CommandButton size="small">Go</CommandButton>
        ) : null}
    </GroupBox>
));

const width = (px: number): CSSProperties => ({ width: px });

export const Default: Story = {
    render: () => (
        <div style={width(640)}>
            <Tiles>{tiles}</Tiles>
        </div>
    ),
};

export const MinWidth: Story = {
    render: () => (
        <div style={width(640)}>
            <Tiles minWidth="260px" gap="md">
                {tiles}
            </Tiles>
        </div>
    ),
};

export const Narrow: Story = {
    render: () => (
        <div style={width(300)}>
            <Tiles>{tiles}</Tiles>
        </div>
    ),
};

export const FewTiles: Story = {
    render: () => (
        <div style={width(640)}>
            <Tiles>{tiles.slice(0, 2)}</Tiles>
        </div>
    ),
};

export const Noir: Story = { ...Default, ...noir };
export const ReducedMotion: Story = { ...Default, ...reducedMotion };
export const ReducedTransparency: Story = {
    ...Default,
    ...reducedTransparency,
};
