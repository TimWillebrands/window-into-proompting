import type { StorybookConfig } from '@storybook/react-vite';
import react from '@vitejs/plugin-react';

const config: StorybookConfig = {
    stories: ['../src/**/*.stories.@(ts|tsx)'],
    staticDirs: [{ from: './backdrops', to: '/backdrops' }],
    framework: {
        name: '@storybook/react-vite',
        options: {},
    },
    addons: [
        '@storybook/addon-a11y',
        '@storybook/addon-vitest',
        '@storybook/addon-mcp',
    ],
    typescript: {
        reactDocgen: 'react-docgen',
    },
    async viteFinal(config) {
        return {
            ...config,
            plugins: [...(config.plugins ?? []), react()],
        };
    },
};

export default config;
