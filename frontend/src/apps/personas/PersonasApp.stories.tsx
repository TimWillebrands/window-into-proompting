import type { Meta, StoryObj } from '@storybook/react-vite';
import { userEvent, within } from 'storybook/test';
import StoryWindow from '../../components/desktop/StoryWindow';
import PersonasApp from './PersonasApp';

const meta = {
    title: 'Apps/PersonasApp',
    component: PersonasApp,
    parameters: { layout: 'fullscreen' },
    decorators: [
        (Story, { parameters }) => (
            <div
                className={
                    parameters.narrow
                        ? 'h-[560px] w-[500px] p-6'
                        : 'h-[520px] w-[760px] p-6'
                }
            >
                {/* biome-ignore lint/correctness/useUniqueElementIds: stories render single instance */}
                <StoryWindow
                    id="personas"
                    title="Persona Manager"
                    icon="personas"
                >
                    <Story />
                </StoryWindow>
            </div>
        ),
    ],
} satisfies Meta<typeof PersonasApp>;

export default meta;
type Story = StoryObj<typeof meta>;

export const Default: Story = {};

export const Editing: Story = {
    play: async ({ canvasElement }) => {
        await userEvent.click(
            await within(canvasElement).findByText('Indie Hacker Girl'),
        );
    },
};

export const Narrow: Story = { parameters: { narrow: true } };

export const NarrowEditing: Story = {
    ...Editing,
    parameters: { narrow: true },
};
