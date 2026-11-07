import { readFileSync } from 'node:fs';
import { createMeter, type Tab } from './meter.ts';

const need = (name: string) => {
    const value = process.env[name];
    if (!value) throw new Error(`${name} is not set`);
    return value;
};

// The CDU's tab: a daily allowance that exists from the start, so nothing has to open it.
const tabs: Tab[] = process.env.METER_TAB_TOKEN
    ? [{
        token: process.env.METER_TAB_TOKEN,
        run: process.env.METER_TAB_NAME ?? 'cdu',
        capUsd: Number(process.env.METER_TAB_DAILY_USD ?? 5),
        capTokens: Number(process.env.METER_TAB_DAILY_TOKENS ?? 200_000_000),
        usd: 0, tokens: 0, calls: 0, daily: true,
    }]
    : [];

const meter = createMeter({
    upstream: need('METER_UPSTREAM_URL').replace(/\/$/, ''),
    key: process.env.METER_UPSTREAM_KEY ?? '',
    secret: need('METER_SECRET'),
    models: JSON.parse(readFileSync(process.env.METER_MODELS ?? '/app/models.json', 'utf8')),
    balanceUrl: process.env.METER_BALANCE_URL || undefined,
    tabs,
    statePath: process.env.METER_STATE || undefined,
});

meter.proxy.listen(Number(process.env.METER_PORT ?? 8788), '0.0.0.0');
meter.admin.listen(Number(process.env.METER_ADMIN_PORT ?? 8789), '0.0.0.0');
console.log(`meter up, ${tabs.length} declared tab(s)`);
