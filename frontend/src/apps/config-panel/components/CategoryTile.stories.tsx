import type { Meta, StoryObj } from '@storybook/react-vite';
import { fn } from 'storybook/test';
import CategoryTile from './CategoryTile';

const meta = {
    title: 'ConfigPanel/CategoryTile',
    component: CategoryTile,
    parameters: { layout: 'centered' },
    args: {
        icon: '🤖',
        label: 'Language Model Providers',
        description: 'Configure AI model endpoints and API keys',
        onClick: fn(),
    },
    decorators: [
        (Story) => (
            <div
                style={{
                    width: 320,
                    padding: 16,
                    background:
                        'linear-gradient(135deg, #5C87C2 0%, #4A73B0 100%)',
                }}
            >
                <Story />
            </div>
        ),
    ],
} satisfies Meta<typeof CategoryTile>;

export default meta;
type Story = StoryObj<typeof meta>;

export const Default: Story = {};
