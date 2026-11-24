import type { Meta, StoryObj } from '@storybook/react-vite';
import { expect, userEvent, within } from 'storybook/test';
import { seeded, withStoryLives } from '../../components/chat/story-seed';
import StoryWindow from '../../components/desktop/StoryWindow';
import MessengerApp from './MessengerApp';

const meta = {
    title: 'Apps/MessengerApp',
    component: MessengerApp,
    parameters: { layout: 'fullscreen' },
    beforeEach: withStoryLives,
    decorators: [
        (Story) => (
            <div className="h-[640px] w-[360px] p-6">
                {/* biome-ignore lint/correctness/useUniqueElementIds: stories render single instance */}
                <StoryWindow
                    id="messenger"
                    title="Messenger"
                    icon="messenger"
                    skin="msn"
                >
                    <Story />
                </StoryWindow>
            </div>
        ),
        seeded(),
    ],
} satisfies Meta<typeof MessengerApp>;

export default meta;
type Story = StoryObj<typeof meta>;

export const Default: Story = {};

const pickSeveral: Story['play'] = async ({ canvasElement }) => {
    const item = (id: string) => {
        const el = canvasElement.querySelector<HTMLElement>(
            `[data-buddy-item="${id}"]`,
        );
        if (!el) throw new Error(`no contact ${id}`);
        return el;
    };
    const user = userEvent.setup();
    await user.click(item('persona-linus'));
    await user.keyboard('{Control>}');
    await user.click(item('persona-ada'));
    await user.keyboard('{/Control}');
    await expect(item('persona-linus')).toHaveAttribute(
        'aria-selected',
        'true',
    );
    await expect(item('persona-ada')).toHaveAttribute('aria-selected', 'true');
    await expect(
        within(canvasElement).getByRole('button', {
            name: /Start a conversation/,
        }),
    ).toBeEnabled();
};

export const PickingSeveral: Story = { play: pickSeveral };

export const Noir: Story = { globals: { scheme: 'noir' } };

export const NoirPickingSeveral: Story = {
    globals: { scheme: 'noir' },
    play: pickSeveral,
};
