import type { Meta, StoryObj } from '@storybook/react-vite';
import DesktopWallpaper from './DesktopWallpaper';

const meta = {
    title: 'Desktop/DesktopWallpaper',
    component: DesktopWallpaper,
    parameters: { layout: 'fullscreen' },
    decorators: [
        (Story) => (
            <div style={{ position: 'relative', width: 800, height: 600 }}>
                <Story />
            </div>
        ),
    ],
} satisfies Meta<typeof DesktopWallpaper>;

export default meta;
type Story = StoryObj<typeof meta>;

export const Default: Story = {};
