import { Icon, Window } from '@partytown/ui';
import type { Meta, StoryObj } from '@storybook/react-vite';
import { fn } from 'storybook/test';
import PersonaEditor from './PersonaEditor';

const meta = {
    title: 'Personas/PersonaEditor',
    component: PersonaEditor,
    parameters: {
        layout: 'centered',
    },
    globals: { backgrounds: { value: 'window-body' } },
    args: { onDeleted: fn() },
    decorators: [
        (Story) => (
            <Window
                title="Persona Manager"
                icon={<Icon name="personas" />}
                className="w-[600px]"
                bodyClassName="overflow-auto"
            >
                <div className="p-3">
                    <Story />
                </div>
            </Window>
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
