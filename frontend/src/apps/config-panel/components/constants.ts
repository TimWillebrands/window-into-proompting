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

export const providerLabel = (p: LlmProviderEntry) =>
    `${p.modelName ?? 'No model'} · ${p.type === 'ollama' ? 'Local' : 'OpenRouter'}`;

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

export const SECTIONS = [
    {
        id: 'llm-providers',
        label: 'Language Model Providers',
        icon: '🤖',
        description: 'Configure AI model endpoints and API keys',
    },
    {
        id: 'persona-models',
        label: 'Persona Models',
        icon: '🎭',
        description: 'Give a persona its own model for a kind of work',
    },
] as const;

export const SECTION_NAMES: Record<string, string> = {
    home: 'Control Panel',
    'llm-providers': 'Language Model Providers',
    'persona-models': 'Persona Models',
};
