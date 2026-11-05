import type { Meta, StoryObj } from '@storybook/react-vite';
import { expect, fn, within } from 'storybook/test';
import type { LlmProviderEntry } from '../../../api/model/llmProviderEntry';
import PersonaModelCard from './PersonaModelCard';

const persona = { id: 'persona-1', name: 'Vlad' };

const providers: LlmProviderEntry[] = [
    {
        id: 'shared',
        type: 'ollama',
        baseUrl: 'http://localhost:11434',
        modelName: 'llama3.2',
        supportedComplexities: 7,
        isEnabled: true,
    },
    {
        id: 'reserved',
        type: 'openrouter',
        baseUrl: 'https://openrouter.ai/api/v1',
        modelName: 'anthropic/claude-opus-5.5',
        supportedComplexities: 1,
        isEnabled: true,
        reserved: true,
    },
    {
        id: 'off',
        type: 'openrouter',
        baseUrl: 'https://openrouter.ai/api/v1',
        modelName: 'deepseek/deepseek-v4.1-flash',
        supportedComplexities: 1,
        isEnabled: false,
    },
];

const meta = {
    title: 'ConfigPanel/PersonaModelCard',
    component: PersonaModelCard,
    parameters: { layout: 'centered' },
    args: {
        persona,
        providers,
        routes: [],
        onChanged: fn(),
        onRemoved: fn(),
    },
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
} satisfies Meta<typeof PersonaModelCard>;

export default meta;
type Story = StoryObj<typeof meta>;

export const OnSharedProviders: Story = {};

export const OwnVoice: Story = {
    args: {
        routes: [{ personaId: persona.id, tier: 2, providerId: 'reserved' }],
    },
};

export const DisabledProvider: Story = {
    args: {
        routes: [{ personaId: persona.id, tier: 4, providerId: 'off' }],
    },
    play: async ({ canvasElement }) => {
        await expect(
            within(canvasElement).getByText(/Using shared providers/),
        ).toBeVisible();
    },
};
