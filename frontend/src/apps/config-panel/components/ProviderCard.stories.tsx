import type { Meta, StoryObj } from '@storybook/react-vite';
import { fn } from 'storybook/test';
import ProviderCard from './ProviderCard';

const meta = {
    title: 'ConfigPanel/ProviderCard',
    component: ProviderCard,
    parameters: { layout: 'centered' },
    args: {
        onEdit: fn(),
        onDeleted: fn(),
        onToggled: fn(),
    },
    decorators: [
        (Story) => (
            <div
                style={{
                    width: 640,
                    padding: 16,
                    background:
                        'linear-gradient(135deg, #5C87C2 0%, #4A73B0 100%)',
                }}
            >
                <Story />
            </div>
        ),
    ],
} satisfies Meta<typeof ProviderCard>;

export default meta;
type Story = StoryObj<typeof meta>;

export const OllamaEnabled: Story = {
    args: {
        provider: {
            id: 'prov-1',
            type: 'ollama',
            baseUrl: 'http://localhost:11434',
            apiKey: null,
            modelName: 'llama3.2',
            supportedComplexities: 1,
            isEnabled: true,
        },
    },
};

export const OpenRouterDisabled: Story = {
    args: {
        provider: {
            id: 'prov-2',
            type: 'openrouter',
            baseUrl: 'https://openrouter.ai/api/v1',
            apiKey: 'sk-or-…',
            modelName: 'nvidia/llama-3.1-nemotron-ultra-253b-v1:free',
            supportedComplexities: 7,
            isEnabled: false,
        },
    },
};

export const NoModel: Story = {
    args: {
        provider: {
            id: 'prov-3',
            type: 'ollama',
            baseUrl: 'http://localhost:11434',
            apiKey: null,
            modelName: null,
            supportedComplexities: 1,
            isEnabled: true,
        },
    },
};
