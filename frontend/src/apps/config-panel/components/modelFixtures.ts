import type { LlmProviderEntry } from '../../../api/model/llmProviderEntry';
import type { ModelAssignment } from '../../../api/model/modelAssignment';
import type { Persona } from '../../../api/model/persona';
import type { PersonaRoute } from '../../../api/model/personaRoute';

/** Story data: half the personas on Haiku, half on Nemotron, a local box behind them, Opus for pins. */
export const providers: LlmProviderEntry[] = [
    {
        id: 'haiku',
        type: 'anthropic',
        baseUrl: 'https://api.anthropic.com',
        modelName: 'claude-haiku-5-5',
        supportedComplexities: 7,
        isEnabled: true,
        share: 50,
    },
    {
        id: 'nemotron',
        type: 'openrouter',
        baseUrl: 'https://openrouter.ai/api/v1',
        modelName: 'nvidia/nemotron-3-super:free',
        supportedComplexities: 7,
        isEnabled: true,
        share: 50,
    },
    {
        id: 'local',
        type: 'ollama',
        baseUrl: 'http://100.91.200.46:11434',
        modelName: 'llama3.2',
        supportedComplexities: 1,
        isEnabled: true,
        priority: 10,
        tailnet: 'home',
    },
    {
        id: 'jev',
        type: 'openrouter',
        baseUrl: 'https://openrouter.ai/api/v1',
        modelName: '~typesafe/jev-latest',
        supportedComplexities: 8,
        isEnabled: true,
    },
    {
        id: 'opus',
        type: 'openrouter',
        baseUrl: 'https://openrouter.ai/api/v1',
        modelName: 'anthropic/claude-opus-5.5',
        supportedComplexities: 1,
        isEnabled: true,
        reserved: true,
    },
];

export const personas: Persona[] = [
    'Bram',
    'Denise',
    'Imke',
    'Joost',
    'Mira',
    'Vlad',
    'Wendeline',
].map((name) => ({ id: name.toLowerCase(), name }));

export const routes: PersonaRoute[] = [
    { personaId: 'vlad', tier: 2, providerId: 'opus' },
    { personaId: 'mira', tier: 1, providerId: 'local' },
];

const NOBODY = '00000000-0000-0000-0000-000000000000';
const HOMES: Record<string, string> = {
    bram: 'haiku',
    denise: 'nemotron',
    imke: 'haiku',
    joost: 'nemotron',
    mira: 'nemotron',
    vlad: 'haiku',
    wendeline: 'haiku',
    [NOBODY]: 'haiku',
};

/** What the router would answer for the data above; `down` providers send their personas elsewhere. */
export const assignmentsFor = (down: string[] = []): ModelAssignment[] =>
    Object.entries(HOMES).flatMap(([personaId, home]) =>
        ([1, 2, 4, 8] as const).map((tier) => {
            const pinnedTo =
                routes.find((r) => r.personaId === personaId && r.tier === tier)
                    ?.providerId ?? null;
            const pool =
                tier === 8
                    ? ['jev']
                    : [home, home === 'haiku' ? 'nemotron' : 'haiku'];
            return {
                personaId,
                tier,
                providerId:
                    [pinnedTo, ...pool].find(
                        (id) => id && !down.includes(id),
                    ) ?? null,
                pinnedTo,
            };
        }),
    );
