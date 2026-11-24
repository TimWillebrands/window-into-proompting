import type { Meta, StoryObj } from '@storybook/react-vite';
import {
    noir,
    reducedMotion,
    reducedTransparency,
    stage,
} from '../../../story-kit/story-kit';
import { Icon } from '../../icons';
import { StatusBar, StatusBarField } from './StatusBar';

const meta = {
    title: 'Surfaces/StatusBar',
    component: StatusBar,
    decorators: [stage],
} satisfies Meta<typeof StatusBar>;

export default meta;

type Story = StoryObj<typeof meta>;

export const Default: Story = {
    render: () => (
        <StatusBar grip className="ui-story-w-560">
            <StatusBarField grow>3 Personas in this Room</StatusBarField>
            <StatusBarField>12 messages</StatusBarField>
            <StatusBarField icon={<Icon name="internet" />}>
                Online
            </StatusBarField>
        </StatusBar>
    ),
};

export const WithoutGrip: Story = {
    render: () => (
        <StatusBar className="ui-story-w-560">
            <StatusBarField grow>Done</StatusBarField>
        </StatusBar>
    ),
};

export const Noir: Story = { ...Default, ...noir };
export const ReducedMotion: Story = { ...Default, ...reducedMotion };
export const ReducedTransparency: Story = {
    ...Default,
    ...reducedTransparency,
};
