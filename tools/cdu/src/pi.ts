import { mkdirSync, writeFileSync } from 'node:fs';
import path from 'node:path';
import {
    type AgentSession,
    createAgentSession,
    createMcpExtension,
    DefaultResourceLoader,
    ModelRuntime,
    SessionManager,
} from '@earendil-works/pi-coding-agent';
import type { Mind } from './cdu.ts';
import type { Model } from './meter.ts';

export interface PiOptions {
    repo: string;
    agentDir: string;
    meterUrl: string;
    /** The env var holding the tab token, so it's never written to disk. */
    tabEnv: string;
    models: (Model & { reasoning?: boolean })[];
    model?: string;
    thinking?: 'off' | 'low' | 'medium' | 'high';
    databaseUrlEnv?: string;
    soul: string;
}

/** Pi's models.json: one provider, the Meter, with the tab as its key. */
export function piModels(options: Pick<PiOptions, 'meterUrl' | 'tabEnv' | 'models'>) {
    return {
        providers: {
            meter: {
                name: 'Meter',
                baseUrl: options.meterUrl,
                api: 'openai-completions',
                apiKey: `$${options.tabEnv}`,
                compat: { supportsDeveloperRole: false, supportsStore: false },
                models: options.models.map((m) => ({
                    id: m.id,
                    name: m.label,
                    reasoning: m.reasoning ?? false,
                    cost: { input: m.in, output: m.out, cacheRead: 0, cacheWrite: 0 },
                    contextWindow: m.context,
                    maxTokens: m.output,
                })),
            },
        },
    };
}

/** Pi's mcp.json: the live database over stdio, its tools declared directly. */
export function piMcp(databaseUrlEnv?: string) {
    if (!databaseUrlEnv) return { mcpServers: {} };
    return {
        mcpServers: {
            postgres: {
                command: 'postgres-mcp',
                args: ['--access-mode=unrestricted'],
                env: { DATABASE_URI: `\${${databaseUrlEnv}}` },
                exposure: 'direct',
                description: 'The live world database, read and write.',
            },
        },
    };
}

/** The CDU's mind: one Pi session, resumed across restarts, with its soul appended to Pi's own prompt. */
export async function createPiMind(options: PiOptions): Promise<Mind> {
    mkdirSync(options.agentDir, { recursive: true });
    writeFileSync(path.join(options.agentDir, 'models.json'), JSON.stringify(piModels(options), null, 2));
    writeFileSync(path.join(options.agentDir, 'mcp.json'), JSON.stringify(piMcp(options.databaseUrlEnv), null, 2));

    let soul = options.soul;
    const modelRuntime = await ModelRuntime.create({
        authPath: path.join(options.agentDir, 'auth.json'),
        modelsPath: path.join(options.agentDir, 'models.json'),
        refreshOnCreate: false,
    });
    const model = modelRuntime.getModel('meter', options.model ?? options.models[0].id) ?? modelRuntime.getModel('meter', options.models[0].id);
    if (!model) throw new Error(`no model ${options.model} behind the Meter`);

    const resourceLoader = new DefaultResourceLoader({
        cwd: options.repo,
        agentDir: options.agentDir,
        extensionFactories: [createMcpExtension()],
        appendSystemPromptOverride: (base) => [...base, soul],
    });
    await resourceLoader.reload();

    const { session } = await createAgentSession({
        cwd: options.repo,
        agentDir: options.agentDir,
        model,
        modelRuntime,
        thinkingLevel: options.thinking ?? 'medium',
        resourceLoader,
        sessionManager: SessionManager.continueRecent(options.repo, path.join(options.agentDir, 'sessions')),
    });
    await session.bindExtensions({});

    return {
        model: model.id,
        think: (text, onEvent) => think(session, text, onEvent),
        async resoul(next) {
            soul = next;
            await session.reload();
        },
    };
}

async function think(session: AgentSession, text: string, onEvent: (event: Record<string, unknown>) => void) {
    const off = session.subscribe((event) => onEvent(event as unknown as Record<string, unknown>));
    try {
        await session.prompt(text);
    } finally {
        off();
    }
    const last = session.state.messages.at(-1) as { role?: string; stopReason?: string; errorMessage?: string } | undefined;
    if (last?.role === 'assistant' && last.stopReason === 'error') throw new Error(last.errorMessage ?? 'the model failed');
    return session.getLastAssistantText() ?? '';
}
