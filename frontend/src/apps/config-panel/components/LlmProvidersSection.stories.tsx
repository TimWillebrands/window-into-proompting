import type { Meta, StoryObj } from '@storybook/react-vite';
import LlmProvidersSection from './LlmProvidersSection';

const meta = {
    title: 'ConfigPanel/LlmProvidersSection',
    component: LlmProvidersSection,
    parameters: { layout: 'fullscreen' },
    decorators: [
        (Story) => (
            <div
                style={{
                    width: 800,
                    minHeight: 480,
                    padding: 20,
                    background:
                        'linear-gradient(135deg, #5C87C2 0%, #4A73B0 100%)',
                }}
            >
                <Story />
            </div>
        ),
    ],
} satisfies Meta<typeof LlmProvidersSection>;

export default meta;
type Story = StoryObj<typeof meta>;

export const Default: Story = {};
