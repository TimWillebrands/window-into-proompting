import type { Meta, StoryObj } from '@storybook/react-vite';
import { fn } from 'storybook/test';
import {
    noir,
    preview,
    reducedMotion,
    reducedTransparency,
    States,
} from '../../../story-kit/story-kit';
import { StartButton } from './StartButton';

const meta = {
    title: 'Chrome/StartButton',
    component: StartButton,
    args: { onClick: fn() },
    decorators: [
        (Story) => (
            <div className="ui-taskbar">
                <Story />
            </div>
        ),
    ],
} satisfies Meta<typeof StartButton>;

export default meta;

type Story = StoryObj<typeof meta>;

export const Default: Story = {};

export const Open: Story = { args: { open: true } };

export const Hover: Story = { play: preview('.ui-start-button', 'hover') };

export const Focus: Story = { play: preview('.ui-start-button', 'focus') };

export const Pressed: Story = { play: preview('.ui-start-button', 'active') };

export const Disabled: Story = { args: { disabled: true } };

export const AllStates: Story = {
    decorators: [(Story) => <Story />],
    render: () => (
        <States
            layout="tiles"
            items={[
                ['default', <Bar key="d" />],
                ['open', <Bar key="o" open />],
                ['disabled', <Bar key="x" disabled />],
            ]}
        />
    ),
};

function Bar(props: { open?: boolean; disabled?: boolean }) {
    return (
        <div className="ui-taskbar">
            <StartButton {...props} />
        </div>
    );
}

export const Noir: Story = { ...noir };

export const ReducedMotion: Story = { ...reducedMotion };

export const ReducedTransparency: Story = { ...reducedTransparency };
