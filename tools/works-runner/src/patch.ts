export interface DiffFacts {
    files: number;
    added: number;
    removed: number;
    paths: string[];
}

const MaxPaths = 12;

/** What a unified diff touched, counted from the text rather than from anyone's say-so. */
export function factsOf(patch: string): DiffFacts {
    const paths: string[] = [];
    let added = 0;
    let removed = 0;
    let inHunk = false;
    for (const line of patch.split('\n')) {
        if (line.startsWith('diff --git ')) {
            inHunk = false;
            paths.push(line.replace(/^diff --git a\/.* b\//, ''));
        } else if (line.startsWith('@@')) inHunk = true;
        else if (inHunk && line.startsWith('+')) added += 1;
        else if (inHunk && line.startsWith('-')) removed += 1;
    }
    return { files: paths.length, added, removed, paths: paths.slice(0, MaxPaths) };
}

/** A link in a patch could aim a later file write outside the clone on the host. */
export const hasSymlink = (patch: string) => /^(new file mode|new mode|old mode|index [0-9a-f]+\.\.[0-9a-f]+) 120000$/m.test(patch);

export const slugOf = (title: string) =>
    title
        .toLowerCase()
        .replace(/[^a-z0-9]+/g, '-')
        .replace(/^-|-$/g, '')
        .slice(0, 40) || 'proposition';

export interface CheckResult {
    name: string;
    passed: boolean;
    seconds: number;
    detail: string;
}

/** Reads the sandbox's `::check name pass|fail seconds detail` lines. */
export function checksIn(output: string): CheckResult[] {
    const results: CheckResult[] = [];
    for (const line of output.split('\n')) {
        const match = /^::check (\S+) (pass|fail) (\d+)(?: (.*))?$/.exec(line.trim());
        if (match) results.push({ name: match[1], passed: match[2] === 'pass', seconds: Number(match[3]), detail: match[4] ?? '' });
    }
    return results;
}
