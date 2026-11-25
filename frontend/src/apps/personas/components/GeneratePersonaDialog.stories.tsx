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
            <div className="relative h-[480px] w-[720px]">
                <Story />
            </div>
        ),
    ],
} satisfies Meta<typeof GeneratePersonaDialog>;

export default meta;
type Story = StoryObj<typeof meta>;

export const Idle: Story = {};
