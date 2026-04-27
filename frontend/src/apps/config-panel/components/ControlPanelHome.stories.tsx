import type { Meta, StoryObj } from '@storybook/react-vite';
import { fn } from 'storybook/test';
import ControlPanelHome from './ControlPanelHome';

const meta = {
    title: 'ConfigPanel/ControlPanelHome',
    component: ControlPanelHome,
    parameters: { layout: 'fullscreen' },
    args: { onNavigate: fn() },
    decorators: [
        (Story) => (
            <div
                style={{
                    width: 720,
                    height: 480,
                    padding: 20,
                    background:
                        'linear-gradient(135deg, #5C87C2 0%, #4A73B0 100%)',
                }}
            >
                <Story />
            </div>
        ),
    ],
} satisfies Meta<typeof ControlPanelHome>;

export default meta;
type Story = StoryObj<typeof meta>;

export const Default: Story = {};
