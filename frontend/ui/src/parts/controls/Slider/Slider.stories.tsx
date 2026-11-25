import type { Meta, StoryObj } from '@storybook/react-vite';
import {
    noir,
    reducedMotion,
    reducedTransparency,
    States,
    stage,
} from '../../../story-kit/story-kit';
import { Slider } from './Slider';

const meta = {
    title: 'Controls/Slider',
    component: Slider,
    decorators: [stage],
    args: { 'aria-label': 'Talkativeness' },
} satisfies Meta<typeof Slider>;

export default meta;

type Story = StoryObj<typeof meta>;

export const Default: Story = {
    render: (args) => (
        <Slider
            {...args}
            className="ui-story-w-320"
            min={0}
            max={10}
            defaultValue={4}
            ticks={11}
            startLabel="Quiet"
            endLabel="Chatty"
        />
    ),
};

export const Playground: Story = {};

export const AllStates: Story = {
    render: () => (
        <States
            items={[
                [
                    'default',
                    <Slider key="d" aria-label="d" defaultValue={30} />,
                ],
                [
                    'ticks',
                    <Slider
                        key="t"
                        aria-label="t"
                        defaultValue={50}
                        ticks={5}
                        className="ui-story-w-200"
                    />,
                ],
                [
                    'hover',
                    <Slider
                        key="h"
                        aria-label="h"
                        defaultValue={30}
                        data-ui-preview="hover"
                    />,
                ],
                [
                    'focus',
                    <Slider
                        key="f"
                        aria-label="f"
                        defaultValue={30}
                        data-ui-preview="focus"
                    />,
                ],
                [
                    'active',
                    <Slider
                        key="a"
                        aria-label="a"
                        defaultValue={30}
                        data-ui-preview="active"
                    />,
                ],
                [
                    'invalid',
                    <Slider key="i" aria-label="i" defaultValue={30} invalid />,
                ],
                [
                    'disabled',
                    <Slider
                        key="x"
                        aria-label="x"
                        defaultValue={30}
                        disabled
                    />,
                ],
            ]}
        />
    ),
};

export const Noir: Story = { ...AllStates, ...noir };
export const ReducedMotion: Story = { ...AllStates, ...reducedMotion };
export const ReducedTransparency: Story = {
    ...AllStates,
    ...reducedTransparency,
};
