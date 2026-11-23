import type { Meta, StoryObj } from '@storybook/react-vite';
import {
    noir,
    reducedMotion,
    reducedTransparency,
    States,
    stage,
} from '../story-kit';
import { ProgressBar } from './ProgressBar';

const meta = {
    title: 'Controls/ProgressBar',
    component: ProgressBar,
    decorators: [stage],
    args: { label: 'Copying', value: 60 },
    argTypes: {
        tone: {
            control: 'inline-radio',
            options: [undefined, 'neutral', 'go', 'caution', 'danger'],
        },
    },
} satisfies Meta<typeof ProgressBar>;

export default meta;

type Story = StoryObj<typeof meta>;

export const Default: Story = {
    render: (args) => <ProgressBar {...args} className="ui-story-w-320" />,
};

export const Playground: Story = {};

export const AllStates: Story = {
    render: () => (
        <States
            items={[
                [
                    '0%',
                    <ProgressBar
                        key="0"
                        label="0"
                        value={0}
                        className="ui-story-w-320"
                    />,
                ],
                [
                    '35%',
                    <ProgressBar
                        key="35"
                        label="35"
                        value={35}
                        className="ui-story-w-320"
                    />,
                ],
                [
                    '100%',
                    <ProgressBar
                        key="100"
                        label="100"
                        value={100}
                        className="ui-story-w-320"
                    />,
                ],
                [
                    'marquee',
                    <ProgressBar
                        key="m"
                        label="Waking the World"
                        marquee
                        className="ui-story-w-320"
                    />,
                ],
            ]}
        />
    ),
};

export const Tones: Story = {
    render: () => (
        <States
            items={[
                [
                    'default (go)',
                    <ProgressBar
                        key="g"
                        label="Copying"
                        value={60}
                        className="ui-story-w-320"
                    />,
                ],
                [
                    'neutral',
                    <ProgressBar
                        key="n"
                        label="Share of the evening"
                        value={45}
                        tone="neutral"
                        className="ui-story-w-320"
                    />,
                ],
                [
                    'caution',
                    <ProgressBar
                        key="c"
                        label="Busy"
                        value={80}
                        tone="caution"
                        className="ui-story-w-320"
                    />,
                ],
                [
                    'danger',
                    <ProgressBar
                        key="d"
                        label="Full"
                        value={100}
                        tone="danger"
                        className="ui-story-w-320"
                    />,
                ],
            ]}
        />
    ),
};

export const Marquee: Story = {
    render: () => (
        <ProgressBar
            label="Waking the World"
            marquee
            className="ui-story-w-320"
        />
    ),
};

export const Noir: Story = { ...AllStates, parameters: noir };
export const TonesNoir: Story = { ...Tones, parameters: noir };
export const ReducedMotion: Story = { ...AllStates, parameters: reducedMotion };
export const ReducedTransparency: Story = {
    ...AllStates,
    parameters: reducedTransparency,
};
