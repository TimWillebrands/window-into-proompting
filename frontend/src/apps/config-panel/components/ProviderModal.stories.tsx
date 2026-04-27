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
        children: (
            <div style={{ padding: 16 }}>
                <p>Modal body content. Click outside or press Esc to close.</p>
                <button type="button">Save</button>
            </div>
        ),
    },
} satisfies Meta<typeof ProviderModal>;

export default meta;
type Story = StoryObj<typeof meta>;

export const Default: Story = {};
