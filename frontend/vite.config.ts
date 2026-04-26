import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import tailwindcss from '@tailwindcss/vite';
import { devtools } from '@tanstack/devtools-vite';
import { tanstackStart } from '@tanstack/react-start/plugin/vite';
import viteReact from '@vitejs/plugin-react';
import { defineConfig } from 'vite';
import viteTsConfigPaths from 'vite-tsconfig-paths';

const __dirname = path.dirname(fileURLToPath(import.meta.url));

function serveXpCss(): import('vite').Plugin {
    const xpDist = path.resolve(__dirname, 'node_modules/xp.css/dist');
    const assets = ['XP.css', 'XP.css.map'];
    const fonts = fs
        .readdirSync(xpDist)
        .filter((f: string) => /\.woff2?$/.test(f));
    const allFiles = [...assets, ...fonts];

    return {
        name: 'serve-xp-css',
        configureServer(server) {
            server.middlewares.use((req, _res, next) => {
                const file = req.url?.split('?')[0]?.slice(1);
                if (file && allFiles.includes(file)) {
                    req.url = `/@fs/${path.resolve(xpDist, file)}`;
                }
                next();
            });
        },
        writeBundle() {
            const outDir = path.resolve(__dirname, '.output/public');
            fs.mkdirSync(outDir, { recursive: true });
            for (const file of allFiles) {
                fs.copyFileSync(
                    path.resolve(xpDist, file),
                    path.resolve(outDir, file),
                );
            }
        },
    };
}

export default defineConfig({
    plugins: [
        devtools(),
        viteTsConfigPaths({
            projects: ['./tsconfig.json'],
        }),
        tailwindcss(),
        tanstackStart(),
        // React's vite plugin must come after start's vite plugin
        viteReact(),
        serveXpCss(),
    ],
    css: {
        transformer: 'postcss',
    },
});
