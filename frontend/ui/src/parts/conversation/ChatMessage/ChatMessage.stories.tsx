import type { Meta, StoryObj } from '@storybook/react-vite';
import { fn } from 'storybook/test';
import { Toolbar, ToolbarButton } from '../../controls';
import {
    noir,
    reducedMotion,
    reducedTransparency,
    stage,
} from '../../controls/story-kit';
import { Text } from '../../controls/Text/Text';
import { Icon } from '../../icons';
import { ChatLog } from '../ChatLog/ChatLog';
import { RichText } from '../RichText/RichText';
import { picture } from '../story-kit';
import { UserPicture } from '../UserPicture/UserPicture';
import { ChatMessage } from './ChatMessage';

const meta = {
    title: 'Conversation/ChatMessage',
    component: ChatMessage,
    decorators: [stage],
    args: { sender: 'Ada' },
} satisfies Meta<typeof ChatMessage>;

export default meta;

type Story = StoryObj<typeof meta>;

const actions = (
    <Toolbar labels="none" aria-label="Message actions">
        <ToolbarButton
            icon={<Icon name="refresh" />}
            aria-label="Redo"
            title="Redo"
            onClick={fn()}
        />
        <ToolbarButton
            icon={<Icon name="delete" />}
            aria-label="Delete"
            title="Delete"
            onClick={fn()}
        />
    </Toolbar>
);

export const Default: Story = {
    render: () => (
        <ChatLog className="ui-story-chat-w-420 ui-story-chat-h-300">
            <ChatMessage
                sender="Ada"
                time="21:04"
                picture={<UserPicture src={picture(210)} size={24} />}
                actions={actions}
            >
                <RichText>
                    <p>
                        Has anyone seen the <strong>kettle</strong>? It was here
                        a minute ago.
                    </p>
                </RichText>
            </ChatMessage>
            <ChatMessage
                sender="Grace (you)"
                own
                time="21:05"
                picture={<UserPicture src={picture(30)} size={24} />}
                actions={actions}
            >
                <RichText>
                    <p>Check the cupboard by the window.</p>
                </RichText>
            </ChatMessage>
            <ChatMessage kind="emote" sender="Ada">
                looks in the cupboard.
            </ChatMessage>
            <ChatMessage
                sender="Appraisal"
                tone="caution"
                time="21:06"
                picture={<Icon name="scales" />}
            >
                Ada stays quiet: nothing to add.
            </ChatMessage>
            <ChatMessage
                sender="Linus"
                live
                picture={<UserPicture src={picture(120)} size={24} />}
                time="(48 chars)"
            >
                <Text tone="muted">typing…</Text>
            </ChatMessage>
            <ChatMessage
                sender="Linus"
                picture={<UserPicture src={picture(120)} size={24} />}
                error="The model did not answer."
            />
        </ChatLog>
    ),
};

export const YourTurn: Story = {
    render: () => (
        <ChatLog className="ui-story-chat-w-420">
            <ChatMessage
                tone="neutral"
                sender="Your turn, Grace"
                picture={<Icon name="film" />}
                data-ui-preview="hover"
                actions={actions}
            >
                Tell Ada where the kettle is.
                <Text as="p" tone="muted" italic>
                    She asked you directly.
                </Text>
            </ChatMessage>
        </ChatLog>
    ),
};

export const WithDetails: Story = {
    render: () => (
        <ChatLog className="ui-story-chat-w-420">
            <ChatMessage
                sender="Ada"
                time="21:04"
                picture={<UserPicture src={picture(210)} size={24} />}
                details={
                    <>
                        <div>
                            <Text as="div" tone="neutral" strong>
                                Model
                            </Text>
                            <Text as="div">openrouter / tiny-model</Text>
                        </div>
                        <div>
                            <Text as="div" tone="neutral" strong>
                                Reasoning
                            </Text>
                            <Text as="div" tone="muted">
                                The kettle came up twice.
                            </Text>
                        </div>
                    </>
                }
            >
                <RichText>
                    <p>It was by the window.</p>
                </RichText>
            </ChatMessage>
        </ChatLog>
    ),
};

export const Transcript: Story = {
    render: () => (
        <ChatLog className="ui-story-chat-w-420 ui-story-chat-h-300">
            <ChatMessage layout="transcript" sender="Ada says:">
                hi there :)
            </ChatMessage>
            <ChatMessage layout="transcript" sender="Ada says:" continued>
                are you around tonight?
            </ChatMessage>
            <ChatMessage layout="transcript" sender="Grace says:" own>
                yes! what's up
            </ChatMessage>
            <ChatMessage layout="transcript" kind="emote" sender="* Ada">
                waves
            </ChatMessage>
            <ChatMessage
                layout="transcript"
                sender="Ada says:"
                error="Ada couldn't reply right now."
            />
            <ChatMessage layout="transcript" sender="Ada says:" live>
                let me think
            </ChatMessage>
        </ChatLog>
    ),
};

export const Talk: Story = {
    render: () => (
        <ChatLog
            aria-label="Said out loud here"
            className="ui-story-chat-w-420 ui-story-chat-h-300"
        >
            <ChatMessage
                layout="transcript"
                sender="Ana"
                colour="var(--ui-series-1)"
            >
                Has anyone seen the new mural by the canal?
            </ChatMessage>
            <ChatMessage
                layout="transcript"
                sender={
                    <>
                        Bo <Icon name="whisper" label="whispers" />
                    </>
                }
                colour="var(--ui-series-2)"
                manner="quiet"
            >
                I painted the left half, don't tell anyone.
            </ChatMessage>
            <ChatMessage
                layout="transcript"
                sender={
                    <>
                        Cy <Icon name="megaphone" label="shouts" />
                    </>
                }
                colour="var(--ui-series-3)"
                manner="loud"
            >
                Last orders!
            </ChatMessage>
            <ChatMessage
                layout="transcript"
                sender="Dee"
                colour="var(--ui-series-4)"
                manner="faint"
            >
                mmh hm mmm hmm
            </ChatMessage>
            <ChatMessage
                layout="transcript"
                kind="emote"
                manner="faint"
                sender="Dee, Eli"
            >
                talking out of earshot · 4 lines
            </ChatMessage>
            <Text as="p" tone="muted" italic>
                Bo is about to say something…
            </Text>
        </ChatLog>
    ),
};

export const Noir: Story = { ...Default, parameters: noir };
export const NoirTalk: Story = { ...Talk, parameters: noir };
export const NoirTranscript: Story = { ...Transcript, parameters: noir };
export const ReducedMotion: Story = { ...Default, parameters: reducedMotion };
export const ReducedTransparency: Story = {
    ...Default,
    parameters: reducedTransparency,
};
