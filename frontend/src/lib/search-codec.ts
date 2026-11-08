import {
    defaultParseSearch,
    defaultStringifySearch,
} from '@tanstack/react-router';
import LZString from 'lz-string';

/// <summary>Query param holding the lz-compressed desktop layout.</summary>
export const PACKED_KEY = 'd';

const DESKTOP_KEYS = [
    'windows',
    'order',
    'focusedId',
    'zCounter',
    'startMenuOpen',
] as const;

type Search = Record<string, unknown>;

let lastParse: { str: string; out: Search } | null = null;
let lastStringify: { key: string; out: string } | null = null;

/// <summary>Desktop layout → one compressed `d` param, everything else stays plain.</summary>
export function stringifySearch(search: Search): string {
    const key = JSON.stringify(search);
    if (lastStringify?.key === key) return lastStringify.out;

    const desktop: Search = {};
    const rest: Search = {};
    for (const [k, v] of Object.entries(search)) {
        if ((DESKTOP_KEYS as readonly string[]).includes(k)) desktop[k] = v;
        else rest[k] = v;
    }
    if (Object.keys(desktop).length > 0) {
        rest[PACKED_KEY] = LZString.compressToEncodedURIComponent(
            JSON.stringify(desktop),
        ).replace(/[+$]/g, (c) => (c === '+' ? '.' : '_'));
    }

    const out = defaultStringifySearch(rest);
    lastStringify = { key, out };
    return out;
}

/// <summary>Inverse of stringifySearch; legacy uncompressed URLs still parse.</summary>
export function parseSearch(str: string): Search {
    if (lastParse?.str === str) return lastParse.out;

    const { [PACKED_KEY]: packed, ...rest } = defaultParseSearch(str) as Search;
    let out: Search = rest;
    if (typeof packed === 'string') {
        try {
            const json = LZString.decompressFromEncodedURIComponent(
                packed.replace(/[._]/g, (c) => (c === '.' ? '+' : '$')),
            );
            const desktop = json ? JSON.parse(json) : null;
            if (desktop && typeof desktop === 'object') {
                out = { ...rest, ...desktop };
            }
        } catch {
            // Corrupt `d`: fall back to an empty desktop via schema defaults.
        }
    }

    lastParse = { str, out };
    return out;
}
