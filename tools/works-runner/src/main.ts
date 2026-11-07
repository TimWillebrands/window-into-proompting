import { execFile } from 'node:child_process';
import path from 'node:path';
import { promisify } from 'node:util';
import { createApi } from './api.ts';
import { loadConfig } from './config.ts';
import { Fabric } from './fabric.ts';
import { Runs, type Meter } from './runs.ts';
import { Sandbox } from './sandbox.ts';

const exec = promisify(execFile);
const config = loadConfig();

const admin = (method: string, url: string, body?: unknown) =>
    fetch(`${config.meterUrl}${url}`, {
        method,
        headers: { 'x-meter-secret': config.meterSecret, 'content-type': 'application/json' },
        body: body === undefined ? undefined : JSON.stringify(body),
    });

const meter: Meter = {
    async open(token, run, capUsd, capTokens, usd, tokens) {
        const answer = await admin('POST', '/tabs', { token, run, capUsd, capTokens, usd, tokens });
        if (!answer.ok) throw new Error(`meter refused the tab: ${answer.status}`);
    },
    async raise(token, addUsd) {
        await admin('POST', `/tabs/${token}/raise`, { addUsd });
    },
    async read(token) {
        const answer = await admin('GET', `/tabs/${token}`);
        return answer.ok ? ((await answer.json()) as { usd: number; tokens: number }) : null;
    },
    async close(token) {
        await admin('DELETE', `/tabs/${token}`);
    },
    async balance() {
        const answer = await admin('GET', '/balance');
        return answer.ok ? ((await answer.json()) as { usd: number | null }).usd : null;
    },
};

// Stray agents from a crash would otherwise hold the box.
const strays = (await exec('docker', ['ps', '-aq', '--filter', 'label=works=1'])).stdout.split('\n').filter(Boolean);
if (strays.length > 0) await exec('docker', ['rm', '-f', ...strays]);

const fabric = new Fabric(path.join(config.state, 'fabric.git'), config.origin, config.branch);
await fabric.sync();
const runs = new Runs(config, fabric, new Sandbox(config), meter);
setInterval(() => runs.sweep().catch(console.error), 60_000);

createApi(config, runs, fabric, meter).listen(config.port, config.bind, () => {
    console.log(`works runner on ${config.bind}:${config.port}, origin ${config.origin} ${config.branch}`);
});
