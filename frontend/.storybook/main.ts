import type { StorybookConfig } from '@storybook/react-vite';
import tailwindcss from '@tailwindcss/vite';

const config: StorybookConfig = {
    stories: ['../src/**/*.stories.@(ts|tsx)'],
    staticDirs: ['../public'],
    framework: {
        name: '@storybook/react-vite',
        options: {},
    },
    addons: ['@storybook/addon-mcp'],
    refs: (_config, { configType }) =>
        configType === 'DEVELOPMENT'
            ? { ui: { title: 'Partytown UI', url: 'http://localhost:6006' } }
            : {},
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
