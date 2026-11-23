import type { Meta, StoryObj } from '@storybook/react-vite';
import { fn } from 'storybook/test';
import {
    useActiveGenerationPhases,
    useChatGroupGenerationState,
    useChatGroupMessages,
} from '#lib/realtime-store';
import {
    STORY_PLAYER,
    seeded,
    withStoryLives,
} from '../../components/chat/story-seed';
import WindowFrame from '../../components/desktop/WindowFrame';
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
        (Story) => (
            <div className="h-[520px] w-[600px] p-6">
                {/* biome-ignore lint/correctness/useUniqueElementIds: stories render single instance */}
                <WindowFrame
                    id="dm-ada"
                    title="Ada — Conversation"
                    icon="/img/icons/messenger.ico"
                    width={560}
                    height={480}
                    zIndex={1}
                    onMinimize={fn()}
                    onMaximize={fn()}
                    onClose={fn()}
                >
                    <Story />
                </WindowFrame>
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
