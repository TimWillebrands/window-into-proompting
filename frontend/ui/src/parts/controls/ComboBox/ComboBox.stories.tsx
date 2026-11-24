import type { Meta, StoryObj } from '@storybook/react-vite';
import {
    noir,
    reducedMotion,
    reducedTransparency,
    States,
    stage,
} from '../../../story-kit/story-kit';
import { ComboBox } from './ComboBox';

const models = [
    { value: 'sonnet', label: 'Claude Sonnet' },
    { value: 'opus', label: 'Claude Opus' },
    { value: 'haiku', label: 'Claude Haiku' },
    { value: 'retired', label: 'Retired model', disabled: true },
];

const meta = {
    title: 'Controls/ComboBox',
    component: ComboBox,
    decorators: [stage],
    args: { 'aria-label': 'Model', options: models },
} satisfies Meta<typeof ComboBox>;

export default meta;

type Story = StoryObj<typeof meta>;

export const Default: Story = {
    render: (args) => (
        <ComboBox {...args} defaultValue="sonnet" className="ui-story-w-200" />
    ),
};

export const Playground: Story = {};

export const AllStates: Story = {
    render: () => (
        <States
            items={[
                [
                    'default',
                    <ComboBox
                        key="d"
                        aria-label="d"
                        options={models}
                        defaultValue="sonnet"
                    />,
                ],
                [
                    'placeholder',
                    <ComboBox
                        key="p"
                        aria-label="p"
                        options={models}
                        placeholder="Pick a model"
                    />,
                ],
                [
                    'hover',
                    <ComboBox
                        key="h"
                        aria-label="h"
                        options={models}
                        defaultValue="sonnet"
                        data-ui-preview="hover"
                    />,
                ],
                [
                    'focus',
                    <ComboBox
                        key="f"
                        aria-label="f"
                        options={models}
                        defaultValue="sonnet"
                        data-ui-preview="focus"
                    />,
                ],
                [
                    'active',
                    <ComboBox
                        key="a"
                        aria-label="a"
                        options={models}
                        defaultValue="sonnet"
                        data-ui-preview="active"
                    />,
                ],
                [
                    'invalid',
                    <ComboBox
                        key="i"
                        aria-label="i"
                        options={models}
                        placeholder="Pick a model"
                        invalid
                    />,
                ],
                [
                    'disabled',
                    <ComboBox
                        key="x"
                        aria-label="x"
                        options={models}
                        defaultValue="sonnet"
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
