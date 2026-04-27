import type { LlmProviderEntry } from '../../../api/model/llmProviderEntry';

export const JOB_COMPLEXITIES = [
    { value: 1, label: 'General' },
    { value: 2, label: 'Character Voice' },
    { value: 4, label: 'Character Thoughts' },
] as const;

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
] as const;

export const SECTION_NAMES: Record<string, string> = {
    home: 'Control Panel',
    'llm-providers': 'Language Model Providers',
};
