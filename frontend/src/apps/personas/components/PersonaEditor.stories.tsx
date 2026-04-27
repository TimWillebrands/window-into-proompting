import type { Meta, StoryObj } from '@storybook/react-vite';
import { fn } from 'storybook/test';
import PersonaEditor from './PersonaEditor';

const meta = {
    title: 'Personas/PersonaEditor',
    component: PersonaEditor,
    parameters: {
        layout: 'centered',
        backgrounds: { default: 'window-body' },
    },
    args: { onDeleted: fn() },
    decorators: [
        (Story) => (
            <div
                className="app-surface"
                style={{ width: 560, padding: 12, background: '#ECE9D8' }}
            >
                <Story />
            </div>
        ),
    ],
} satisfies Meta<typeof PersonaEditor>;

export default meta;
type Story = StoryObj<typeof meta>;

export const Populated: Story = {
    args: {
        persona: {
            id: 'persona-1',
            name: 'Indie Hacker Girl',
            bio: 'Sharp-witted millennial, builds fast.',
            systemPrompt: 'You are Denise. Brief, critical, action-led.',
        },
    },
};

export const Empty: Story = {
    args: {
        persona: {
            id: 'new-persona',
            name: '',
            bio: null,
            systemPrompt: '',
        },
    },
};
