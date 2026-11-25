import type { Meta, StoryObj } from '@storybook/react-vite';
import { expect, userEvent, within } from 'storybook/test';
import {
    useActiveGenerationPhases,
    useChatGroupGenerationState,
    useChatGroupMessages,
} from '#lib/realtime-store';
import {
    STORY_GROUP,
    STORY_PLAYER,
    seeded,
    withStoryLives,
} from '../../components/chat/story-seed';
import StoryWindow from '../../components/desktop/StoryWindow';
import type { RealtimeChatMessage } from '../../lib/realtime-store';
import MessengerChatWindow from './MessengerChatWindow';

const at = Date.now() - 600_000;

const line = (
    messageId: number,
    senderId: string,
    content: string,
    extra: Partial<RealtimeChatMessage> = {},
): RealtimeChatMessage => ({
    chatGroupId: 'dm-ada',
    messageId,
    content,
    reasoning: null,
    appraisal: null,
    error: null,
    senderType: senderId === STORY_PLAYER ? 'user' : 'assistant',
    senderId,
    sendAt: at + messageId * 30_000,
    metadata: null,
    generationEvents: [],
    ...extra,
});

const transcript = [
    line(1, 'persona-ada', 'hey Grace :)'),
    line(2, 'persona-ada', 'are you around tonight?'),
    line(3, STORY_PLAYER, 'yes! what did you have in mind?'),
    line(4, 'persona-ada', 'looks out of the window', { kind: 'emote' }),
    line(5, 'persona-ada', 'a *very* small picnic. bring the good mugs <3'),
    line(6, 'persona-ada', 'and maybe'),
];

const mock = (hook: unknown, value: unknown) =>
    (hook as { mockReturnValue: (v: unknown) => void }).mockReturnValue(value);

const meta = {
    title: 'Apps/MessengerChatWindow',
    component: MessengerChatWindow,
    parameters: { layout: 'fullscreen' },
    args: { chatGroupId: 'dm-ada', withName: 'Ada' },
    beforeEach: () => {
        withStoryLives();
        mock(useChatGroupMessages, transcript);
        mock(useChatGroupGenerationState, [6]);
        mock(useActiveGenerationPhases, []);
    },
    decorators: [
        (Story, { parameters }) => (
            <div
                className={
                    parameters.narrow
                        ? 'h-[560px] w-[400px] p-6'
                        : 'h-[560px] w-[600px] p-6'
                }
            >
                {/* biome-ignore lint/correctness/useUniqueElementIds: stories render single instance */}
                <StoryWindow
                    id="dm-ada"
                    title={
                        (parameters.title as string | undefined) ??
                        'Ada — Conversation'
                    }
                    icon={parameters.title ? 'chat-rooms' : 'chat'}
                    skin="msn"
                >
                    <Story />
                </StoryWindow>
            </div>
        ),
        seeded(),
    ],
} satisfies Meta<typeof MessengerChatWindow>;

export default meta;
type Story = StoryObj<typeof meta>;

export const Default: Story = {};

export const Spectating: Story = {
    args: { spectate: true, withName: 'Ada & Linus' },
};

export const Noir: Story = { globals: { scheme: 'noir' } };

export const Narrow: Story = { parameters: { narrow: true } };

export const NoConversation: Story = { args: { chatGroupId: undefined } };

const groupTranscript = [
    line(1, 'persona-ada', 'picnic tonight? the four of us', {
        chatGroupId: STORY_GROUP,
    }),
    line(2, 'persona-linus', 'only if Hedy brings the lemonade', {
        chatGroupId: STORY_GROUP,
    }),
    line(3, STORY_PLAYER, "I'm in! where?", { chatGroupId: STORY_GROUP }),
    line(4, 'persona-hedy', 'stretches and yawns', {
        chatGroupId: STORY_GROUP,
        kind: 'emote',
    }),
    line(5, 'persona-ada', 'the park, by the big tree :)', {
        chatGroupId: STORY_GROUP,
    }),
    line(6, 'persona-linus', 'bring the good mugs <3', {
        chatGroupId: STORY_GROUP,
    }),
];

const inGroup = () => {
    mock(useChatGroupMessages, groupTranscript);
    mock(useChatGroupGenerationState, []);
};

export const Group: Story = {
    args: { chatGroupId: STORY_GROUP, withName: 'Ada, Linus and Hedy' },
    parameters: { title: 'Conversation with Ada, Linus, Hedy' },
    beforeEach: inGroup,
};

export const GroupNoir: Story = { ...Group, globals: { scheme: 'noir' } };

export const GroupNarrow: Story = {
    ...Group,
    parameters: { ...Group.parameters, narrow: true },
};

const openInvite: Story['play'] = async ({ canvasElement }) => {
    await userEvent.click(
        await within(canvasElement).findByRole('button', { name: /Invite/ }),
    );
    await expect(
        await within(canvasElement).findByRole('dialog', {
            name: /Invite someone/,
        }),
    ).toBeInTheDocument();
};

export const InviteFromDm: Story = { play: openInvite };

export const InviteFromDmNoir: Story = {
    globals: { scheme: 'noir' },
    play: openInvite,
};

export const InviteToGroup: Story = { ...Group, play: openInvite };
