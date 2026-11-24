import type { Meta, StoryObj } from '@storybook/react-vite';
import {
    noir,
    reducedMotion,
    reducedTransparency,
    States,
    stage,
} from '../../../story-kit/story-kit';
import { ShareBar, type ShareBarSegment } from './ShareBar';

const three: ShareBarSegment[] = [
    {
        key: 'a',
        share: 0.5,
        colour: '#2a78d6',
        label: '50%',
        caption: '6 now',
        title: 'llama3.2: 50%',
    },
    {
        key: 'b',
        share: 0.3,
        colour: '#eda100',
        label: '30%',
        caption: '4 now',
        title: 'nemotron: 30%',
    },
    {
        key: 'c',
        share: 0.2,
        colour: '#1baf7a',
        label: '20%',
        caption: '2 now',
        title: 'haiku: 20%',
    },
];

const meta = {
    title: 'Views/ShareBar',
    component: ShareBar,
    decorators: [stage],
    args: { segments: three, label: 'General: llama3.2 50%, nemotron 30%' },
} satisfies Meta<typeof ShareBar>;

export default meta;

type Story = StoryObj<typeof meta>;

export const Default: Story = {
    render: (args) => <ShareBar {...args} className="ui-story-w-320" />,
};

const all = (
    <States
        items={[
            [
                'one',
                <ShareBar
                    key="1"
                    className="ui-story-w-320"
                    label="One model"
                    segments={[
                        {
                            key: 'a',
                            share: 1,
                            colour: '#6250d6',
                            label: '100%',
                        },
                    ]}
                />,
            ],
            [
                'captions',
                <ShareBar
                    key="c"
                    className="ui-story-w-320"
                    label="Split"
                    segments={three}
                />,
            ],
            [
                'dim and off',
                <ShareBar
                    key="d"
                    className="ui-story-w-320"
                    label="Benched and offline"
                    segments={[
                        { ...three[0], caption: undefined },
                        { ...three[1], state: 'dim', caption: undefined },
                        { ...three[2], state: 'off', caption: undefined },
                    ]}
                />,
            ],
            [
                'thin slice',
                <ShareBar
                    key="t"
                    className="ui-story-w-320"
                    label="Thin"
                    segments={[
                        {
                            key: 'a',
                            share: 0.96,
                            colour: '#e87ba4',
                            label: '96%',
                        },
                        { key: 'b', share: 0.04, colour: '#008300' },
                    ]}
                />,
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
