import type { Meta, StoryObj } from '@storybook/react-vite';
import { inputStyle } from './constants';
import FormRow from './FormRow';

const meta = {
    title: 'ConfigPanel/FormRow',
    component: FormRow,
    parameters: {
        layout: 'centered',
        backgrounds: { default: 'window-body' },
    },
    decorators: [
        (Story) => (
            <div style={{ width: 360, padding: 12, background: '#ECE9D8' }}>
                <Story />
            </div>
        ),
    ],
} satisfies Meta<typeof FormRow>;

export default meta;
type Story = StoryObj<typeof meta>;

export const TextInput: Story = {
    args: {
        label: 'Base URL',
        children: (
            <input
                type="text"
                defaultValue="http://localhost:11434"
                style={inputStyle}
            />
        ),
    },
};

export const Select: Story = {
    args: {
        label: 'Type',
        children: (
            <select style={inputStyle} defaultValue="ollama">
                <option value="ollama">Ollama (local)</option>
                <option value="openrouter">OpenRouter</option>
            </select>
        ),
    },
};
