import type { Meta, StoryObj } from '@storybook/react-vite';
import {
    noir,
    reducedMotion,
    reducedTransparency,
    States,
    stage,
} from '../../controls/story-kit';
import { Meter } from './Meter';

const meta = {
    title: 'Panels/Meter',
    component: Meter,
    decorators: [stage],
    args: { value: 0.6, label: 'Salience' },
} satisfies Meta<typeof Meter>;

export default meta;

type Story = StoryObj<typeof meta>;

export const Default: Story = {};

const all = (
    <States
        items={[
            ['empty', <Meter key="0" value={0} label="Empty" />],
            ['neutral', <Meter key="n" value={0.4} label="Neutral" />],
            ['go', <Meter key="g" value={0.8} tone="go" label="Go" />],
            [
                'caution',
                <Meter key="c" value={0.5} tone="caution" label="Caution" />,
            ],
            ['full', <Meter key="f" value={1} tone="danger" label="Full" />],
            [
                'diverging warm',
                <Meter key="w" value={0.7} diverging label="Warm" />,
            ],
            [
                'diverging cold',
                <Meter key="k" value={-0.45} diverging label="Cold" />,
            ],
            [
                'diverging zero',
                <Meter key="z" value={0} diverging label="Neutral" />,
            ],
        ]}
    />
);

export const AllStates: Story = { render: () => all };

export const Noir: Story = { render: () => all, parameters: noir };
export const ReducedMotion: Story = {
    render: () => all,
    parameters: reducedMotion,
};
export const ReducedTransparency: Story = {
    render: () => all,
    parameters: reducedTransparency,
};
