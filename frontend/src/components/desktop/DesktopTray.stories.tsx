import type { Meta, StoryObj } from '@storybook/react-vite';
import DesktopTray from './DesktopTray';

const meta = {
    title: 'Desktop/DesktopTray',
    component: DesktopTray,
    parameters: { layout: 'centered' },
} satisfies Meta<typeof DesktopTray>;

export default meta;
type Story = StoryObj<typeof meta>;

export const Default: Story = {};
