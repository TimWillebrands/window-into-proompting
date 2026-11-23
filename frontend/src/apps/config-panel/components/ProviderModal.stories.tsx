import { CommandButton } from '@partytown/ui';
import type { Meta, StoryObj } from '@storybook/react-vite';
import { fn } from 'storybook/test';
import ProviderModal from './ProviderModal';

const meta = {
    title: 'ConfigPanel/ProviderModal',
    component: ProviderModal,
    parameters: { layout: 'fullscreen' },
    args: {
        title: 'Edit Provider',
        onClose: fn(),
        buttons: (
            <>
                <CommandButton isDefault type="button">
                    Save
                </CommandButton>
                <CommandButton>Cancel</CommandButton>
            </>
        ),
        children: <p>Modal body content. Press Esc to close.</p>,
    },
    decorators: [
        (Story) => (
            <div className="h-[480px]">
                <Story />
            </div>
        ),
    ],
} satisfies Meta<typeof ProviderModal>;

export default meta;
type Story = StoryObj<typeof meta>;

export const Default: Story = {};
