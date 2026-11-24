import {
    windowActions as act,
    emptyDesktop,
    windowReducer,
} from '@partytown/ui';
import { defaultStringifySearch } from '@tanstack/react-router';
import * as v from 'valibot';
import { describe, expect, it } from 'vitest';
import { desktopLayoutSchema } from '../types/desktop';
import { PACKED_KEY, parseSearch, stringifySearch } from './search-codec';

const bigDesktop = (n: number) => {
    const windows: Record<string, unknown> = {};
    for (let i = 0; i < n; i++) {
        windows[`win-${i}`] = {
            id: `win-${i}`,
            appId: 'places',
            title: `Places window number ${i}`,
            icon: '/icons/places.png',
            x: 100 + i,
            y: 80 + i,
            width: 800,
            height: 600,
            zIndex: i,
            minimized: false,
            maximized: false,
            restoreBounds: { x: 1, y: 2, width: 3, height: 4 },
            props: { roomId: `00000000-0000-0000-0000-00000000000${i % 10}` },
        };
    }
    return {
        windows,
        order: Object.keys(windows),
        focusedId: 'win-0',
        zCounter: n,
        startMenuOpen: false,
    };
};

describe('search codec', () => {
    it('round-trips the desktop through one compressed param', () => {
        const desktop = bigDesktop(5);
        const str = stringifySearch(desktop);
        expect(new URLSearchParams(str).has(PACKED_KEY)).toBe(true);
        expect(new URLSearchParams(str).has('windows')).toBe(false);
        expect(parseSearch(str)).toEqual(desktop);
    });

    it('is much shorter than the default encoding', () => {
        const desktop = bigDesktop(12);
        const plain = defaultStringifySearch(desktop);
        expect(plain.length).toBeGreaterThan(4096);
        const packed = stringifySearch(desktop).length;
        expect(packed).toBeLessThan(4096);
        expect(packed).toBeLessThan(plain.length / 3);
    });

    it('keeps non-desktop params plain', () => {
        const str = stringifySearch({ ...bigDesktop(1), droste: 2 });
        expect(new URLSearchParams(str).get('droste')).toBe('2');
        expect(parseSearch(str).droste).toBe(2);
    });

    it('leaves searches without a desktop alone', () => {
        expect(stringifySearch({ redirect_url: '/x' })).toBe(
            '?redirect_url=%2Fx',
        );
        expect(parseSearch('?redirect_url=%2Fx')).toEqual({
            redirect_url: '/x',
        });
    });

    it('still parses legacy uncompressed URLs', () => {
        const desktop = bigDesktop(2);
        expect(parseSearch(defaultStringifySearch(desktop))).toEqual(desktop);
    });

    it('survives a corrupt packed param', () => {
        expect(parseSearch(`?${PACKED_KEY}=garbage!!&x=1`)).toEqual({ x: 1 });
    });

    it('stays cheap enough for every window commit', () => {
        const runs = 200;
        const start = performance.now();
        for (let i = 0; i < runs; i++) {
            const desktop = bigDesktop(12);
            desktop.zCounter = i;
            parseSearch(stringifySearch(desktop));
        }
        const perCommit = (performance.now() - start) / runs;
        console.info(`search codec: ${perCommit.toFixed(3)} ms per commit`);
        expect(perCommit).toBeLessThan(5);
    });

    it('keeps a window manager desktop through the URL and its schema', () => {
        const area = { width: 1200, height: 800 };
        const desktop = [
            act.open({
                appId: 'board',
                title: 'Partyboard',
                icon: 'board',
                bounds: { x: 240, y: 120, width: 860, height: 600 },
                props: { threadId: 't1' },
                area,
            }),
            act.open({
                appId: 'messenger-chat',
                id: 'dm-1',
                title: 'Denise — Conversation',
                icon: 'chat',
                area,
            }),
            act.snap('board', 'left', area),
            act.minimise('dm-1'),
            act.startMenu(true),
        ].reduce(windowReducer, emptyDesktop);
        const read = v.parse(
            desktopLayoutSchema,
            parseSearch(stringifySearch(desktop)),
        );
        expect(read).toEqual(desktop);
    });

    it('still reads desktops written before the window manager', () => {
        const legacy = bigDesktop(3);
        const maximised = legacy.windows['win-1'] as Record<string, unknown>;
        maximised.maximized = true;
        const read = v.parse(
            desktopLayoutSchema,
            parseSearch(stringifySearch(legacy)),
        );
        expect(read).toEqual(legacy);
        const restored = windowReducer(read, act.toggleMaximise('win-1'));
        expect(restored.windows['win-1']).toMatchObject({
            maximized: false,
            x: 101,
            y: 81,
            width: 800,
            height: 600,
        });
        expect(restored.focusedId).toBe('win-1');
    });

    it('falls back to an empty desktop for a broken one', () => {
        expect(
            v.parse(desktopLayoutSchema, { windows: 'nope', focusedId: 3 }),
        ).toEqual(emptyDesktop);
    });
});
