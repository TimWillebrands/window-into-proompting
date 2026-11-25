import { Icon, Window } from '@partytown/ui';
import type { Meta, StoryObj } from '@storybook/react-vite';
import { fn } from 'storybook/test';
import NewPersonaButton from './NewPersonaButton';

const meta = {
    title: 'Personas/NewPersonaButton',
    component: NewPersonaButton,
    parameters: {
        layout: 'centered',
    },
    globals: { backgrounds: { value: 'window-body' } },
    args: { onCreated: fn() },
    decorators: [
        (Story) => (
            <Window
                title="Persona Manager"
                icon={<Icon name="personas" />}
                className="w-[240px]"
                bodyClassName="overflow-auto"
            >
                <div className="p-2">
                    <Story />
                </div>
            </Window>
        ),
    ],
} satisfies Meta<typeof NewPersonaButton>;

export default meta;
type Story = StoryObj<typeof meta>;

export const Default: Story = {};
