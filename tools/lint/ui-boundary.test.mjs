import assert from 'node:assert/strict';
import { describe, test } from 'node:test';
import { compare, rebaseline, rulesFor, scan } from './ui-boundary.mjs';

const APP_TSX = 'frontend/src/apps/demo/Demo.tsx';
const APP_TS = 'frontend/src/apps/demo/demo.ts';
const APP_CSS = 'frontend/src/apps/demo/demo.css';
const PART_TSX = 'frontend/ui/src/parts/controls/Demo/Demo.tsx';
const PART_CSS = 'frontend/ui/src/parts/controls/Demo/Demo.css';

const hits = (file, text, rule) =>
    scan(file, text)
        .filter((v) => !rule || v.rule === rule)
        .map((v) => v.fingerprint);

describe('raw-colour', () => {
    test('flags hex, rgb() and hsl() in app TSX strings', () => {
        const text = `const a = '#fff'; const b = 'rgba(0, 0, 0, 0.5)'; const c = \`1px solid hsl(200 50% 50%)\`;`;
        assert.deepEqual(hits(APP_TSX, text, 'raw-colour'), [
            '#fff',
            'rgba(0, 0, 0, 0.5)',
            'hsl(200 50% 50%)',
        ]);
    });

    test('flags hex and named colours in app CSS', () => {
        const text = `.a { color: #1d5b9e; border: 1px solid black; background: var(--ui-face); }`;
        assert.deepEqual(hits(APP_CSS, text, 'raw-colour'), [
            '#1d5b9e',
            'black',
        ]);
    });

    test('flags Tailwind palette utilities and named SVG colours', () => {
        const text = `<div className="bg-white text-slate-500 hover:ring-white/80 flex gap-2"><stop stopColor="white" /></div>`;
        assert.deepEqual(hits(APP_TSX, text, 'raw-colour'), [
            'bg-white',
            'text-slate-500',
            'ring-white/80',
            'white',
        ]);
    });

    test('ignores tokens, fragments, imports and private names', () => {
        const text = `import x from '#api/party-zone'; class A { #abc = 1 }
            const a = <a href="#add" style={{ '--ui-x': 'var(--ui-face)' }}>white</a>;
            const c = 'rgb(from var(--ui-face) r g b / 50%)';`;
        assert.deepEqual(hits(APP_TSX, text, 'raw-colour'), []);
    });

    test('exempts renderer data, tests, mocks and generated files', () => {
        const text = `export const skin = '#e0b080';`;
        for (const file of [
            'frontend/src/apps/places/scene/prefabs.ts',
            'frontend/src/apps/places/figure/dress.ts',
            'frontend/src/apps/places/iso/appearance.ts',
            'frontend/src/apps/demo/demo.test.ts',
            'frontend/src/lib/realtime-store.mock.ts',
            'frontend/src/api/party-zone.ts',
            'frontend/src/routeTree.gen.ts',
        ])
            assert.deepEqual(hits(file, text, 'raw-colour'), [], file);
    });
});

describe('visual-inline-style', () => {
    test('flags visual properties and allows geometry and custom properties', () => {
        const text = `<div style={{ left: x, width: 20, transform: t, gridTemplateColumns: '1fr 2fr', '--ui-home': colour, color: ink, borderRadius: 4, fontSize: 11, transition: 'opacity 1s', opacity: 0.5 }} />`;
        assert.deepEqual(hits(APP_TSX, text, 'visual-inline-style'), [
            'color: ink',
            'borderRadius: 4',
            'fontSize: 11',
            "transition: 'opacity 1s'",
            'opacity: 0.5',
        ]);
    });

    test('allows opacity that is data', () => {
        const text = `<div style={{ opacity: fade, top: y }} />`;
        assert.deepEqual(hits(APP_TSX, text, 'visual-inline-style'), []);
    });

    test('only applies to TSX', () => {
        assert.deepEqual(rulesFor(APP_TS).has('visual-inline-style'), false);
    });
});

describe('emoji-icon', () => {
    test('flags emoji in icon props and icon-like elements', () => {
        const text = `const apps = [{ id: 'home', icon: '🏠' }];
            const a = <TaskbarButton icon="💬" label="Chat" />;
            const b = <button type="button">⚙️</button>;
            const c = <span className="tray-icon">🔔</span>;`;
        assert.deepEqual(hits(APP_TSX, text, 'emoji-icon'), [
            'icon: 🏠',
            'icon="💬"',
            '<button>⚙️',
            '<span>🔔',
        ]);
    });

    test('allows emoji in content and named icons', () => {
        const text = `const a = <p>Party time 🎉 with friends</p>;
            const b = <button>Send 🚀 now</button>;
            const c = <Icon name="home" />;
            const d = <span>©</span>;`;
        assert.deepEqual(hits(APP_TSX, text, 'emoji-icon'), []);
    });
});

describe('deep-import', () => {
    test('allows the public entry points', () => {
        const text = `import { Window } from '@partytown/ui'; import '@partytown/ui/styles.css'; import '@partytown/ui/tokens.css';`;
        assert.deepEqual(hits(APP_TSX, text, 'deep-import'), []);
    });

    test('flags deep paths and relative reaches into the kit', () => {
        const text = `import { Window } from '@partytown/ui/src/parts/chrome'; import x from '../../../ui/src/scheme';`;
        assert.deepEqual(hits(APP_TSX, text, 'deep-import'), [
            'deep path @partytown/ui/src/parts/chrome',
            'reaches into the kit: ../../../ui/src/scheme',
        ]);
    });

    test('flags deep paths in app CSS and Storybook config', () => {
        assert.deepEqual(
            hits(
                APP_CSS,
                `@import '@partytown/ui/src/tokens.css';`,
                'deep-import',
            ),
            ['deep path @partytown/ui/src/tokens.css'],
        );
        assert.deepEqual(
            hits(
                'frontend/.storybook/preview.tsx',
                `import '@partytown/ui/fonts/x.woff2';`,
                'deep-import',
            ),
            ['deep path @partytown/ui/fonts/x.woff2'],
        );
    });

    test('flags the kit importing app code or other packages', () => {
        const text = `import { api } from '#api/party-zone'; import { a } from '../../../../../src/lib/x';
            import { useQuery } from '@tanstack/react-query'; const m = await import('#lib/realtime-store');
            import { useState } from 'react'; import { createPortal } from 'react-dom'; import './Demo.css';`;
        assert.deepEqual(hits(PART_TSX, text, 'deep-import'), [
            'kit imports app code: #api/party-zone',
            'kit reaches outside: ../../../../../src/lib/x',
            'kit depends on @tanstack/react-query; React is its only peer',
            'kit imports app code: #lib/realtime-store',
        ]);
    });

    test('lets kit stories use Storybook', () => {
        const text = `import type { Meta } from '@storybook/react-vite'; import { expect } from 'storybook/test';`;
        assert.deepEqual(
            hits(
                'frontend/ui/src/parts/controls/Demo/Demo.stories.tsx',
                text,
                'deep-import',
            ),
            [],
        );
    });
});

describe('dark-variant', () => {
    test('flags dark: utilities and classList toggles', () => {
        const text = `const a = <div className="bg-x dark:bg-y md:dark:hidden" />; document.documentElement.classList.toggle('dark', on);`;
        assert.deepEqual(hits(APP_TSX, text, 'dark-variant'), [
            'dark:',
            'dark:',
            "classList.toggle('dark')",
        ]);
    });

    test('flags .dark selectors, the dark variant and prefers-color-scheme in CSS', () => {
        const text = `@custom-variant dark (&:where(.dark, .dark *));
            .dark .a { top: 0; }
            @media (prefers-color-scheme: dark) { .b { top: 0; } }
            .darker { top: 0; }`;
        assert.deepEqual(hits(APP_CSS, text, 'dark-variant'), [
            '@custom-variant dark',
            '.dark',
            'prefers-color-scheme: dark',
        ]);
    });
});

describe('font-literal', () => {
    test('flags literal stacks and allows tokens', () => {
        const text = `.a { font-family: Tahoma, sans-serif; } .b { font: bold 11px Tahoma, sans-serif; } .c { font-family: var(--ui-font-ui); font: inherit; }
            @font-face { font-family: X; src: url(x.woff2); }`;
        assert.deepEqual(hits(APP_CSS, text, 'font-literal'), [
            'font-family: Tahoma, sans-serif',
            'font: bold 11px Tahoma, sans-serif',
            '@font-face',
            'font-family: X',
        ]);
    });

    test('flags fontFamily literals and Tailwind font stacks in TSX', () => {
        const text = `<div className="font-mono" style={{ fontFamily: 'Tahoma' }} />; const s = { fontFamily: '"Courier New", monospace' };`;
        assert.deepEqual(hits(APP_TSX, text, 'font-literal'), [
            'font-mono',
            'fontFamily: "Courier New", monospace',
        ]);
    });
});

describe('motion-literal', () => {
    test('flags durations and easings in CSS transitions and animations', () => {
        const text = `.a { transition: opacity 0.15s ease; animation: pulse 2s cubic-bezier(0.2, 0, 0, 1) infinite; }
            .b { transition: opacity var(--ui-dur-quick) var(--ui-ease-out); transition-delay: 0s; }
            .c { animation: marquee 1s linear infinite; }`;
        assert.deepEqual(hits(APP_CSS, text, 'motion-literal'), [
            '0.15s',
            'ease',
            '2s',
            'cubic-bezier(0.2, 0, 0, 1)',
            '1s',
        ]);
    });

    test('flags Tailwind motion utilities and literal transition strings', () => {
        const text = `<div className="transition-all duration-150 ease-out animate-pulse" />; const t = 'transform 200ms ease-out'; const ago = '5s ago';`;
        assert.deepEqual(hits(APP_TSX, text, 'motion-literal'), [
            'transition-all',
            'duration-150',
            'ease-out',
            'animate-pulse',
            '200ms',
            'ease-out',
        ]);
    });
});

describe('kit-raw-values', () => {
    test('flags raw values in parts and accepts tokens', () => {
        const text = `.ui-x { color: #fff; transition: background var(--ui-dur-quick) var(--ui-ease-out), opacity 120ms ease-in;
            font-family: Tahoma; background: var(--ui-control); border-color: white; }`;
        assert.deepEqual(hits(PART_CSS, text, 'kit-raw-values'), [
            '#fff',
            '120ms',
            'ease-in',
            'font-family: Tahoma',
            'white',
        ]);
    });

    test('flags raw values in part TSX', () => {
        const text = `const s = { fontFamily: 'Tahoma' }; const c = '#c00';`;
        assert.deepEqual(hits(PART_TSX, text, 'kit-raw-values'), [
            'fontFamily: Tahoma',
            '#c00',
        ]);
    });

    test('lets tokens, fonts, skins and styleguide stories hold values', () => {
        const text = `:root { --ui-face: #ece9d8; --ui-dur-quick: 120ms; font-family: Tahoma; }`;
        for (const file of [
            'frontend/ui/src/tokens.css',
            'frontend/ui/src/base.css',
            'frontend/ui/skins/winamp/winamp.css',
            'frontend/ui/src/styleguide/styleguide.css',
            'frontend/ui/src/story-kit/story-kit.css',
            'frontend/ui/src/parts/controls/Demo/Demo.stories.tsx',
        ])
            assert.deepEqual(hits(file, text, 'kit-raw-values'), [], file);
    });

    test('app rules do not apply inside the kit', () => {
        const text = `<div className="dark:bg-x" style={{ color: 'red' }} icon="🏠" />`;
        assert.deepEqual(
            hits(PART_TSX, text).filter((f) => f !== 'red'),
            [],
        );
    });
});

describe('skin-outside-skins', () => {
    test('flags a named skin selector outside frontend/ui/skins/', () => {
        const text = `.ui-window[data-ui-skin="msn"] .ui-window-titlebar { padding: 0; }
            [data-ui-skin~='msn'] { --ui-face: var(--ui-content); }`;
        for (const file of [APP_CSS, PART_CSS, 'frontend/src/styles.css'])
            assert.deepEqual(
                hits(file, text, 'skin-outside-skins'),
                ['[data-ui-skin="msn"]', "[data-ui-skin~='msn']"],
                file,
            );
    });

    test('lets a skin style itself and a part keep the bare hook', () => {
        assert.deepEqual(
            hits(
                'frontend/ui/skins/msn/msn.css',
                `.ui-window[data-ui-skin="msn"] { --ui-face: #edf3fc; }`,
                'skin-outside-skins',
            ),
            [],
        );
        assert.deepEqual(
            hits(
                PART_CSS,
                `.ui-window[data-ui-skin] { isolation: isolate; }`,
                'skin-outside-skins',
            ),
            [],
        );
    });

    test('flags a data-ui-skin attribute in app TSX, not the kit', () => {
        const text = `<section data-ui-skin="msn" />`;
        assert.deepEqual(hits(APP_TSX, text, 'skin-outside-skins'), [
            'data-ui-skin',
        ]);
        assert.deepEqual(hits(PART_TSX, text, 'skin-outside-skins'), []);
        assert.deepEqual(
            hits(APP_TSX, `<Window skin="msn" />`, 'skin-outside-skins'),
            [],
        );
    });
});

describe('layout-soup', () => {
    test('flags a className with more than 6 utilities and allows 6', () => {
        const text = `<>
            <div className="flex min-h-0 flex-1 flex-col gap-3 overflow-y-auto p-3" />
            <div className="flex min-w-0 flex-1 items-center gap-2 p-2" />
        </>`;
        assert.deepEqual(hits(APP_TSX, text, 'layout-soup'), [
            'flex min-h-0 flex-1 flex-col gap-3 overflow-y-auto p-3',
        ]);
    });

    test('flags arbitrary grid templates and calc() however short', () => {
        const text = `<>
            <div className="grid grid-cols-[128px_minmax(0,1fr)] gap-3" />
            <div className="grid md:grid-rows-[auto_1fr]" />
            <div className="max-w-[calc(100%-80px)]" />
            <div className="grid grid-cols-2 w-[220px]" />
        </>`;
        assert.deepEqual(hits(APP_TSX, text, 'layout-soup'), [
            'grid grid-cols-[128px_minmax(0,1fr)] gap-3',
            'grid md:grid-rows-[auto_1fr]',
            'max-w-[calc(100%-80px)]',
        ]);
    });

    test('reads each branch of a conditional or call on its own, and part class props', () => {
        const text = `<>
            <div className={open ? 'flex flex-col gap-2 p-2 min-h-0 flex-1 overflow-auto' : 'hidden'} />
            <div className={cn('flex gap-2', wide && 'grid grid-cols-[1fr_2fr]')} />
            <Window bodyClassName={\`flex flex-col gap-1 p-1 min-h-0 flex-1 \${x} overflow-auto\`} />
        </>`;
        assert.deepEqual(hits(APP_TSX, text, 'layout-soup'), [
            'flex flex-col gap-2 p-2 min-h-0 flex-1 overflow-auto',
            'grid grid-cols-[1fr_2fr]',
            'flex flex-col gap-1 p-1 min-h-0 flex-1 overflow-auto',
        ]);
    });

    test('only in app TSX: not in strings elsewhere, kit parts or CSS', () => {
        const soup = 'flex min-h-0 flex-1 flex-col gap-3 overflow-y-auto p-3';
        assert.deepEqual(
            hits(APP_TSX, `const c = '${soup}';`, 'layout-soup'),
            [],
        );
        assert.deepEqual(
            hits(PART_TSX, `<div className="${soup}" />`, 'layout-soup'),
            [],
        );
        assert.equal(rulesFor(APP_CSS).has('layout-soup'), false);
        assert.equal(rulesFor(APP_TS).has('layout-soup'), false);
    });
});

describe('baseline', () => {
    const v = (fingerprint, line = 1) => ({
        file: APP_CSS,
        rule: 'raw-colour',
        fingerprint,
        line,
        col: 1,
    });

    test('keys by fingerprint, not line, so moving code does not churn it', () => {
        const baseline = { [APP_CSS]: { 'raw-colour': { '#fff': 2 } } };
        const { fresh, stale } = compare(
            [v('#fff', 40), v('#fff', 90)],
            baseline,
        );
        assert.deepEqual([fresh.length, stale.length], [0, 0]);
    });

    test('a new occurrence beyond the count is fresh', () => {
        const baseline = { [APP_CSS]: { 'raw-colour': { '#fff': 1 } } };
        const { fresh } = compare(
            [v('#fff'), v('#fff', 2), v('#000', 3)],
            baseline,
        );
        assert.deepEqual(
            fresh.map((f) => f.fingerprint),
            ['#fff', '#000'],
        );
    });

    test('a fixed occurrence is stale', () => {
        const baseline = {
            [APP_CSS]: { 'raw-colour': { '#fff': 2, '#000': 1 } },
        };
        const { fresh, stale } = compare([v('#fff')], baseline);
        assert.equal(fresh.length, 0);
        assert.deepEqual(stale, [
            {
                file: APP_CSS,
                rule: 'raw-colour',
                fingerprint: '#fff',
                count: 1,
            },
            {
                file: APP_CSS,
                rule: 'raw-colour',
                fingerprint: '#000',
                count: 1,
            },
        ]);
    });

    test('a partial rebaseline rewrites only the named rules', () => {
        const other = 'frontend/src/apps/demo/Other.tsx';
        const baseline = {
            [APP_CSS]: { 'raw-colour': { '#fff': 2 } },
            [APP_TSX]: {
                'layout-soup': { 'old soup': 1 },
                'raw-colour': { '#000': 1 },
            },
            [other]: { 'layout-soup': { gone: 1 } },
        };
        const violations = [
            { file: APP_TSX, rule: 'layout-soup', fingerprint: 'new soup' },
            { file: APP_TSX, rule: 'raw-colour', fingerprint: '#123' },
        ];
        assert.deepEqual(rebaseline(baseline, violations, ['layout-soup']), {
            [APP_CSS]: { 'raw-colour': { '#fff': 2 } },
            [APP_TSX]: {
                'layout-soup': { 'new soup': 1 },
                'raw-colour': { '#000': 1 },
            },
        });
    });
});
