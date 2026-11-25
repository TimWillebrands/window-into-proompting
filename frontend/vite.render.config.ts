import tailwindcss from '@tailwindcss/vite';
import viteReact from '@vitejs/plugin-react';
import { defineConfig } from 'vite';

const backendTarget = process.env.VITE_API_URL ?? 'http://backend:5072';

export default defineConfig({
    root: 'render',
    publicDir: '../public',
    resolve: {
        tsconfigPaths: true,
    },
    plugins: [tailwindcss(), viteReact()],
    css: {
        transformer: 'postcss',
    },
    build: {
        outDir: '../dist-render',
        emptyOutDir: true,
    },
    preview: {
        port: 8090,
        strictPort: true,
        proxy: {
            '/api': {
                target: backendTarget,
                changeOrigin: true,
            },
        },
    },
});
