import type { Meta, StoryObj } from '@storybook/react-vite';
import { fn } from 'storybook/test';
import TemplateButton from './TemplateButton';

const meta = {
    title: 'Personas/TemplateButton',
    component: TemplateButton,
    parameters: {
        layout: 'centered',
        backgrounds: { default: 'window-body' },
    },
    args: { onCreated: fn() },
    decorators: [
        (Story) => (
            <div style={{ width: 200, padding: 80, background: '#ECE9D8' }}>
                <Story />
            </div>
        ),
    ],
} satisfies Meta<typeof TemplateButton>;

export default meta;
type Story = StoryObj<typeof meta>;

export const Closed: Story = {};
