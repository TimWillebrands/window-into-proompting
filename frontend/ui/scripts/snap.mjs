#!/usr/bin/env node
import { spawn } from 'node:child_process';
import { existsSync, mkdirSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { chromium } from 'playwright';

const kit = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');

const USAGE = `Usage: pnpm --filter @partytown/ui snap [options] <story-id | component-id>...

Renders kit stories and saves PNG golden references to frontend/ui/reference/kit/.

  <story-id>       a story, e.g. controls-commandbutton--default
  <component-id>   every story of a part, e.g. controls-commandbutton

A part's Default story is saved as <Part>.png, every other story as <Part>-<Story>.png
(CommandButton.png, CommandButton-AllStates.png). Stories render in Luna Blue on the XP
desktop backdrop at 1280x800, device scale 1, animations off, fonts loaded, and the PNG
is cropped to what the story rendered. Play functions run to the end and images
and fonts settle before the capture.

Options:
  --all             every story outside Styleguide
  --scheme <name>   luna-blue (default), olive-green, silver or noir; others add a scheme suffix
  --url <url>       use a running Storybook (default http://localhost:6006);
                    one is started on --port when nothing answers there
  --port <n>        port for the Storybook this script starts (default 6116)
  --out <dir>       output directory (default frontend/ui/reference/kit)
  --list            print the matching story ids and file names, render nothing
  --help            this text

Examples:
  pnpm --filter @partytown/ui snap controls-commandbutton--default
  pnpm --filter @partytown/ui snap controls-commandbutton controls-textbox
  pnpm --filter @partytown/ui snap --all --scheme noir --out /tmp/noir`;

const parse = (argv) => {
    const opts = {
        ids: [],
        all: false,
        list: false,
        scheme: 'luna-blue',
        url: 'http://localhost:6006',
        port: 6116,
        out: path.join(kit, 'reference/kit'),
    };
    for (let i = 0; i < argv.length; i++) {
        const a = argv[i];
        const value = () => {
            const v = argv[++i];
            if (v === undefined) throw new Error(`${a} needs a value`);
            return v;
        };
        if (a === '--help' || a === '-h') opts.help = true;
        else if (a === '--all') opts.all = true;
        else if (a === '--list') opts.list = true;
        else if (a === '--scheme') opts.scheme = value();
        else if (a === '--url') opts.url = value().replace(/\/$/, '');
        else if (a === '--port') opts.port = Number(value());
        else if (a === '--out') opts.out = path.resolve(value());
        else if (a.startsWith('--')) throw new Error(`unknown option ${a}`);
        else opts.ids.push(a);
    }
    if (!['luna-blue', 'olive-green', 'silver', 'noir'].includes(opts.scheme))
        throw new Error(`unknown scheme ${opts.scheme}`);
    return opts;
};

const pascal = (s) =>
    s
        .split(/[^A-Za-z0-9]+/)
        .filter(Boolean)
        .map((w) => w[0].toUpperCase() + w.slice(1))
        .join('');

const fileName = (entry, scheme) => {
    const part = pascal(entry.title.split('/').at(-1));
    const story = entry.name === 'Default' ? '' : `-${pascal(entry.name)}`;
    const suffix = scheme === 'luna-blue' ? '' : `-${pascal(scheme)}`;
    return `${part}${story}${suffix}.png`;
};

const fetchIndex = async (url) => {
    const res = await fetch(`${url}/index.json`, {
        signal: AbortSignal.timeout(2000),
    });
    if (!res.ok) throw new Error(`${url}/index.json: ${res.status}`);
    const entries = (await res.json()).entries;
    const kitStories = Object.values(entries).every((e) =>
        existsSync(path.join(kit, e.importPath)),
    );
    if (!kitStories) throw new Error(`${url} is not the kit Storybook`);
    return entries;
};

const startStorybook = async (port) => {
    const child = spawn(
        'pnpm',
        ['exec', 'storybook', 'dev', '--ci', '--no-open', '-p', String(port)],
        { cwd: kit, stdio: ['ignore', 'ignore', 'inherit'], detached: true },
    );
    const url = `http://localhost:${port}`;
    const until = Date.now() + 120_000;
    while (Date.now() < until) {
        if (child.exitCode !== null)
            throw new Error(`storybook exited with ${child.exitCode}`);
        try {
            const entries = await fetchIndex(url);
            return {
                url,
                entries,
                stop: () => process.kill(-child.pid, 'SIGTERM'),
            };
        } catch {
            await new Promise((r) => setTimeout(r, 500));
        }
    }
    process.kill(-child.pid, 'SIGTERM');
    throw new Error(`storybook did not answer on ${url} within 120s`);
};

const connect = async (opts) => {
    try {
        return {
            url: opts.url,
            entries: await fetchIndex(opts.url),
            stop() {},
        };
    } catch (e) {
        console.error(
            `${e.message.startsWith(opts.url) ? e.message : `no kit Storybook at ${opts.url}`}, starting one on port ${opts.port}`,
        );
        return startStorybook(opts.port);
    }
};

const select = (entries, opts) => {
    const stories = Object.values(entries).filter((e) => e.type === 'story');
    if (opts.all) return stories.filter((e) => !e.id.startsWith('styleguide-'));
    const picked = [];
    for (const id of opts.ids) {
        const match = stories.filter(
            (e) => e.id === id || e.id.startsWith(`${id}--`),
        );
        if (!match.length) throw new Error(`no story matches ${id}`);
        picked.push(...match);
    }
    return [...new Map(picked.map((e) => [e.id, e])).values()];
};

const snap = async (page, url, entry, scheme, file) => {
    const globals = `scheme:${scheme};reduceMotion:off;reduceTransparency:off;contrast:normal;backgrounds.value:desktop`;
    await page.goto(
        `${url}/iframe.html?id=${entry.id}&viewMode=story&globals=${globals}`,
    );
    await page.waitForFunction(
        () =>
            document.querySelector('#storybook-root')?.childElementCount > 0 ||
            document.body.classList.contains('sb-show-errordisplay'),
    );
    if (await page.locator('body.sb-show-errordisplay').count())
        throw new Error(`${entry.id} failed to render`);
    await page.waitForFunction(
        () =>
            ['finished', 'errored', 'aborted'].includes(
                globalThis.__STORYBOOK_PREVIEW__?.currentRender?.phase,
            ),
        undefined,
        { timeout: 30_000 },
    );
    const phase = await page.evaluate(
        () => globalThis.__STORYBOOK_PREVIEW__.currentRender.phase,
    );
    if (phase !== 'finished')
        throw new Error(`${entry.id} ended in phase ${phase}`);
    await page.evaluate(async () => {
        await document.fonts.ready;
        await Promise.all(
            [...document.images].map((image) =>
                image.complete
                    ? image.decode().catch(() => undefined)
                    : new Promise((done) => {
                          image.addEventListener('load', done, { once: true });
                          image.addEventListener('error', done, {
                              once: true,
                          });
                      }),
            ),
        );
        await document.fonts.ready;
        await new Promise((done) =>
            requestAnimationFrame(() => requestAnimationFrame(done)),
        );
    });
    await page.waitForTimeout(100);
    const box = await page.evaluate(() => {
        const root = document.querySelector('#storybook-root');
        const rects = [...root.querySelectorAll('*')]
            .map((el) => el.getBoundingClientRect())
            .filter((r) => r.width && r.height);
        if (!rects.length) return null;
        const x = Math.max(0, Math.min(...rects.map((r) => r.left)));
        const y = Math.max(0, Math.min(...rects.map((r) => r.top)));
        const right = Math.max(...rects.map((r) => r.right));
        const bottom = Math.max(...rects.map((r) => r.bottom));
        return { x, y, width: right - x, height: bottom - y };
    });
    const pad = 8;
    await page.screenshot({
        path: file,
        animations: 'disabled',
        caret: 'hide',
        clip: box
            ? {
                  x: Math.max(0, box.x - pad),
                  y: Math.max(0, box.y - pad),
                  width: box.width + pad * 2,
                  height: box.height + pad * 2,
              }
            : undefined,
        fullPage: !box,
    });
};

const main = async () => {
    const opts = parse(process.argv.slice(2));
    if (opts.help || (!opts.all && !opts.ids.length)) {
        console.log(USAGE);
        process.exit(opts.help ? 0 : 1);
    }
    const sb = await connect(opts);
    try {
        const chosen = select(sb.entries, opts);
        if (opts.list) {
            for (const e of chosen)
                console.log(`${e.id}\t${fileName(e, opts.scheme)}`);
            return;
        }
        mkdirSync(opts.out, { recursive: true });
        const browser = await chromium.launch();
        try {
            const page = await browser.newPage({
                viewport: { width: 1280, height: 800 },
                deviceScaleFactor: 1,
                reducedMotion: 'no-preference',
                colorScheme: 'light',
            });
            for (const e of chosen) {
                const file = path.join(opts.out, fileName(e, opts.scheme));
                await snap(page, sb.url, e, opts.scheme, file);
                console.log(`${e.id} -> ${file}`);
            }
        } finally {
            await browser.close();
        }
    } finally {
        sb.stop();
    }
};

main().catch((e) => {
    console.error(e.message);
    console.error('Run with --help for usage.');
    process.exit(1);
});
