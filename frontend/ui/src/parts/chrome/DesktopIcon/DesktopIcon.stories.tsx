import type { Meta, StoryObj } from '@storybook/react-vite';
import { fn } from 'storybook/test';
import {
    Desk,
    noir,
    preview,
    reducedMotion,
    reducedTransparency,
    States,
} from '../../../story-kit/story-kit';
import { Icon } from '../../icons';
import { DesktopIcon } from './DesktopIcon';

const meta = {
    title: 'Chrome/DesktopIcon',
    component: DesktopIcon,
    args: {
        icon: <Icon name="personas" size={48} />,
        label: 'Personas',
        onClick: fn(),
    },
    decorators: [
        (Story) => (
            <Desk size="small">
                <Story />
            </Desk>
        ),
    ],
} satisfies Meta<typeof DesktopIcon>;

export default meta;

type Story = StoryObj<typeof meta>;

export const Default: Story = {};

export const Selected: Story = { args: { selected: true } };

export const Hover: Story = { play: preview('.ui-desktop-icon', 'hover') };

export const Focus: Story = { play: preview('.ui-desktop-icon', 'focus') };

export const Pressed: Story = { play: preview('.ui-desktop-icon', 'active') };

export const Disabled: Story = { args: { disabled: true } };

export const LongLabel: Story = {
    args: { label: 'A Very Long Desktop Icon Label That Wraps' },
};

export const Column: Story = {
    render: (args) => (
        <States
            layout="tiles"
            items={[
                ['default', <DesktopIcon key="a" {...args} />],
                [
                    'selected',
                    <DesktopIcon
                        key="b"
                        {...args}
                        icon={<Icon name="chat-rooms" size={48} />}
                        label="Chat Rooms"
                        selected
                    />,
                ],
                [
                    'long label',
                    <DesktopIcon
                        key="c"
                        {...args}
                        icon={<Icon name="control-panel" size={48} />}
                        label="Control Panel and Friends"
                    />,
                ],
            ]}
        />
    ),
};

export const Noir: Story = { ...noir };

export const ReducedMotion: Story = { ...reducedMotion };

export const ReducedTransparency: Story = { ...reducedTransparency };
