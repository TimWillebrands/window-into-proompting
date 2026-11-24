import type { Meta, StoryObj } from '@storybook/react-vite';
import {
    getGetPartyIdChatGroupsChatGroupIdDriverOverridesQueryKey,
    getGetPartyIdChatGroupsChatGroupIdParticipantsQueryKey,
} from '#api/party-zone';
import {
    useActiveGenerationPhases,
    useChatGroupGenerationState,
    useChatGroupMessages,
    useDeclinedDecisions,
} from '#lib/realtime-store';
import { DriverKind } from '../../api/model';
import { seeded } from '../../components/chat/story-seed';
import StoryWindow from '../../components/desktop/StoryWindow';
import type { RealtimeChatMessage } from '../../lib/realtime-store';
import { WORLD_ID } from '../../lib/world';
import { ChatView } from './GroupChatWindow';

const at = Date.parse('2026-04-22T09:00:00Z');

const message = (
    messageId: number,
    senderId: string,
    content: string,
    extra: Partial<RealtimeChatMessage> = {},
): RealtimeChatMessage => ({
    chatGroupId: 'cg-1',
    messageId,
    content,
    reasoning: null,
    appraisal: null,
    error: null,
    senderType: senderId === 'persona-3' ? 'user' : 'assistant',
    senderId,
    sendAt: at + messageId * 30_000,
    metadata: null,
    generationEvents: [],
    ...extra,
});

const messages = [
    message(1, 'persona-3', 'Welcome to the standup. Status updates please.'),
    message(
        2,
        'persona-1',
        'Shipped the **seedling tracker** yesterday.\n\n- dial in the watering heuristic\n- write the `README`',
        {
            reasoning: 'Indie hacker mode — concise, action-led updates.',
            metadata: { provider: 'openrouter', modelName: 'tiny-model' },
        } as Partial<RealtimeChatMessage>,
    ),
    message(3, 'persona-2', 'nods slowly, sensing the sprint energy.', {
        kind: 'emote',
    }),
    message(4, 'persona-2', '', {
        appraisal: JSON.stringify({
            personaId: 'persona-2',
            stop: true,
            reason: 'Nothing to add yet.',
            instruction: 'Wait for a question.',
        }),
    }),
    message(5, 'persona-2', 'The backlog is a river; we merely'),
];

const mock = (hook: unknown, value: unknown) =>
    (hook as { mockReturnValue: (v: unknown) => void }).mockReturnValue(value);

const meta = {
    title: 'Daccord/ChatView',
    component: ChatView,
    parameters: { layout: 'fullscreen' },
    args: { chatGroupId: 'cg-1' },
    beforeEach: () => {
        mock(useChatGroupMessages, messages);
        mock(useChatGroupGenerationState, [5]);
        mock(useActiveGenerationPhases, [
            {
                phase: 'streaming',
                personaName: 'persona-2',
                messageId: 5,
                charCount: 33,
            },
        ]);
        mock(useDeclinedDecisions, [
            {
                messageId: 3,
                personaId: 'persona-1',
                reason: 'Already gave an update.',
            },
        ]);
    },
    decorators: [
        (Story, { parameters }) => (
            <div
                className={
                    parameters.narrow
                        ? 'h-[600px] w-[500px] p-6'
                        : 'h-[600px] w-[920px] p-6'
                }
            >
                {/* biome-ignore lint/correctness/useUniqueElementIds: stories render single instance */}
                <StoryWindow
                    id="room"
                    title="D'Accord — Standup"
                    icon="/img/icons/chat-rooms.ico"
                >
                    <Story />
                </StoryWindow>
            </div>
        ),
        seeded([
            [
                getGetPartyIdChatGroupsChatGroupIdParticipantsQueryKey(
                    WORLD_ID,
                    'cg-1',
                ),
                ['persona-1', 'persona-2', 'persona-3'],
            ],
            [
                getGetPartyIdChatGroupsChatGroupIdDriverOverridesQueryKey(
                    WORLD_ID,
                    'cg-1',
                ),
                [{ personaId: 'persona-3', kind: DriverKind.User }],
            ],
        ]),
    ],
} satisfies Meta<typeof ChatView>;

export default meta;
type Story = StoryObj<typeof meta>;

export const Default: Story = {};

export const Narrow: Story = { parameters: { narrow: true } };
