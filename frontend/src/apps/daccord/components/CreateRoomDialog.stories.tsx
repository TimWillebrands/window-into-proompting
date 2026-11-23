import type { Meta, StoryObj } from '@storybook/react-vite';
import { expect, fn, userEvent } from 'storybook/test';
import CreateRoomDialog from './CreateRoomDialog';

const meta = {
    title: 'Daccord/CreateRoomDialog',
    component: CreateRoomDialog,
    parameters: { layout: 'fullscreen' },
    args: {
        open: true,
        creating: false,
        errorMessage: null,
        onClose: fn(),
        onSubmit: fn(),
    },
    decorators: [
        (Story) => (
            <div className="relative h-[520px] w-[720px]">
                <Story />
            </div>
        ),
    ],
} satisfies Meta<typeof CreateRoomDialog>;

export default meta;
type Story = StoryObj<typeof meta>;

export const Idle: Story = {};

export const Creating: Story = {
    args: {
        creating: true,
    },
};

export const WithError: Story = {
    args: {
        errorMessage: 'Could not reach the server. Try again?',
    },
};

export const SubmitsTitleAndScenario: Story = {
    play: async ({ canvas, args }) => {
        const title = await canvas.findByLabelText(/title/i);
        await userEvent.type(title, 'Stealth HQ');

        const scenario = await canvas.findByLabelText(/scenario/i);
        await userEvent.type(scenario, 'Late-night planning session.');

        const submit = canvas.getByRole('button', { name: /create room/i });
        await userEvent.click(submit);

        await expect(args.onSubmit).toHaveBeenCalledWith(
            'Stealth HQ',
            'Late-night planning session.',
        );
    },
};

export const CancelClosesDialog: Story = {
    play: async ({ canvas, args }) => {
        const cancel = canvas.getByRole('button', { name: /cancel/i });
        await userEvent.click(cancel);
        await expect(args.onClose).toHaveBeenCalled();
    },
};
