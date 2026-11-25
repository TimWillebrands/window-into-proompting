import type { IconName, Tone } from '@partytown/ui';
import type { LlmProviderEntry } from '../../../api/model/llmProviderEntry';

export const GENERAL = 1;
export const DECISIONS = 8;

export const JOB_COMPLEXITIES = [
    { value: 1, label: 'General', hint: 'Plans and daily life' },
    { value: 2, label: 'Character Voice', hint: 'What they say and write' },
    {
        value: 4,
        label: 'Character Thoughts',
        hint: 'Quick reactions and reflection',
    },
    {
        value: DECISIONS,
        label: 'Decisions',
        hint: 'System One weighing; the model must serve /systemone',
    },
] as const;

export const REASONING_EFFORTS = [
    { value: 0, label: 'Default', hint: 'the model decides' },
    { value: 1, label: 'Off', hint: 'answers straight away' },
    { value: 2, label: 'Low', hint: 'up to 512 tokens' },
    { value: 3, label: 'Medium', hint: 'up to 2,048 tokens' },
    { value: 4, label: 'High', hint: 'up to 8,192 tokens' },
    { value: 5, label: 'Max', hint: 'no limit, can take minutes' },
] as const;

export const PROVIDER_STATES: ReadonlyArray<{ label: string; tone: Tone }> = [
    { label: 'Ready', tone: 'go' },
    { label: 'Benched', tone: 'caution' },
    { label: 'Offline', tone: 'danger' },
];

export const BENCHED = 1;
export const OFFLINE = 2;

export const PROVIDER_TYPES = {
    ollama: {
        label: 'Local server',
        option: 'Local server (Ollama, llama.cpp, …)',
        short: 'Local',
        icon: 'local-server',
        baseUrl: 'http://localhost:11434',
        keyHint: null,
        modelHint: 'e.g. llama3.2',
    },
    openrouter: {
        label: 'OpenRouter',
        option: 'OpenRouter',
        short: 'OpenRouter',
        icon: 'internet',
        baseUrl: 'https://openrouter.ai/api/v1',
        keyHint: 'sk-or-...',
        modelHint: 'e.g. nvidia/llama-3.1-nemotron-ultra-253b-v1:free',
    },
    anthropic: {
        label: 'Claude',
        option: 'Claude (Anthropic API)',
        short: 'Claude',
        icon: 'claude',
        baseUrl: 'https://api.anthropic.com',
        keyHint: 'sk-ant-...',
        modelHint: 'e.g. claude-haiku-5-5',
    },
} as const satisfies Record<string, { icon: IconName; [key: string]: unknown }>;

export type ProviderType = keyof typeof PROVIDER_TYPES;

export const providerType = (type?: string | null) =>
    PROVIDER_TYPES[(type as ProviderType) ?? 'ollama'] ?? PROVIDER_TYPES.ollama;

export const providerLabel = (p: LlmProviderEntry) =>
    `${p.modelName ?? 'No model'} · ${providerType(p.type).short}`;

export const EMPTY_ENTRY: LlmProviderEntry = {
    type: 'ollama',
    baseUrl: 'http://localhost:11434',
    apiKey: null,
    modelName: null,
    supportedComplexities: 1,
    isEnabled: true,
};

export const SERIES = [
    'var(--ui-series-1)',
    'var(--ui-series-2)',
    'var(--ui-series-3)',
    'var(--ui-series-4)',
    'var(--ui-series-5)',
    'var(--ui-series-6)',
    'var(--ui-series-7)',
    'var(--ui-series-8)',
] as const;
export const SERIES_OTHER = 'var(--ui-series-other)';

export const seriesColor = (providers: LlmProviderEntry[], id?: string) => {
    const index = providers.findIndex((p) => p.id === id);
    return SERIES[index] ?? SERIES_OTHER;
};

export const shortModel = (p?: LlmProviderEntry) =>
    p?.modelName?.split('/').pop() ?? p?.baseUrl ?? 'No model';

export const SECTIONS = [
    {
        id: 'llm-providers',
        label: 'Language Models',
        icon: 'models',
        description: 'Which models there are and who speaks through which',
    },
] as const satisfies ReadonlyArray<{ icon: IconName; [key: string]: unknown }>;

export const SECTION_NAMES: Record<string, string> = {
    home: 'Control Panel',
    'llm-providers': 'Language Models',
    'persona-models': 'Language Models',
};
