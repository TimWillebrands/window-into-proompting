import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import type { StorybookConfig } from '@storybook/react-vite';
import tailwindcss from '@tailwindcss/vite';
import type { Plugin } from 'vite';

const __dirname = path.dirname(fileURLToPath(import.meta.url));

function serveXpCssAssets(): Plugin {
    const xpDist = path.resolve(__dirname, '../node_modules/xp.css/dist');
    const allFiles = fs
        .readdirSync(xpDist)
        .filter((f) => /\.(css|map|woff2?)$/.test(f));

    return {
        name: 'sb-serve-xp-css',
        configureServer(server) {
            server.middlewares.use((req, _res, next) => {
                const file = req.url?.split('?')[0]?.slice(1);
                if (file && allFiles.includes(file)) {
                    req.url = `/@fs/${path.resolve(xpDist, file)}`;
                }
                next();
            });
        },
    };
}

const config: StorybookConfig = {
    stories: ['../src/**/*.stories.@(ts|tsx)'],
    framework: {
        name: '@storybook/react-vite',
        options: {},
    },
    addons: [],
    typescript: {
        reactDocgen: 'react-docgen',
    },
    async viteFinal(config) {
        return {
            ...config,
            resolve: { ...(config.resolve ?? {}), tsconfigPaths: true },
            plugins: [
                ...(config.plugins ?? []),
                tailwindcss(),
                serveXpCssAssets(),
            ],
            css: { transformer: 'postcss' },
        };
    },
};

export default config;
