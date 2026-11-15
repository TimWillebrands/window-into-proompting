#!/usr/bin/env node
import { execFileSync } from 'node:child_process';
import { readFileSync, writeFileSync } from 'node:fs';
import { createRequire } from 'node:module';
import path from 'node:path';

const MESSAGE = [
    'Comments are not allowed: they go stale, nobody reads them, and they excuse workarounds instead of fixing them.',
    'Say what the code does in names, types and tests; put why in the commit message or the issue.',
    'Only tool directives stay: biome-ignore, @ts-expect-error, /// <reference>, @vitest-environment, @vite-ignore, #__PURE__, shebangs.',
].join('\n');

const SKIP = [
    /(^|\/)node_modules\//,
    /\.gen\.ts$/,
    /(^|\/)frontend\/src\/api\/party-zone\.ts$/,
    /(^|\/)frontend\/src\/api\/model\//,
    /(^|\/)\.agents\//,
    /(^|\/)\.claude\//,
    /(^|\/)\.opencode\//,
    /(^|\/)docker-entrypoint-initdb\.d\/0[1-4]-orleans-/,
];

const DIRECTIVES = [
    /^\/\/\s*biome-ignore/,
    /^\/\*\s*biome-ignore/,
    /^\/\/\s*@ts-expect-error/,
    /^\/\/\s*@ts-check/,
    /^\/\/\/\s*<reference\s/,
    /^\/\*\*?\s*@vitest-environment\s/,
    /^\/\*\s*@vite-ignore\s*\*\/$/,
    /^\/\*\s*[#@]__PURE__\s*\*\/$/,
];

const SCRIPT_EXT = new Set(['.ts', '.tsx', '.mts', '.cts', '.js', '.jsx', '.mjs', '.cjs']);
const HASH_EXT = new Set(['.py', '.sh']);

const args = process.argv.slice(2);
const fix = args.includes('--fix');
const roots = args.filter((a) => a !== '--fix');

let ts;
const here = path.dirname(new URL(import.meta.url).pathname);
const loadTs = () => {
    for (const base of [process.cwd(), path.join(here, '../../frontend'), path.join(here, '../cdu')]) {
        try {
            ts ??= createRequire(path.join(base, 'package.json'))('typescript');
        } catch {}
    }
    if (!ts) throw new Error('typescript not found: install frontend or tools/cdu dependencies');
    return ts;
};

const listFiles = (dirs) => {
    const out = execFileSync('git', ['ls-files', '-co', '--exclude-standard', '--', ...(dirs.length ? dirs : ['.'])], {
        encoding: 'utf8',
        maxBuffer: 1 << 28,
    });
    return out.split('\n').filter(Boolean);
};

const kindOf = (file, text) => {
    const ext = path.extname(file);
    if (!ext && /^#!.*\b(ba|z)?sh\b/.test(text ?? '')) return 'hash';
    if (SCRIPT_EXT.has(ext)) return 'script';
    if (ext === '.css') return 'css';
    if (HASH_EXT.has(ext)) return 'hash';
    if (ext === '.sql') return 'sql';
    return null;
};

const scriptComments = (file, text) => {
    const t = loadTs();
    const ext = path.extname(file);
    const scriptKind = ext === '.tsx' || ext === '.jsx' ? t.ScriptKind.TSX : t.ScriptKind.TS;
    const sf = t.createSourceFile(file, text, t.ScriptTarget.Latest, true, scriptKind);
    const literals = [];
    const ranges = new Map();
    const add = (r) => r && r.forEach((c) => ranges.set(c.pos, { start: c.pos, end: c.end }));
    const visit = (node) => {
        switch (node.kind) {
            case t.SyntaxKind.JsxText:
            case t.SyntaxKind.StringLiteral:
            case t.SyntaxKind.NoSubstitutionTemplateLiteral:
            case t.SyntaxKind.TemplateHead:
            case t.SyntaxKind.TemplateMiddle:
            case t.SyntaxKind.TemplateTail:
            case t.SyntaxKind.RegularExpressionLiteral:
                literals.push([node.getStart(sf), node.end]);
        }
        if (node.kind === t.SyntaxKind.JsxExpression && !node.expression) {
            const at = node.getStart(sf) + 1;
            const seen = new Map();
            for (const c of [...(t.getTrailingCommentRanges(text, at) ?? []), ...(t.getLeadingCommentRanges(text, at) ?? [])])
                seen.set(c.pos, c);
            const inner = [...seen.values()];
            if (inner.length) {
                ranges.set(node.getStart(sf), { start: node.getStart(sf), end: node.end, jsx: inner });
                return;
            }
        }
        add(t.getLeadingCommentRanges(text, node.pos));
        add(t.getTrailingCommentRanges(text, node.end));
        for (const child of node.getChildren(sf)) visit(child);
    };
    add(t.getLeadingCommentRanges(text, 0));
    visit(sf);
    const inLiteral = (r) => literals.some(([s, e]) => r.start >= s && r.start < e);
    return [...ranges.values()]
        .filter((r) => !inLiteral(r))
        .filter((r) => {
            const parts = r.jsx ?? [r];
            return !parts.every((p) => DIRECTIVES.some((d) => d.test(text.slice(p.pos ?? p.start, p.end).trim())));
        });
};

const cssComments = (text) => {
    const out = [];
    let i = 0;
    while (i < text.length) {
        const c = text[i];
        if (c === '"' || c === "'") {
            i++;
            while (i < text.length && text[i] !== c) i += text[i] === '\\' ? 2 : 1;
            i++;
        } else if (c === '/' && text[i + 1] === '*') {
            const end = text.indexOf('*/', i + 2);
            const stop = end < 0 ? text.length : end + 2;
            if (!DIRECTIVES.some((d) => d.test(text.slice(i, stop)))) out.push({ start: i, end: stop });
            i = stop;
        } else i++;
    }
    return out;
};

const lineComments = (text, marker, keepShebang) => {
    const out = [];
    let offset = 0;
    text.split('\n').forEach((line, n) => {
        const trimmed = line.trimStart();
        if (trimmed.startsWith(marker) && !(keepShebang && n === 0 && trimmed.startsWith('#!'))) {
            const start = offset + line.length - trimmed.length;
            out.push({ start, end: offset + line.length });
        }
        offset += line.length + 1;
    });
    return out;
};

const commentsIn = (file, text) => {
    switch (kindOf(file, text)) {
        case 'script':
            return scriptComments(file, text);
        case 'css':
            return cssComments(text);
        case 'hash':
            return lineComments(text, '#', true);
        case 'sql':
            return lineComments(text, '--', false);
        default:
            return [];
    }
};

const lineCol = (text, pos) => {
    const before = text.slice(0, pos).split('\n');
    return [before.length, before.at(-1).length + 1];
};

const strip = (text, ranges) => {
    let out = text;
    for (const r of [...ranges].sort((a, b) => b.start - a.start)) {
        const ls = out.lastIndexOf('\n', r.start - 1) + 1;
        let le = out.indexOf('\n', r.end);
        if (le < 0) le = out.length;
        const prefix = out.slice(ls, r.start);
        const suffix = out.slice(r.end, le);
        if (!prefix.trim() && !suffix.trim()) {
            out = out.slice(0, ls) + out.slice(Math.min(le + 1, out.length));
            const prevBlank = ls === 0 || /\n[ \t]*\n$/.test(out.slice(0, ls)) || /^\s*$/.test(out.slice(0, ls));
            const next = out.slice(ls, out.indexOf('\n', ls) < 0 ? out.length : out.indexOf('\n', ls));
            if (prevBlank && !next.trim() && ls < out.length) out = out.slice(0, ls) + out.slice(ls + next.length + 1);
        } else if (!suffix.trim()) {
            out = out.slice(0, r.start).replace(/[ \t]+$/, '') + out.slice(r.end);
        } else {
            out = out.slice(0, r.start) + out.slice(r.end).replace(/^[ \t]+/, prefix.trim() ? ' ' : '');
        }
    }
    return out;
};

const top = execFileSync('git', ['rev-parse', '--show-toplevel'], { encoding: 'utf8' }).trim();
let count = 0;
for (const file of listFiles(roots)) {
    const fromTop = path.relative(top, path.resolve(file));
    if (SKIP.some((s) => s.test(fromTop))) continue;
    if (!kindOf(file) && path.extname(file)) continue;
    let text;
    try {
        text = readFileSync(file, 'utf8');
    } catch {
        continue;
    }
    if (!kindOf(file, text)) continue;
    const found = commentsIn(file, text);
    if (!found.length) continue;
    if (fix) {
        writeFileSync(file, strip(text, found));
        count += found.length;
        continue;
    }
    for (const r of found) {
        const [line, col] = lineCol(text, r.start);
        const first = text.slice(r.start, r.end).split('\n')[0].slice(0, 80);
        console.error(`${file}:${line}:${col} ${first}`);
        count++;
    }
}

if (fix) console.log(`removed ${count} comment(s)`);
else if (count) {
    console.error(`\n${count} comment(s). ${MESSAGE}\nnode tools/lint/no-comments.mjs --fix <paths> strips them.`);
    process.exit(1);
}
