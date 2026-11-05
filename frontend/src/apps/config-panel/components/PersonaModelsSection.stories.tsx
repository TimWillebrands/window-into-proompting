import type { Meta, StoryObj } from '@storybook/react-vite';
import { fn } from 'storybook/test';
import PersonaModelsSection from './PersonaModelsSection';

const meta = {
    title: 'ConfigPanel/PersonaModelsSection',
    component: PersonaModelsSection,
    parameters: { layout: 'fullscreen' },
    args: { onOpenProviders: fn() },
    decorators: [
        (Story) => (
            <div
                style={{
                    width: 720,
                    padding: 20,
                    background:
                        'linear-gradient(135deg, #5C87C2 0%, #4A73B0 100%)',
                }}
            >
                <Story />
            </div>
        ),
    ],
} satisfies Meta<typeof PersonaModelsSection>;

export default meta;
type Story = StoryObj<typeof meta>;

export const Default: Story = {};
