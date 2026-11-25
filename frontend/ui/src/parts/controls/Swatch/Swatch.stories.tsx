import type { Meta, StoryObj } from '@storybook/react-vite';
import {
    noir,
    reducedMotion,
    reducedTransparency,
    States,
    stage,
} from '../../../story-kit/story-kit';
import { Swatch } from './Swatch';

const meta = {
    title: 'Controls/Swatch',
    component: Swatch,
    decorators: [stage],
    args: { colour: '#2a78d6' },
} satisfies Meta<typeof Swatch>;

export default meta;

type Story = StoryObj<typeof meta>;

export const Default: Story = {
    render: (args) => (
        <span className="ui-story-row">
            <Swatch {...args} /> llama3.2 · Local
        </span>
    ),
};

const all = (
    <States
        items={[
            [
                'sizes',
                <span key="s" className="ui-story-row">
                    <Swatch colour="#eb6834" size="small" />
                    <Swatch colour="#eb6834" />
                    <Swatch colour="#eb6834" size="large" />
                </span>,
            ],
            [
                'dot',
                <span key="d" className="ui-story-row">
                    <Swatch colour="#1baf7a" shape="dot" size="small" />
                    <Swatch colour="#1baf7a" shape="dot" />
                    <Swatch colour="#1baf7a" shape="dot" size="large" />
                </span>,
            ],
            [
                'ring',
                <Swatch
                    key="r"
                    colour="#f3c487"
                    ring="#7b5cc4"
                    shape="dot"
                    size="large"
                />,
            ],
            ['empty', <Swatch key="e" />],
            [
                'labelled',
                <Swatch key="l" colour="#e34948" label="Series colour: red" />,
            ],
        ]}
    />
);

export const AllStates: Story = { render: () => all };

export const Noir: Story = { render: () => all, ...noir };
export const ReducedMotion: Story = {
    render: () => all,
    ...reducedMotion,
};
export const ReducedTransparency: Story = {
    render: () => all,
    ...reducedTransparency,
};
