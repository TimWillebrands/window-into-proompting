import tailwindcss from '@tailwindcss/vite';
import { devtools } from '@tanstack/devtools-vite';
import { tanstackStart } from '@tanstack/react-start/plugin/vite';
import viteReact from '@vitejs/plugin-react';
import { defineConfig } from 'vite';

const backendTarget = process.env.VITE_API_URL ?? 'http://backend:5072';

export default defineConfig({
    resolve: {
        tsconfigPaths: true,
    },
    plugins: [
        devtools({ consolePiping: { enabled: false } }),
        tailwindcss(),
        tanstackStart(),
        viteReact(),
    ],
    css: {
        transformer: 'postcss',
    },
    server: {
        proxy: {
            '/api': {
                target: backendTarget,
                changeOrigin: true,
                ws: true,
            },
        },
    },
});
