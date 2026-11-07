import { readFileSync } from 'node:fs';
import { createMeter } from './meter.ts';

const need = (name: string) => {
    const value = process.env[name];
    if (!value) throw new Error(`${name} is not set`);
    return value;
};

const meter = createMeter({
    upstream: need('WORKS_UPSTREAM_URL').replace(/\/$/, ''),
    key: process.env.WORKS_UPSTREAM_KEY ?? '',
    secret: need('WORKS_METER_SECRET'),
    models: JSON.parse(readFileSync(process.env.WORKS_MODELS ?? '/app/models.json', 'utf8')),
    balanceUrl: process.env.WORKS_BALANCE_URL || undefined,
});

meter.proxy.listen(8788, '0.0.0.0');
meter.admin.listen(8789, '0.0.0.0');
console.log('meter up');
