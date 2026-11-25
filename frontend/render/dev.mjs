import { createServer } from 'node:http';
import { fileURLToPath } from 'node:url';
import { chromium } from 'playwright';
import { build, preview } from 'vite';

const configFile = fileURLToPath(
    new URL('../vite.render.config.ts', import.meta.url),
);
const SHOT_PORT = Number(process.env.RENDER_SHOT_PORT ?? 8091);

const watcher = await build({ configFile, build: { watch: {} } });
await new Promise((resolve) =>
    watcher.on('event', (e) => {
        if (e.code === 'ERROR') console.error(e.error);
        if (e.code === 'END') resolve();
    }),
);
const server = await preview({ configFile });
server.printUrls();

let browser;
let queue = Promise.resolve();

const launch = async () => {
    if (!browser?.isConnected())
        browser = await chromium.launch({
            args: ['--use-angle=swiftshader', '--enable-unsafe-swiftshader'],
        });
    return browser;
};

async function shoot({ url, viewport, waitForSelector }) {
    const page = await (await launch()).newPage({
        viewport: viewport ?? { width: 1280, height: 860 },
    });
    try {
        await page.goto(url, { waitUntil: 'load', timeout: 45000 });
        if (waitForSelector?.selector)
            await page.waitForSelector(waitForSelector.selector, {
                timeout: waitForSelector.timeout ?? 45000,
            });
        return await page.screenshot({ type: 'png' });
    } finally {
        await page.close();
    }
}

const readJson = async (req) => {
    let body = '';
    for await (const chunk of req) body += chunk;
    return JSON.parse(body);
};

createServer(async (req, res) => {
    if (req.method !== 'POST' || req.url !== '/screenshot') {
        res.writeHead(404).end();
        return;
    }
    try {
        const request = await readJson(req);
        const turn = queue.then(
            () => shoot(request),
            () => shoot(request),
        );
        queue = turn;
        const png = await turn;
        res.writeHead(200, { 'content-type': 'image/png' }).end(png);
    } catch (e) {
        res.writeHead(422, { 'content-type': 'application/json' }).end(
            JSON.stringify({
                success: false,
                errors: [{ message: String(e?.message ?? e).slice(0, 400) }],
            }),
        );
    }
}).listen(SHOT_PORT, '127.0.0.1', () =>
    console.log(`  ➜  Screenshots: http://127.0.0.1:${SHOT_PORT}/screenshot`),
);
