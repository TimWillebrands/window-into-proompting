import type { Meta, StoryObj } from '@storybook/react-vite';
import { fn } from 'storybook/test';
import GeneratePersonaDialog from './GeneratePersonaDialog';

const meta = {
    title: 'Personas/GeneratePersonaDialog',
    component: GeneratePersonaDialog,
    parameters: { layout: 'fullscreen' },
    args: {
        onClose: fn(),
        onCreated: fn(),
    },
    decorators: [
        (Story) => (
            <div
                className="app-surface"
                style={{
                    position: 'relative',
                    width: 720,
                    height: 480,
                    background: '#ECE9D8',
                }}
            >
                <Story />
            </div>
        ),
    ],
} satisfies Meta<typeof GeneratePersonaDialog>;

export default meta;
type Story = StoryObj<typeof meta>;

export const Idle: Story = {};
