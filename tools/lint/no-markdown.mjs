#!/usr/bin/env node
import { execFileSync } from 'node:child_process';

const PROSE = /\.(md|mdx|markdown|rst|adoc)$/i;

const ALLOWED = [
    /^(CLAUDE|CONTEXT|README)\.md$/,
    /^docs\/adr\/\d{4}-[a-z0-9-]+\.md$/,
    /^docs\/rfc\/\d{4}-\d{2}-\d{2}-[a-z0-9-]+\.md$/,
    /^docs\/agents\/(domain|issue-tracker|triage-labels)\.md$/,
    /^(frontend|tools\/cdu|tools\/figure)\/README\.md$/,
    /^backend\/Services\/ResponsePipeline\/Prompts\/[A-Za-z]+\.md$/,
    /^tools\/cdu\/(book|soul)\//,
    /^\.(agents|claude|opencode)\//,
];

const MESSAGE = [
    'Markdown is not allowed here. It lives only in ADRs (docs/adr/NNNN-slug.md), RFCs, skills, prompts and the CDU book.',
    'What code does belongs in the code; how to run a script in its own usage output; why in the commit message or the issue.',
].join('\n');

const top = execFileSync('git', ['rev-parse', '--show-toplevel'], { encoding: 'utf8' }).trim();
const files = execFileSync('git', ['ls-files', '-co', '--exclude-standard'], { cwd: top, encoding: 'utf8' })
    .split('\n')
    .filter((f) => PROSE.test(f) && !ALLOWED.some((a) => a.test(f)));

if (files.length) {
    for (const f of files) console.error(f);
    console.error(`\n${files.length} markdown file(s). ${MESSAGE}`);
    process.exit(1);
}
