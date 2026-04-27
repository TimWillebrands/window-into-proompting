import type { Meta, StoryObj } from '@storybook/react-vite';
import { fn } from 'storybook/test';
import NewPersonaButton from './NewPersonaButton';

const meta = {
    title: 'Personas/NewPersonaButton',
    component: NewPersonaButton,
    parameters: {
        layout: 'centered',
        backgrounds: { default: 'window-body' },
    },
    args: { onCreated: fn() },
    decorators: [
        (Story) => (
            <div style={{ width: 200, padding: 8, background: '#ECE9D8' }}>
                <Story />
            </div>
        ),
    ],
} satisfies Meta<typeof NewPersonaButton>;

export default meta;
type Story = StoryObj<typeof meta>;

export const Default: Story = {};
