#!/usr/bin/env node
import { execFileSync } from 'node:child_process';
import { readFileSync, writeFileSync } from 'node:fs';
import { createRequire } from 'node:module';
import path from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';

const here = path.dirname(fileURLToPath(import.meta.url));
const BASELINE = path.join(here, 'ui-boundary.baseline.json');
const SKILL = '.agents/skills/partytown-ui/SKILL.md';

export const RULES = {
    'raw-colour':
        'Colour literal outside the kit. Pick a part or a tone; a colour that is data goes through a part prop or a --ui-* custom property.',
    'visual-inline-style':
        'Visual property in an inline style. Apps hold layout only: use a part (or add one to the kit). Geometry and --ui-* custom properties are fine.',
    'emoji-icon':
        'Emoji used as an icon. Use <Icon name="…" /> from @partytown/ui; add the name to the kit if it is missing.',
    'deep-import':
        'Import crosses the kit boundary. Apps import only @partytown/ui, @partytown/ui/tokens.css and @partytown/ui/styles.css; the kit never imports app code and only depends on React.',
    'dark-variant':
        'Dark mode outside the kit. Dark is only a scheme (Noir): parts read tokens and get it for free. No dark: utilities, .dark selectors or prefers-color-scheme in apps.',
    'font-literal':
        'Font stack outside the kit. Fonts come only from the kit (--ui-font-*), through parts.',
    'motion-literal':
        'Literal duration or easing outside the kit. Motion comes only from --ui-dur-* and --ui-ease-* through parts.',
    'kit-raw-values':
        'Raw value in a kit part. Parts read tokens only; values live in src/tokens.css (fonts in base.css, skins in skins/).',
};

const SKIP_ALL = [
    /\.gen\.(ts|tsx|mts)$/,
    /^frontend\/src\/api\/party-zone\.ts$/,
    /^frontend\/src\/api\/model\//,
    /\.(test|spec)\.(ts|tsx|mts|js|mjs)$/,
    /\.mock\.(ts|tsx)$/,
    /(^|\/)(__fixtures__|fixtures)\//,
];

const RENDERER_DATA = [
    /^frontend\/src\/apps\/places\/scene\//,
    /^frontend\/src\/apps\/places\/figure\//,
    /^frontend\/src\/apps\/places\/iso\//,
];

const KIT_RAW_ALLOWED = [
    /^frontend\/ui\/src\/tokens\.css$/,
    /^frontend\/ui\/src\/base\.css$/,
    /^frontend\/ui\/src\/styleguide\//,
    /\.stories\.(ts|tsx)$/,
    /(^|\/)story-kit\.(ts|tsx|css)$/,
];

export const EXEMPTIONS = {
    'all rules': SKIP_ALL,
    'raw-colour, font-literal, motion-literal (renderer data)': RENDERER_DATA,
    'kit-raw-values (where kit values live)': [
        ...KIT_RAW_ALLOWED,
        /^frontend\/ui\/skins\//,
    ],
};

const APP = /^frontend\/src\//;
const KIT = /^frontend\/ui\//;
const KIT_SRC = /^frontend\/ui\/src\//;
const SCANNED = /\.(ts|tsx|mts|js|jsx|mjs|css)$/;

const NAMED_COLOURS =
    'aliceblue antiquewhite aqua aquamarine azure beige bisque black blanchedalmond blue blueviolet brown burlywood cadetblue chartreuse chocolate coral cornflowerblue cornsilk crimson cyan darkblue darkcyan darkgoldenrod darkgray darkgreen darkgrey darkkhaki darkmagenta darkolivegreen darkorange darkorchid darkred darksalmon darkseagreen darkslateblue darkslategray darkslategrey darkturquoise darkviolet deeppink deepskyblue dimgray dimgrey dodgerblue firebrick floralwhite forestgreen fuchsia gainsboro ghostwhite gold goldenrod gray green greenyellow grey honeydew hotpink indianred indigo ivory khaki lavender lavenderblush lawngreen lemonchiffon lightblue lightcoral lightcyan lightgoldenrodyellow lightgray lightgreen lightgrey lightpink lightsalmon lightseagreen lightskyblue lightslategray lightslategrey lightsteelblue lightyellow lime limegreen linen magenta maroon mediumaquamarine mediumblue mediumorchid mediumpurple mediumseagreen mediumslateblue mediumspringgreen mediumturquoise mediumvioletred midnightblue mintcream mistyrose moccasin navajowhite navy oldlace olive olivedrab orange orangered orchid palegoldenrod palegreen paleturquoise palevioletred papayawhip peachpuff peru pink plum powderblue purple rebeccapurple red rosybrown royalblue saddlebrown salmon sandybrown seagreen seashell sienna silver skyblue slateblue slategray slategrey snow springgreen steelblue tan teal thistle tomato turquoise violet wheat white whitesmoke yellow yellowgreen'.split(
        ' ',
    );
const NAMED = new RegExp(
    `(?<![\\w-])(?:${NAMED_COLOURS.join('|')})(?![\\w-])`,
    'gi',
);

const HEX =
    /(?<![\w&#./-])#(?:[0-9a-fA-F]{8}|[0-9a-fA-F]{6}|[0-9a-fA-F]{3,4})(?![\w-])/g;
const COLOUR_FN =
    /\b(?:rgba?|hsla?|hwb|oklch|oklab|lab|lch)\((?!\s*(?:from\s+)?var\()[^()]*(?:\([^()]*\)[^()]*)*\)/g;
const TW_COLOUR =
    /(?<![\w-])(?:bg|text|border(?:-[trblxyse])?|ring|ring-offset|outline|fill|stroke|from|via|to|divide|shadow|accent|caret|decoration|placeholder)-(?:black|white|(?:slate|gray|zinc|neutral|stone|red|orange|amber|yellow|lime|green|emerald|teal|cyan|sky|blue|indigo|violet|purple|fuchsia|pink|rose)-\d{2,3})(?:\/\d{1,3})?(?![\w-])/g;
const TIME = /(?<![\w.#-])(?:\d+\.?\d*|\.\d+)m?s(?![\w-])/g;
const EASING_FN = /\b(?:cubic-bezier|steps|linear)\([^)]*\)/g;
const EASING_WORD =
    /(?<![\w-])(?:ease|ease-in|ease-out|ease-in-out|step-start|step-end)(?![\w-])/g;
const TW_MOTION =
    /(?<![\w-])(?:(?:duration|delay)-(?:\d+|\[[^\]]+\])|ease-(?:linear|in|out|in-out|\[[^\]]+\])|animate-(?!none(?![\w-]))[\w-]+|transition-(?:all|colors|opacity|shadow|transform|\[[^\]]+\]))(?![\w-])/g;
const TW_FONT = /(?<![\w-])font-(?:sans|serif|mono|\[[^\]]+\])(?![\w-])/g;
const TW_DARK = /(?<![\w-])dark:(?=[\w[!-])/g;
const FONT_GENERIC =
    /(?<![\w-])(?:serif|sans-serif|monospace|cursive|fantasy|system-ui|ui-sans-serif|ui-serif|ui-monospace|ui-rounded|emoji|math|fangsong)(?![\w-])/;
const EMOJI = /[\p{Extended_Pictographic}\p{Regional_Indicator}]/u;
const NOT_EMOJI = /\u00A9|\u00AE|\u2122/gu;
const EMOJI_ONLY =
    /^(?:[\p{Extended_Pictographic}\p{Regional_Indicator}\p{Emoji_Modifier}]|\uFE0F|\u200D|\u20E3|\s)+$/u;

const COLOUR_PROPS =
    /^(?:color|background(?:-color|-image)?|border(?:-(?:top|right|bottom|left|block|inline)(?:-(?:start|end))?)?(?:-color)?|outline(?:-color)?|fill|stroke|box-shadow|text-shadow|caret-color|accent-color|text-decoration(?:-color)?|column-rule(?:-color)?|stop-color|flood-color|lighting-color)$/;
const VISUAL_STYLE =
    /^(?:color|background\w*|border\w*|outline\w*|boxShadow|textShadow|font\w*|lineHeight|letterSpacing|textTransform|textDecoration\w*|transition\w*|animation\w*|filter|backdropFilter|WebkitBackdropFilter|fill|stroke|accentColor|caretColor|mixBlendMode|opacity)$/;
const ICON_NAME = /^(?:icon\w*|\w+Icon)$/;
const ICON_HOLDER = /icon|button|btn/i;

const kebab = (name) => name.replace(/[A-Z]/g, (c) => `-${c.toLowerCase()}`);
const norm = (s) => s.replace(/\s+/g, ' ').trim().slice(0, 160);
const stripVars = (v) =>
    v.replace(/var\((?:[^()]|\([^()]*\))*\)/g, (m) => ' '.repeat(m.length));
const hasEmoji = (s) => EMOJI.test(s.replace(NOT_EMOJI, ''));

const matchesAll = (re, text) => {
    re.lastIndex = 0;
    return [...text.matchAll(re)].map((m) => ({ text: m[0], index: m.index }));
};

const colourLiterals = (text) => [
    ...matchesAll(HEX, text),
    ...matchesAll(COLOUR_FN, text),
];

const namedColours = (value) =>
    matchesAll(
        NAMED,
        stripVars(value.replace(/url\([^)]*\)/g, (m) => ' '.repeat(m.length))),
    );

const nonZeroTimes = (text) =>
    matchesAll(TIME, text).filter((m) => Number.parseFloat(m.text) !== 0);

const motionInDeclaration = (prop, value) => {
    const v = stripVars(value);
    const hits = matchesAll(EASING_FN, v);
    if (/^(?:transition|animation)(?:-|$)/.test(prop) || prop.startsWith('--'))
        hits.push(...nonZeroTimes(v));
    if (/^(?:transition|animation)(?:-|$)/.test(prop))
        hits.push(...matchesAll(EASING_WORD, v.replace(EASING_FN, '')));
    return hits;
};

const fontStack = (prop, value) => {
    const v = stripVars(value).trim();
    if (prop === 'font-family')
        return v && !/^(?:inherit|initial|unset|revert|revert-layer)?$/.test(v);
    if (prop === 'font') return /["']/.test(v) || FONT_GENERIC.test(v);
    return false;
};

let ts;
const loadTs = () => {
    if (ts) return ts;
    for (const base of [
        process.cwd(),
        path.join(here, '../../frontend'),
        path.join(here, '../cdu'),
    ]) {
        try {
            ts = createRequire(path.join(base, 'package.json'))('typescript');
            return ts;
        } catch {}
    }
    throw new Error('typescript not found: install frontend dependencies');
};

const lineCol = (text, pos) => {
    const before = text.slice(0, pos).split('\n');
    return { line: before.length, col: before.at(-1).length + 1 };
};

const resolveSpecifier = (file, spec) =>
    path.posix.normalize(path.posix.join(path.posix.dirname(file), spec));

const ALLOWED_UI_PATHS = new Set([
    '@partytown/ui',
    '@partytown/ui/tokens.css',
    '@partytown/ui/styles.css',
]);

const isStoryFile = (file) =>
    /\.stories\.(ts|tsx)$/.test(file) ||
    /(^|\/)story-kit\.(ts|tsx|css)$/.test(file) ||
    /^frontend\/ui\/src\/styleguide\//.test(file);

const importProblem = (file, rawSpec) => {
    const spec = rawSpec.replace(/\?.*$/, '');
    const inKit = KIT.test(file);
    if (
        spec.startsWith('@partytown/ui/') &&
        !ALLOWED_UI_PATHS.has(spec) &&
        !inKit
    )
        return `deep path ${spec}`;
    if (spec.startsWith('.')) {
        const target = resolveSpecifier(file, spec);
        if (inKit && !KIT.test(target)) return `kit reaches outside: ${spec}`;
        if (!inKit && KIT.test(target)) return `reaches into the kit: ${spec}`;
        return null;
    }
    if (!inKit) return null;
    if (/^(?:#|@\/|~\/|\/)/.test(rawSpec) || spec === 'proompting-party')
        return `kit imports app code: ${spec}`;
    if (!KIT_SRC.test(file) || spec.startsWith('node:')) return null;
    const pkg = spec.startsWith('@')
        ? spec.split('/').slice(0, 2).join('/')
        : spec.split('/')[0];
    if (pkg === 'react' || pkg === 'react-dom' || pkg === '@partytown/ui')
        return null;
    if (
        isStoryFile(file) &&
        (pkg === 'storybook' || pkg.startsWith('@storybook/'))
    )
        return null;
    return `kit depends on ${pkg}; React is its only peer`;
};

const scanCss = (file, text, on, report) => {
    const clean = text.replace(/\/\*[\s\S]*?\*\//g, (m) =>
        m.replace(/[^\n]/g, ' '),
    );
    const inKitRaw = on('kit-raw-values');
    let start = 0;
    let depth = 0;
    let quote = null;
    const segment = (end, terminator) => {
        const raw = clean.slice(start, end);
        const lead = raw.length - raw.trimStart().length;
        const at = start + lead;
        const body = raw.trim();
        start = end + 1;
        if (!body) return;
        const add = (rule, hits, offset = 0) => {
            for (const h of hits) report(rule, h.text, at + offset + h.index);
        };
        if (terminator === '{') {
            if (on('dark-variant')) {
                add('dark-variant', matchesAll(/\.dark(?![\w-])/g, body));
                add(
                    'dark-variant',
                    matchesAll(/prefers-color-scheme\s*:\s*dark/g, body),
                );
            }
            if (on('font-literal') && /^@font-face\b/.test(body))
                report('font-literal', '@font-face', at);
            return;
        }
        if (body.startsWith('@')) {
            const imp = body.match(/^@import\s+(?:url\()?\s*['"]([^'"]+)['"]/);
            if (imp && on('deep-import')) {
                const problem = importProblem(file, imp[1]);
                if (problem) report('deep-import', problem, at);
            }
            if (
                /^@(?:custom-variant|variant)\s+dark\b/.test(body) &&
                on('dark-variant')
            )
                report(
                    'dark-variant',
                    body.split(/\s+/).slice(0, 2).join(' '),
                    at,
                );
            if (body.startsWith('@apply')) {
                if (on('raw-colour'))
                    add('raw-colour', matchesAll(TW_COLOUR, body));
                if (on('motion-literal'))
                    add('motion-literal', matchesAll(TW_MOTION, body));
                if (on('font-literal'))
                    add('font-literal', matchesAll(TW_FONT, body));
                if (on('dark-variant'))
                    add('dark-variant', matchesAll(TW_DARK, body));
            }
            return;
        }
        const colon = body.indexOf(':');
        if (colon < 0 || depth === 0) return;
        const prop = body.slice(0, colon).trim().toLowerCase();
        const value = body.slice(colon + 1);
        const vOff = colon + 1;
        if (on('deep-import')) {
            for (const u of matchesAll(
                /url\(\s*['"]?([^'")]+)['"]?\s*\)/g,
                value,
            )) {
                const target = u.text.replace(
                    /^url\(\s*['"]?|['"]?\s*\)$/g,
                    '',
                );
                if (!target.startsWith('.')) continue;
                const problem = importProblem(file, target);
                if (problem)
                    report('deep-import', problem, at + vOff + u.index);
            }
        }
        const colours = [
            ...colourLiterals(
                value.replace(/url\([^)]*\)/g, (m) => ' '.repeat(m.length)),
            ),
            ...(COLOUR_PROPS.test(prop) ? namedColours(value) : []),
        ];
        const motion = motionInDeclaration(prop, value);
        const font = fontStack(prop, value);
        if (inKitRaw) {
            add('kit-raw-values', colours, vOff);
            add('kit-raw-values', motion, vOff);
            if (font) report('kit-raw-values', `${prop}: ${value}`, at);
            return;
        }
        if (on('raw-colour')) add('raw-colour', colours, vOff);
        if (on('motion-literal')) add('motion-literal', motion, vOff);
        if (on('font-literal') && font)
            report('font-literal', `${prop}: ${value}`, at);
    };
    for (let i = 0; i < clean.length; i++) {
        const c = clean[i];
        if (quote) {
            if (c === '\\') i++;
            else if (c === quote) quote = null;
            continue;
        }
        if (c === '"' || c === "'") quote = c;
        else if (c === '(') {
            let d = 1;
            while (d && ++i < clean.length) {
                if (clean[i] === '(') d++;
                else if (clean[i] === ')') d--;
            }
        } else if (c === '{') {
            segment(i, '{');
            depth++;
        } else if (c === ';') segment(i, ';');
        else if (c === '}') {
            segment(i, '}');
            depth = Math.max(0, depth - 1);
        }
    }
};

const scanScript = (file, text, on, report) => {
    const t = loadTs();
    const kind = /\.(tsx|jsx)$/.test(file) ? t.ScriptKind.TSX : t.ScriptKind.TS;
    const sf = t.createSourceFile(
        file,
        text,
        t.ScriptTarget.Latest,
        true,
        kind,
    );
    const kitRaw = on('kit-raw-values');
    const unwrap = (e) => {
        while (
            e &&
            (t.isParenthesizedExpression(e) ||
                t.isAsExpression(e) ||
                t.isSatisfiesExpression?.(e) ||
                t.isTypeAssertionExpression?.(e))
        )
            e = e.expression;
        return e;
    };
    const stringOf = (e) => {
        e = unwrap(e);
        if (!e) return null;
        if (t.isStringLiteral(e) || t.isNoSubstitutionTemplateLiteral(e))
            return e.text;
        if (t.isTemplateExpression(e))
            return [
                e.head.text,
                ...e.templateSpans.map((s) => s.literal.text),
            ].join(' ');
        return null;
    };
    const nameOf = (n) => {
        if (!n) return null;
        if (t.isIdentifier(n) || t.isPrivateIdentifier?.(n)) return n.text;
        if (t.isStringLiteral(n) || t.isNoSubstitutionTemplateLiteral(n))
            return n.text;
        if (t.isJsxNamespacedName?.(n))
            return `${n.namespace.text}:${n.name.text}`;
        return null;
    };
    const tagName = (el) => (el ? el.tagName.getText(sf) : '');
    const isDataExpr = (e) => {
        e = unwrap(e);
        return (
            t.isIdentifier(e) ||
            t.isPropertyAccessExpression(e) ||
            t.isElementAccessExpression(e) ||
            t.isCallExpression(e)
        );
    };
    const emojiOnly = (s) => s.trim() && EMOJI_ONLY.test(s) && hasEmoji(s);
    const inStyleAttr = (node) => {
        for (let p = node.parent; p; p = p.parent) {
            if (t.isJsxAttribute(p)) return nameOf(p.name) === 'style';
            if (t.isJsxElement(p) || t.isJsxSelfClosingElement(p)) return false;
        }
        return false;
    };

    const checkString = (node, s) => {
        const pos = node.getStart(sf) + 1;
        const add = (rule, hits) => {
            for (const h of hits) report(rule, h.text, pos + h.index);
        };
        const href = text.slice(Math.max(0, pos - 24), pos);
        const colours = /(?:href|to|hash|id)\s*[=:]\s*\{?\s*['"`]$/.test(href)
            ? []
            : colourLiterals(s);
        const easing = matchesAll(EASING_FN, s);
        const timed =
            /^\s*(?:\d+\.?\d*|\.\d+)m?s\s*$/.test(s) ||
            /transition|animation|cubic-bezier|\bease\b/.test(s)
                ? nonZeroTimes(s)
                : [];
        const fontDecl = matchesAll(/font-family\s*:[^;'"]+/g, s).filter((m) =>
            fontStack('font-family', m.text.replace(/^font-family\s*:/, '')),
        );
        if (kitRaw) {
            add('kit-raw-values', colours);
            add('kit-raw-values', [...easing, ...timed]);
            add('kit-raw-values', fontDecl);
            return;
        }
        if (on('raw-colour'))
            add('raw-colour', [...colours, ...matchesAll(TW_COLOUR, s)]);
        if (on('motion-literal'))
            add('motion-literal', [
                ...easing,
                ...timed,
                ...matchesAll(TW_MOTION, s),
            ]);
        if (on('font-literal'))
            add('font-literal', [...fontDecl, ...matchesAll(TW_FONT, s)]);
        if (on('dark-variant')) add('dark-variant', matchesAll(TW_DARK, s));
    };

    const checkProperty = (name, init, at) => {
        const s = stringOf(init);
        if (s == null) return;
        const css = kebab(name);
        if (ICON_NAME.test(name) && hasEmoji(s) && on('emoji-icon'))
            report('emoji-icon', `${name}: ${s}`, at);
        const colourProp = COLOUR_PROPS.test(css);
        const named = colourProp ? namedColours(s) : [];
        const font =
            name === 'fontFamily' &&
            !/^\s*var\(/.test(s) &&
            s.trim() !== 'inherit';
        if (kitRaw) {
            for (const h of named) report('kit-raw-values', h.text, at);
            if (font) report('kit-raw-values', `fontFamily: ${s}`, at);
            return;
        }
        if (on('raw-colour'))
            for (const h of named) report('raw-colour', h.text, at);
        if (on('font-literal') && font)
            report('font-literal', `fontFamily: ${s}`, at);
    };

    const checkStyleObject = (obj) => {
        for (const p of obj.properties) {
            if (
                !t.isPropertyAssignment(p) &&
                !t.isShorthandPropertyAssignment(p)
            )
                continue;
            const name = nameOf(p.name);
            if (!name || name.startsWith('--') || !VISUAL_STYLE.test(name))
                continue;
            const init = t.isPropertyAssignment(p) ? p.initializer : p.name;
            if (name === 'opacity' && isDataExpr(init)) continue;
            report(
                'visual-inline-style',
                `${name}: ${init.getText(sf)}`,
                p.getStart(sf),
            );
        }
    };

    const checkImport = (specNode) => {
        if (!specNode || !on('deep-import')) return;
        const s = stringOf(specNode);
        if (s == null) return;
        const problem = importProblem(file, s);
        if (problem) report('deep-import', problem, specNode.getStart(sf));
    };

    const visit = (node) => {
        if (t.isImportDeclaration(node) || t.isExportDeclaration(node)) {
            checkImport(node.moduleSpecifier);
            return;
        }
        if (
            t.isCallExpression(node) &&
            (node.expression.kind === t.SyntaxKind.ImportKeyword ||
                (t.isIdentifier(node.expression) &&
                    node.expression.text === 'require'))
        )
            checkImport(node.arguments[0]);
        if (
            t.isImportTypeNode?.(node) &&
            node.argument &&
            t.isLiteralTypeNode(node.argument)
        )
            checkImport(node.argument.literal);

        if (
            on('dark-variant') &&
            t.isCallExpression(node) &&
            t.isPropertyAccessExpression(node.expression) &&
            /^(?:add|toggle|remove|contains)$/.test(
                node.expression.name.text,
            ) &&
            /classList$/.test(node.expression.expression.getText(sf)) &&
            stringOf(node.arguments[0]) === 'dark'
        )
            report(
                'dark-variant',
                `classList.${node.expression.name.text}('dark')`,
                node.getStart(sf),
            );

        if (
            t.isStringLiteral(node) ||
            t.isNoSubstitutionTemplateLiteral(node)
        ) {
            const parent = node.parent;
            const isSpecifier =
                (t.isImportDeclaration(parent) ||
                    t.isExportDeclaration(parent)) &&
                parent.moduleSpecifier === node;
            if (!isSpecifier && !t.isLiteralTypeNode(parent))
                checkString(node, node.text);
        } else if (t.isTemplateExpression(node)) {
            checkString(node.head, node.head.text);
            for (const span of node.templateSpans)
                checkString(span.literal, span.literal.text);
        }

        if (t.isJsxAttribute(node)) {
            const name = nameOf(node.name);
            const init = node.initializer;
            const expr =
                init && t.isJsxExpression(init)
                    ? unwrap(init.expression)
                    : init;
            if (
                name === 'style' &&
                expr &&
                t.isObjectLiteralExpression(expr) &&
                on('visual-inline-style')
            )
                checkStyleObject(expr);
            if (name && ICON_NAME.test(name) && on('emoji-icon')) {
                const s = stringOf(expr);
                if (s != null && hasEmoji(s))
                    report('emoji-icon', `${name}="${s}"`, node.getStart(sf));
            }
            if (name && COLOUR_PROPS.test(kebab(name))) {
                const s = stringOf(expr);
                const rule = kitRaw ? 'kit-raw-values' : 'raw-colour';
                if (s != null)
                    for (const h of namedColours(s))
                        report(rule, h.text, node.getStart(sf));
            }
        }

        if (t.isPropertyAssignment(node) && !inStyleAttr(node)) {
            const name = nameOf(node.name);
            if (name) checkProperty(name, node.initializer, node.getStart(sf));
        }

        if (t.isJsxElement(node) && on('emoji-icon')) {
            const opening = node.openingElement;
            const className = opening.attributes.properties
                .filter(
                    (a) =>
                        t.isJsxAttribute(a) && nameOf(a.name) === 'className',
                )
                .map(
                    (a) =>
                        stringOf(
                            a.initializer && t.isJsxExpression(a.initializer)
                                ? a.initializer.expression
                                : a.initializer,
                        ) ?? '',
                )
                .join(' ');
            const named =
                ICON_HOLDER.test(tagName(opening)) ||
                /(?:^|[\s_-])icon(?:$|[\s_-])/i.test(className);
            const kids = node.children.filter(
                (c) =>
                    !(t.isJsxText(c) && !c.text.trim()) &&
                    !(t.isJsxExpression(c) && !c.expression),
            );
            if (named && kids.length === 1) {
                const kid = kids[0];
                const s = t.isJsxText(kid)
                    ? kid.text
                    : t.isJsxExpression(kid)
                      ? stringOf(kid.expression)
                      : null;
                if (s != null && emojiOnly(s))
                    report(
                        'emoji-icon',
                        `<${tagName(opening)}>${s.trim()}`,
                        kid.getStart(sf),
                    );
            }
        }

        if (t.isJsxText(node) && kitRaw) checkString(node, node.text);

        t.forEachChild(node, visit);
    };
    visit(sf);
};

export const rulesFor = (file) => {
    if (SKIP_ALL.some((r) => r.test(file))) return new Set();
    const rules = new Set();
    if (APP.test(file)) {
        for (const r of [
            'raw-colour',
            'visual-inline-style',
            'emoji-icon',
            'deep-import',
            'dark-variant',
            'font-literal',
            'motion-literal',
        ])
            rules.add(r);
        if (RENDERER_DATA.some((r) => r.test(file))) {
            rules.delete('raw-colour');
            rules.delete('font-literal');
            rules.delete('motion-literal');
        }
        if (!/\.(tsx|jsx)$/.test(file)) rules.delete('visual-inline-style');
        return rules;
    }
    if (/^frontend\//.test(file) && !/^frontend\/node_modules\//.test(file))
        rules.add('deep-import');
    if (KIT_SRC.test(file) && !KIT_RAW_ALLOWED.some((r) => r.test(file)))
        rules.add('kit-raw-values');
    return rules;
};

export const scan = (file, text) => {
    const rules = rulesFor(file);
    if (!rules.size || !SCANNED.test(file)) return [];
    const on = (rule) => rules.has(rule);
    const found = [];
    const report = (rule, match, pos) => {
        if (!on(rule)) return;
        found.push({
            file,
            rule,
            fingerprint: norm(match),
            ...lineCol(text, pos),
        });
    };
    if (file.endsWith('.css')) scanCss(file, text, on, report);
    else scanScript(file, text, on, report);
    const seen = new Set();
    found.sort((a, b) => a.line - b.line || a.col - b.col);
    return found.filter((v) => {
        const key = `${v.rule}\0${v.line}\0${v.col}\0${v.fingerprint}`;
        if (seen.has(key)) return false;
        seen.add(key);
        return true;
    });
};

export const tally = (violations) => {
    const out = {};
    for (const v of violations) {
        out[v.file] ??= {};
        out[v.file][v.rule] ??= {};
        out[v.file][v.rule][v.fingerprint] =
            (out[v.file][v.rule][v.fingerprint] ?? 0) + 1;
    }
    return sortDeep(out);
};

const sortDeep = (o) =>
    o && typeof o === 'object'
        ? Object.fromEntries(
              Object.keys(o)
                  .sort()
                  .map((k) => [k, sortDeep(o[k])]),
          )
        : o;

export const compare = (violations, baseline) => {
    const current = tally(violations);
    const fresh = [];
    const stale = [];
    const used = new Map();
    for (const v of violations) {
        const key = `${v.file}\0${v.rule}\0${v.fingerprint}`;
        const allowed = baseline[v.file]?.[v.rule]?.[v.fingerprint] ?? 0;
        const n = (used.get(key) ?? 0) + 1;
        used.set(key, n);
        if (n > allowed) fresh.push(v);
    }
    for (const [file, rules] of Object.entries(baseline))
        for (const [rule, prints] of Object.entries(rules))
            for (const [fingerprint, count] of Object.entries(prints)) {
                const have = current[file]?.[rule]?.[fingerprint] ?? 0;
                if (have < count)
                    stale.push({
                        file,
                        rule,
                        fingerprint,
                        count: count - have,
                    });
            }
    return { fresh, stale };
};

const listFiles = (top) =>
    execFileSync(
        'git',
        ['ls-files', '-co', '--exclude-standard', '--', 'frontend'],
        {
            cwd: top,
            encoding: 'utf8',
            maxBuffer: 1 << 28,
        },
    )
        .split('\n')
        .filter((f) => f && SCANNED.test(f) && rulesFor(f).size);

const USAGE = `Usage: node tools/lint/ui-boundary.mjs [--update-baseline] [--all]

Checks the @partytown/ui boundary (see ${SKILL}) across frontend/.
Known violations live in tools/lint/ui-boundary.baseline.json; new ones fail,
and fixed ones fail as stale until --update-baseline rewrites the file.

  --update-baseline   record the current violations as the baseline
  --all               also print baselined violations
  --help              this text

Rules:
${Object.entries(RULES)
    .map(([rule, why]) => `  ${rule.padEnd(20)} ${why}`)
    .join('\n')}

Exempt paths:
${Object.entries(EXEMPTIONS)
    .map(
        ([what, paths]) =>
            `  ${what}:\n${paths.map((p) => `    ${p.source}`).join('\n')}`,
    )
    .join('\n')}`;

const main = () => {
    const args = process.argv.slice(2);
    if (args.includes('--help')) {
        console.log(USAGE);
        return;
    }
    const top = execFileSync('git', ['rev-parse', '--show-toplevel'], {
        encoding: 'utf8',
    }).trim();
    const violations = [];
    for (const file of listFiles(top)) {
        let text;
        try {
            text = readFileSync(path.join(top, file), 'utf8');
        } catch {
            continue;
        }
        violations.push(...scan(file, text));
    }

    const counts = Object.fromEntries(Object.keys(RULES).map((r) => [r, 0]));
    for (const v of violations) counts[v.rule]++;

    if (args.includes('--update-baseline')) {
        writeFileSync(
            BASELINE,
            `${JSON.stringify(tally(violations), null, 4)}\n`,
        );
        console.log(
            `ui-boundary: baseline written to ${path.relative(process.cwd(), BASELINE)}`,
        );
        for (const [rule, n] of Object.entries(counts))
            console.log(`  ${rule.padEnd(20)} ${n}`);
        return;
    }

    let baseline = {};
    try {
        baseline = JSON.parse(readFileSync(BASELINE, 'utf8'));
    } catch {}
    const { fresh, stale } = compare(violations, baseline);
    const show = args.includes('--all') ? violations : fresh;

    const byRule = new Map();
    for (const v of show) {
        if (!byRule.has(v.rule)) byRule.set(v.rule, []);
        byRule.get(v.rule).push(v);
    }
    for (const [rule, list] of byRule) {
        console.error(`\n${rule}: ${RULES[rule]}`);
        for (const v of list)
            console.error(
                `  ${path.relative(process.cwd(), path.join(top, v.file))}:${v.line}:${v.col}  ${v.fingerprint}`,
            );
    }
    if (stale.length) {
        console.error(
            '\nstale baseline entries (fixed, well done): run node tools/lint/ui-boundary.mjs --update-baseline',
        );
        for (const s of stale)
            console.error(
                `  ${s.file}  ${s.rule}  ${s.fingerprint}${s.count > 1 ? ` (x${s.count})` : ''}`,
            );
    }

    const freshBy = Object.fromEntries(Object.keys(RULES).map((r) => [r, 0]));
    for (const v of fresh) freshBy[v.rule]++;
    const staleBy = Object.fromEntries(Object.keys(RULES).map((r) => [r, 0]));
    for (const s of stale) staleBy[s.rule] += s.count;
    const rows = Object.keys(RULES).map(
        (r) =>
            `  ${r.padEnd(20)} ${String(counts[r]).padStart(5)} found  ${String(freshBy[r]).padStart(4)} new  ${String(staleBy[r]).padStart(4)} stale`,
    );
    const failed = fresh.length || stale.length;
    (failed ? console.error : console.log)(
        `\nui-boundary:\n${rows.join('\n')}`,
    );
    if (failed) {
        console.error(
            `\n${fresh.length} new violation(s), ${stale.length} stale baseline entr${stale.length === 1 ? 'y' : 'ies'}. Read the partytown-ui skill (${SKILL}): apps compose kit parts and do layout only.`,
        );
        process.exit(1);
    }
};

if (
    process.argv[1] &&
    import.meta.url === pathToFileURL(path.resolve(process.argv[1])).href
)
    main();
