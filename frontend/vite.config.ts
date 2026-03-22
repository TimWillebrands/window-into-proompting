import { cloudflare } from '@cloudflare/vite-plugin';
import tailwindcss from '@tailwindcss/vite';
import { devtools } from '@tanstack/devtools-vite';
import { tanstackStart } from '@tanstack/react-start/plugin/vite';
import viteReact from '@vitejs/plugin-react';
import { defineConfig } from 'vite';
import viteTsConfigPaths from 'vite-tsconfig-paths';

export default defineConfig({
    plugins: [
        devtools(),
        cloudflare({ viteEnvironment: { name: 'ssr' } }),
        viteTsConfigPaths({
            projects: ['./tsconfig.json'],
        }),
        tailwindcss(),
        tanstackStart(),
        // React's vite plugin must come after start's vite plugin
        viteReact(),
    ],
    css: {
        transformer: 'postcss',
    },
    build: {
        rollupOptions: {
            external: (id) => id.includes('XP.css'),
        },
    },
});
