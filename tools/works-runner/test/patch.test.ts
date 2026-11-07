import assert from 'node:assert/strict';
import { test } from 'node:test';
import { checksIn, factsOf, slugOf } from '../src/patch.ts';
import { latestOpen, type Proposition, reject } from '../src/propositions.ts';

const patch = `diff --git a/a.txt b/a.txt
index 1..2 100644
--- a/a.txt
+++ b/a.txt
@@ -1,2 +1,2 @@
 same
-old
+new
diff --git a/dir/b.txt b/dir/b.txt
new file mode 100644
--- /dev/null
+++ b/dir/b.txt
@@ -0,0 +1 @@
+-- not a removal
`;

test('counts what a diff touched, headers excluded', () => {
    assert.deepEqual(factsOf(patch), { files: 2, added: 2, removed: 1, paths: ['a.txt', 'dir/b.txt'] });
    assert.deepEqual(factsOf(''), { files: 0, added: 0, removed: 0, paths: [] });
});

test('slugs are branch-safe and never empty', () => {
    assert.equal(slugOf('  Fix the Doors! (again) '), 'fix-the-doors-again');
    assert.equal(slugOf('???'), 'proposition');
});

test('reads check lines and ignores build noise', () => {
    const out = 'Restored\n::check build pass 20\n  warning\n::check tests fail 46 1040/1058\n';
    assert.deepEqual(checksIn(out), [
        { name: 'build', passed: true, seconds: 20, detail: '' },
        { name: 'tests', passed: false, seconds: 46, detail: '1040/1058' },
    ]);
});

const proposition = (n: number, parent: number | null, status: Proposition['status'] = 'open'): Proposition => ({
    n, parent, status, commission: 'c', title: 't', why: 'w', author: 'a', branch: `proposition/${n}`, baseSha: '', sha: '', note: null, checks: [], createdAt: 0,
});

test('new work stacks on the newest open Proposition', () => {
    assert.equal(latestOpen([proposition(1, null), proposition(2, 1)])?.n, 2);
    assert.equal(latestOpen([proposition(1, null, 'merged')]), null);
});

test('rejection takes down what is stacked on it, nothing else', () => {
    const all = [proposition(1, null), proposition(2, 1), proposition(3, 2), proposition(4, null)];
    assert.equal(reject(all, 2, 'no'), true);
    assert.deepEqual(all.map((p) => p.status), ['open', 'rejected', 'fallen', 'open']);
    assert.equal(all[1].note, 'no');
    assert.equal(reject(all, 2, 'again'), false);
});
