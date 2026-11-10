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

/** Router states from `ProviderState` (ADR 0040), indexed by its number. */
export const PROVIDER_STATES = [
    { label: 'Ready', color: '#afa', border: 'rgba(60,180,60,0.5)' },
    { label: 'Benched', color: '#fd8', border: 'rgba(255,190,60,0.55)' },
    { label: 'Offline', color: '#faa', border: 'rgba(220,60,60,0.55)' },
] as const;

export const BENCHED = 1;
export const OFFLINE = 2;

/** Backend provider types (`LlmEndpoints.Resolve`); a key means the editor asks for one. */
export const PROVIDER_TYPES = {
    ollama: {
        label: 'Local server',
        option: 'Local server (Ollama, llama.cpp, …)',
        short: 'Local',
        icon: '🦙',
        baseUrl: 'http://localhost:11434',
        keyHint: null,
        modelHint: 'e.g. llama3.2',
    },
    openrouter: {
        label: 'OpenRouter',
        option: 'OpenRouter',
        short: 'OpenRouter',
        icon: '🌐',
        baseUrl: 'https://openrouter.ai/api/v1',
        keyHint: 'sk-or-...',
        modelHint: 'e.g. nvidia/llama-3.1-nemotron-ultra-253b-v1:free',
    },
    anthropic: {
        label: 'Claude',
        option: 'Claude (Anthropic API)',
        short: 'Claude',
        icon: '✳️',
        baseUrl: 'https://api.anthropic.com',
        keyHint: 'sk-ant-...',
        modelHint: 'e.g. claude-haiku-5-5',
    },
} as const;

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

export const inputStyle: React.CSSProperties = {
    width: '100%',
    padding: '6px 8px',
    fontSize: 12,
    background: 'rgba(255,255,255,0.9)',
    border: '1px solid #7F9DB9',
    color: '#000',
    boxSizing: 'border-box',
};

export const selectStyle: React.CSSProperties = inputStyle;

/** Categorical hues, fixed order, validated on the light sheet; a provider keeps its slot. */
export const SERIES = [
    '#2a78d6',
    '#eb6834',
    '#1baf7a',
    '#eda100',
    '#e87ba4',
    '#008300',
    '#6250d6',
    '#e34948',
] as const;
export const SERIES_OTHER = '#8a8a86';

export const seriesColor = (providers: LlmProviderEntry[], id?: string) => {
    const index = providers.findIndex((p) => p.id === id);
    return SERIES[index] ?? SERIES_OTHER;
};

/** The model without its vendor path: what fits a table cell. */
export const shortModel = (p?: LlmProviderEntry) =>
    p?.modelName?.split('/').pop() ?? p?.baseUrl ?? 'No model';

export const SHEET: React.CSSProperties = {
    background: 'rgba(252,252,251,0.96)',
    border: '1px solid rgba(255,255,255,0.9)',
    borderRadius: 14,
    boxShadow: '0 6px 16px -8px rgba(0,0,0,0.45)',
    padding: '14px 16px',
    color: '#0b0b0b',
};

export const INK_MUTED = '#52514e';

export const SECTIONS = [
    {
        id: 'llm-providers',
        label: 'Language Models',
        icon: '🤖',
        description: 'Which models there are and who speaks through which',
    },
] as const;

export const SECTION_NAMES: Record<string, string> = {
    home: 'Control Panel',
    'llm-providers': 'Language Models',
    // Old history entries from when this was its own screen.
    'persona-models': 'Language Models',
};
