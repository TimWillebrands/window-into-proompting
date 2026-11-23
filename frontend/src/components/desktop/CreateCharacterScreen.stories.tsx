import type { Meta, StoryObj } from '@storybook/react-vite';
import CreateCharacterScreen from './CreateCharacterScreen';

const meta = {
    title: 'Desktop/CreateCharacterScreen',
    component: CreateCharacterScreen,
    parameters: { layout: 'fullscreen' },
} satisfies Meta<typeof CreateCharacterScreen>;

export default meta;
type Story = StoryObj<typeof meta>;

export const Default: Story = {};
