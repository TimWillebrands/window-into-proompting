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

export const ReservedForPersonas: Story = {
    args: {
        personaCount: 3,
        provider: {
            id: 'prov-3',
            type: 'openrouter',
            baseUrl: 'https://openrouter.ai/api/v1',
            apiKey: '••••ab12',
            modelName: 'anthropic/claude-opus-5.5',
            supportedComplexities: 1,
            isEnabled: true,
            reserved: true,
        },
    },
};

export const TailnetReady: Story = {
    args: {
        provider: {
            id: 'prov-4',
            type: 'ollama',
            baseUrl: 'http://100.91.200.46:11434',
            apiKey: null,
            modelName: 'd1:3b',
            supportedComplexities: 8,
            isEnabled: true,
            priority: -1,
            tailnet: 'home',
        },
        availability: {
            providerId: 'prov-4',
            state: 0,
            reason: null,
            until: null,
        },
    },
};

export const TailnetOffline: Story = {
    args: {
        provider: {
            id: 'prov-5',
            type: 'ollama',
            baseUrl: 'http://100.91.200.46:11434',
            apiKey: null,
            modelName: 'd1:3b',
            supportedComplexities: 8,
            isEnabled: true,
            priority: -1,
            tailnet: 'home',
        },
        availability: {
            providerId: 'prov-5',
            state: 2,
            reason: 'Connection refused (100.91.200.46:11434)',
            until: null,
        },
    },
};

export const BenchedOutOfCredit: Story = {
    args: {
        provider: {
            id: 'prov-6',
            type: 'openrouter',
            baseUrl: 'https://openrouter.ai/api/v1',
            apiKey: '••••ab12',
            modelName: '~typesafe/jev-latest',
            supportedComplexities: 8,
            isEnabled: true,
        },
        availability: {
            providerId: 'prov-6',
            state: 1,
            reason: 'Response status code does not indicate success: 402 (Payment Required).',
            until: '2026-11-07T14:32:00Z',
        },
    },
};
