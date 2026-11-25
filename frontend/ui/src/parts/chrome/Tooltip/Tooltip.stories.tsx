import type { Meta, StoryObj } from '@storybook/react-vite';
import {
    busy,
    Desk,
    noir,
    reducedMotion,
    reducedTransparency,
} from '../../../story-kit/story-kit';
import { CommandButton } from '../../controls';
import { Tooltip } from './Tooltip';

const meta = {
    title: 'Chrome/Tooltip',
    component: Tooltip,
    args: {
        content: 'Shows who is in the room',
        open: true,
        children: <CommandButton>Who's here</CommandButton>,
    },
    decorators: [
        (Story) => (
            <Desk size="small">
                <Story />
            </Desk>
        ),
    ],
} satisfies Meta<typeof Tooltip>;

export default meta;

type Story = StoryObj<typeof meta>;

export const Default: Story = {};

export const Above: Story = { args: { placement: 'above' } };

export const OnHover: Story = {
    args: {
        open: undefined,
        content: 'Hover or focus me, Escape hides',
    },
};

export const Noir: Story = { ...noir };

export const ReducedMotion: Story = { ...reducedMotion };

export const OverBusyBackdrop: Story = { ...busy };

export const ReducedTransparency: Story = { ...reducedTransparency };
