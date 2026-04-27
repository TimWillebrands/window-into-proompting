import type { Meta, StoryObj } from '@storybook/react-vite';
import { fn } from 'storybook/test';
import ProviderEditor from './ProviderEditor';

const meta = {
    title: 'ConfigPanel/ProviderEditor',
    component: ProviderEditor,
    parameters: { layout: 'centered' },
    args: {
        isNew: false,
        onClose: fn(),
    },
    decorators: [
        (Story) => (
            <div
                className="window"
                style={{ width: 480, background: '#ECE9D8' }}
            >
                <div className="window-body" style={{ padding: 12 }}>
                    <Story />
                </div>
            </div>
        ),
    ],
} satisfies Meta<typeof ProviderEditor>;

export default meta;
type Story = StoryObj<typeof meta>;

export const EditOllama: Story = {
    args: {
        entry: {
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

export const NewOpenRouter: Story = {
    args: {
        isNew: true,
        entry: {
            type: 'openrouter',
            baseUrl: 'https://openrouter.ai/api/v1',
            apiKey: '',
            modelName: null,
            supportedComplexities: 7,
            isEnabled: true,
        },
    },
};
