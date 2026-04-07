import tailwindcss from '@tailwindcss/vite';
import { devtools } from '@tanstack/devtools-vite';
import { tanstackStart } from '@tanstack/react-start/plugin/vite';
import viteReact from '@vitejs/plugin-react';
import { defineConfig } from 'vite';
import viteTsConfigPaths from 'vite-tsconfig-paths';
import { nitro } from 'nitro/vite'

export default defineConfig({
    plugins: [
        devtools(),
        viteTsConfigPaths({
            projects: ['./tsconfig.json'],
        }),
        tailwindcss(),
        tanstackStart(),
        nitro(), 
        // React's vite plugin must come after start's vite plugin
        viteReact(),
    ],
    css: {
        transformer: 'postcss',
    },
});
