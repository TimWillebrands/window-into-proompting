import type { Meta, StoryObj } from '@storybook/react-vite';
import { useWorldChoice } from '../../lib/world';
import WelcomeScreen from './WelcomeScreen';

const meta = {
    title: 'Desktop/WelcomeScreen',
    component: WelcomeScreen,
    parameters: { layout: 'fullscreen' },
} satisfies Meta<typeof WelcomeScreen>;

export default meta;
type Story = StoryObj<typeof meta>;

export const Default: Story = {
    beforeEach: () => {
        useWorldChoice.setState({ id: null });
    },
};

export const Picked: Story = {
    beforeEach: () => {
        useWorldChoice.setState({ id: 'world-1' });
    },
};
