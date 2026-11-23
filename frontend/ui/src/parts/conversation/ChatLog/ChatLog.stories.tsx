import type { Meta, StoryObj } from '@storybook/react-vite';
import {
    noir,
    reducedMotion,
    reducedTransparency,
    stage,
} from '../../controls/story-kit';
import { ChatMessage } from '../ChatMessage/ChatMessage';
import { ChatLog } from './ChatLog';
import '../story-kit';

const meta = {
    title: 'Conversation/ChatLog',
    component: ChatLog,
    decorators: [stage],
} satisfies Meta<typeof ChatLog>;

export default meta;

type Story = StoryObj<typeof meta>;

const lines = [
    ['Ada says:', 'morning!'],
    ['Grace says:', 'morning :) coffee?'],
    ['Ada says:', 'always'],
    ['Grace says:', 'meet you downstairs in five'],
    ['Ada says:', 'see you there'],
    ['Grace says:', 'bring the good mugs'],
    ['Ada says:', 'obviously'],
] as const;

export const Default: Story = {
    render: () => (
        <ChatLog
            aria-label="Conversation"
            className="ui-story-chat-w-420 ui-story-chat-h-300"
        >
            {lines.map(([sender, text], index) => (
                <ChatMessage
                    // biome-ignore lint/suspicious/noArrayIndexKey: static story lines
                    key={index}
                    layout="transcript"
                    sender={sender}
                    own={sender.startsWith('Grace')}
                >
                    {text}
                </ChatMessage>
            ))}
        </ChatLog>
    ),
};

export const Empty: Story = {
    render: () => (
        <ChatLog
            aria-label="Conversation"
            className="ui-story-chat-w-420 ui-story-chat-h-300"
        />
    ),
};

export const Noir: Story = { ...Default, parameters: noir };
export const ReducedMotion: Story = { ...Default, parameters: reducedMotion };
export const ReducedTransparency: Story = {
    ...Default,
    parameters: reducedTransparency,
};
