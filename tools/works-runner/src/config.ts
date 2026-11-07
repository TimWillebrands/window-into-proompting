import { readFileSync } from 'node:fs';
import path from 'node:path';
import type { Model } from './meter.ts';

export interface Config {
    token: string;
    bind: string;
    port: number;
    state: string;
    origin: string;
    branch: string;
    image: string;
    postgresImage: string;
    runtime: string;
    network: string;
    meterUrl: string;
    meterSecret: string;
    /** How the sandbox reaches the Meter's proxy. */
    meterProxy: string;
    models: Model[];
    cpus: string;
    memory: string;
    pids: string;
    turnMs: number;
    idleMs: number;
    capTokens: number;
    maxPatchBytes: number;
}

export function loadConfig(env: NodeJS.ProcessEnv = process.env): Config {
    const need = (name: string) => {
        const value = env[name];
        if (!value) throw new Error(`${name} is not set`);
        return value;
    };
    return {
        token: need('WORKS_TOKEN'),
        bind: env.WORKS_BIND ?? '127.0.0.1',
        port: Number(env.WORKS_PORT ?? 8787),
        state: path.resolve(env.WORKS_STATE ?? 'state'),
        origin: need('WORKS_ORIGIN'),
        branch: env.WORKS_BRANCH ?? 'master',
        image: env.WORKS_IMAGE ?? 'works-sandbox:latest',
        postgresImage: env.WORKS_POSTGRES_IMAGE ?? 'apache/age:release_PG16_1.6.0',
        runtime: env.WORKS_RUNTIME ?? '',
        network: env.WORKS_NETWORK ?? 'works-sandbox',
        meterUrl: env.WORKS_METER_URL ?? 'http://127.0.0.1:8789',
        meterSecret: need('WORKS_METER_SECRET'),
        meterProxy: env.WORKS_METER_PROXY ?? 'http://meter:8788/v1',
        models: JSON.parse(readFileSync(env.WORKS_MODELS ?? 'models.json', 'utf8')),
        cpus: env.WORKS_CPUS ?? '8',
        memory: env.WORKS_MEMORY ?? '12g',
        pids: env.WORKS_PIDS ?? '4096',
        turnMs: Number(env.WORKS_TURN_MINUTES ?? 45) * 60_000,
        idleMs: Number(env.WORKS_IDLE_MINUTES ?? 120) * 60_000,
        capTokens: Number(env.WORKS_CAP_TOKENS ?? 20_000_000),
        maxPatchBytes: Number(env.WORKS_MAX_PATCH_BYTES ?? 5_000_000),
    };
}
