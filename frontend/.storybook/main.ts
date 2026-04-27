import type { StorybookConfig } from '@storybook/react-vite';
import tailwindcss from '@tailwindcss/vite';

const config: StorybookConfig = {
    stories: ['../src/**/*.stories.@(ts|tsx)'],
    framework: {
        name: '@storybook/react-vite',
        options: {},
    },
    addons: ['@storybook/addon-mcp'],
    typescript: {
        reactDocgen: 'react-docgen',
    },
    async viteFinal(config) {
        return {
            ...config,
            resolve: { ...(config.resolve ?? {}), tsconfigPaths: true },
            plugins: [...(config.plugins ?? []), tailwindcss()],
            css: { transformer: 'postcss' },
        };
    },
};

export default config;
