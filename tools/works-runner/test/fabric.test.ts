import assert from 'node:assert/strict';
import { execFileSync } from 'node:child_process';
import { mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { after, before, test } from 'node:test';
import { Fabric } from '../src/fabric.ts';

const root = mkdtempSync(path.join(os.tmpdir(), 'fabric-test-'));
const origin = path.join(root, 'origin');
const fabric = new Fabric(path.join(root, 'fabric.git'), origin, 'master');
const git = (cwd: string, ...args: string[]) => execFileSync('git', args, { cwd, encoding: 'utf8' }).trim();
const who = ['-c', 'user.name=t', '-c', 'user.email=t@t'];

const patch = `diff --git a/world.txt b/world.txt
index 0000000..1111111 100644
--- a/world.txt
+++ b/world.txt
@@ -1 +1 @@
-old
+new
`;

before(async () => {
    execFileSync('git', ['init', '-q', '-b', 'master', origin]);
    writeFileSync(path.join(origin, 'world.txt'), 'old\n');
    git(origin, 'add', '.');
    git(origin, ...who, 'commit', '-q', '-m', 'first');
    await fabric.sync();
});

after(() => rmSync(root, { recursive: true, force: true }));

test('a patch becomes one commit by the architect, and a mailbox git am takes', async () => {
    const sha = await fabric.propose('proposition/1-new', 'main', patch, { name: 'Ria van der Werff', title: 'Change it', why: 'Because', commission: 'c1' });
    assert.equal(await fabric.sha('proposition/1-new'), sha);
    assert.equal(await fabric.merged('proposition/1-new'), false);

    const mail = await fabric.mailbox(await fabric.sha('main'), 'proposition/1-new');
    assert.match(mail, /From: Ria van der Werff <ria\.van\.der\.werff@works\.proompting\.party>/);
    assert.match(mail, /Commission: c1/);

    writeFileSync(path.join(root, 'mail'), `${mail}\n`);
    git(origin, ...who, 'am', '-q', path.join(root, 'mail'));
    assert.equal(readFileSync(path.join(origin, 'world.txt'), 'utf8'), 'new\n');

    await fabric.sync();
    assert.equal(await fabric.merged('proposition/1-new'), true);
});

test('a patch that does not apply fails loudly and leaves no branch', async () => {
    const stale = patch.replace('-old', '-something else');
    await assert.rejects(fabric.propose('proposition/2-stale', 'main', stale, { name: 'A', title: 'T', why: 'W', commission: 'c2' }));
    assert.throws(() => git(fabric.dir, 'rev-parse', '--verify', '-q', 'proposition/2-stale'));
});
