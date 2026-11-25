import { Icon, Window } from '@partytown/ui';
import type { Meta, StoryObj } from '@storybook/react-vite';
import { expect, fn, userEvent, within } from 'storybook/test';
import { useThemeStore } from '../../lib/theme-store';
import { DisplayProperties } from './DisplayApp';

const meta = {
    title: 'Apps/DisplayApp',
    component: DisplayProperties,
    parameters: { layout: 'fullscreen' },
    args: { onClose: fn() },
    decorators: [
        (Story) => {
            useThemeStore.setState({
                scheme: 'luna-blue',
                lastLight: 'luna-blue',
                mode: 'light',
            });
            return (
                <div className="h-[520px] w-[440px] p-6">
                    <Window
                        title="Display Properties"
                        icon={<Icon name="palette" />}
                        active
                        onClose={fn()}
                        className="h-full"
                    >
                        <Story />
                    </Window>
                </div>
            );
        },
    ],
} satisfies Meta<typeof DisplayProperties>;

export default meta;

type Story = StoryObj<typeof meta>;

export const Default: Story = {};

export const PreviewingOliveGreen: Story = {
    play: async ({ canvasElement }) => {
        const canvas = within(canvasElement);
        await userEvent.selectOptions(
            canvas.getByLabelText('Color scheme:'),
            'olive-green',
        );
        await expect(
            canvas.getByRole('button', { name: 'Apply' }),
        ).toBeEnabled();
    },
};

export const PreviewingSilver: Story = {
    play: async ({ canvasElement }) => {
        const canvas = within(canvasElement);
        await userEvent.selectOptions(
            canvas.getByLabelText('Color scheme:'),
            'silver',
        );
    },
};

export const PreviewingNoir: Story = {
    play: async ({ canvasElement }) => {
        const canvas = within(canvasElement);
        await userEvent.selectOptions(
            canvas.getByLabelText('Color scheme:'),
            'noir',
        );
    },
};
