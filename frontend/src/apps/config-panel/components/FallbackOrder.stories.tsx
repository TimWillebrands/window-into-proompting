import type { Meta, StoryObj } from '@storybook/react-vite';
import FallbackOrder from './FallbackOrder';

const meta = {
    title: 'ConfigPanel/FallbackOrder',
    component: FallbackOrder,
    parameters: { layout: 'centered' },
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
} satisfies Meta<typeof FallbackOrder>;

export default meta;
type Story = StoryObj<typeof meta>;

const providers = [
    {
        id: 'd1',
        type: 'ollama',
        baseUrl: 'http://100.91.200.46:11434',
        apiKey: null,
        modelName: 'd1:3b',
        supportedComplexities: 8,
        isEnabled: true,
        priority: -1,
        tailnet: 'home',
    },
    {
        id: 'jev',
        type: 'openrouter',
        baseUrl: 'https://openrouter.ai/api/v1',
        apiKey: '••••ab12',
        modelName: '~typesafe/jev-latest',
        supportedComplexities: 8,
        isEnabled: true,
        priority: 0,
    },
    {
        id: 'haiku',
        type: 'anthropic',
        baseUrl: 'https://api.anthropic.com',
        apiKey: '••••cd34',
        modelName: 'claude-haiku-5-5',
        supportedComplexities: 7,
        isEnabled: true,
        priority: -1,
    },
    {
        id: 'deepseek',
        type: 'openrouter',
        baseUrl: 'https://openrouter.ai/api/v1',
        apiKey: '••••ab12',
        modelName: 'deepseek/deepseek-v4.1-flash',
        supportedComplexities: 5,
        isEnabled: true,
        priority: 0,
    },
];

export const AllReady: Story = {
    args: { providers },
};

export const LocalBoxAsleep: Story = {
    args: {
        providers,
        availability: [
            {
                providerId: 'd1',
                state: 2,
                reason: 'Connection refused',
                until: null,
            },
            {
                providerId: 'jev',
                state: 1,
                reason: '402 (Payment Required)',
                until: '2026-11-07T14:32:00Z',
            },
        ],
    },
};

export const NoDecisionModel: Story = {
    args: { providers: providers.filter((p) => p.supportedComplexities !== 8) },
};
