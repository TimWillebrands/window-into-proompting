import type { Meta, StoryObj } from '@storybook/react-vite';
import { useConsentPrompt } from '../../lib/analytics';
import CookieBalloon from './CookieBalloon';

const meta = {
    title: 'Desktop/CookieBalloon',
    component: CookieBalloon,
    parameters: {
        layout: 'fullscreen',
        backgrounds: { default: 'xp-desktop' },
    },
    decorators: [
        (Story) => {
            useConsentPrompt.setState({ open: true });
            return <Story />;
        },
    ],
} satisfies Meta<typeof CookieBalloon>;

export default meta;

export const Open: StoryObj<typeof meta> = {};
