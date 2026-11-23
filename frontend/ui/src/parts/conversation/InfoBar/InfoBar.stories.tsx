import type { Meta, StoryObj } from '@storybook/react-vite';
import { fn } from 'storybook/test';
import { CommandButton } from '../../controls';
import {
    noir,
    reducedMotion,
    reducedTransparency,
    stage,
} from '../../controls/story-kit';
import { Text } from '../../controls/Text/Text';
import { Icon } from '../../icons';
import { InfoBar } from './InfoBar';
import '../story-kit';

const meta = {
    title: 'Conversation/InfoBar',
    component: InfoBar,
    decorators: [stage],
} satisfies Meta<typeof InfoBar>;

export default meta;

type Story = StoryObj<typeof meta>;

export const Default: Story = {
    render: () => (
        <div className="ui-story-chat-column ui-story-chat-w-420">
            <InfoBar icon={<Icon name="scales" />}>
                <Text strong>As Grace:</Text> answer Ada's question.
            </InfoBar>
            <InfoBar
                tone="neutral"
                actions={
                    <CommandButton size="small" onClick={fn()}>
                        Jump to latest
                    </CommandButton>
                }
            >
                3 new messages
            </InfoBar>
            <InfoBar tone="neutral" icon={<Icon name="watching" />}>
                You're reading a private conversation. They can't see you.
            </InfoBar>
            <InfoBar tone="danger" icon={<Icon name="warning" />}>
                Could not save the cast.
            </InfoBar>
            <InfoBar tone="go" icon={<Icon name="check" />}>
                Saved.
            </InfoBar>
        </div>
    ),
};

export const Noir: Story = { ...Default, parameters: noir };
export const ReducedMotion: Story = { ...Default, parameters: reducedMotion };
export const ReducedTransparency: Story = {
    ...Default,
    parameters: reducedTransparency,
};
