import type { Meta, StoryObj } from '@storybook/react-vite';
import { fn } from 'storybook/test';
import MoveInDialog from './MoveInDialog';

const meta = {
    title: 'Personas/MoveInDialog',
    component: MoveInDialog,
    parameters: { layout: 'fullscreen' },
    args: {
        persona: {
            id: 'persona-1',
            name: 'Mara',
            systemPrompt: 'You are Mara.',
        },
        onClose: fn(),
    },
    decorators: [
        (Story) => (
            <div style={{ position: 'relative', height: 320 }}>
                <Story />
            </div>
        ),
    ],
} satisfies Meta<typeof MoveInDialog>;

export default meta;
type Story = StoryObj<typeof meta>;

export const Default: Story = {};
