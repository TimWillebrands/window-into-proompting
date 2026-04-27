import { fn } from 'storybook/test';
import type {
    ChatGroupInfo,
    DefaultPersonaTemplate,
    LlmProviderEntry,
    Persona,
} from './model';

export * from './party-zone';

const noopMutationResult = () => ({
    mutate: fn(),
    mutateAsync: fn().mockResolvedValue(undefined),
    isPending: false,
    isError: false,
    isSuccess: false,
    isIdle: true,
    error: null,
    data: undefined,
    reset: fn(),
    status: 'idle' as const,
    variables: undefined,
    failureCount: 0,
    failureReason: null,
    isPaused: false,
    submittedAt: 0,
    context: undefined,
});

const suspenseQueryResult = <T>(data: T) => ({
    data,
    error: null,
    isError: false,
    isPending: false,
    isLoading: false,
    isSuccess: true,
    isFetching: false,
    isFetched: true,
    isStale: false,
    isPlaceholderData: false,
    refetch: fn().mockResolvedValue({ data }),
    status: 'success' as const,
    fetchStatus: 'idle' as const,
    dataUpdatedAt: 0,
    errorUpdatedAt: 0,
    failureCount: 0,
    failureReason: null,
    isFetchedAfterMount: true,
    isInitialLoading: false,
    isLoadingError: false,
    isPaused: false,
    isRefetchError: false,
    isRefetching: false,
    promise: Promise.resolve(data),
});

const queryResult = <T>(data: T) => ({
    ...suspenseQueryResult(data),
});

// ---------- Fixtures (overridable via storybook parameters or beforeEach) ----------

export const mockPersonas: Persona[] = [
    {
        id: 'persona-1',
        name: 'Indie Hacker Girl',
        bio: 'Sharp-witted millennial. Builds fast, ships often.',
        systemPrompt: 'You are Denise, a millennial indie hacker.',
    },
    {
        id: 'persona-2',
        name: 'Shaman Agile Coach',
        bio: 'Ancient being channelling agile wisdom.',
        systemPrompt: 'You are Vlad, an immortal scrum master.',
    },
    {
        id: 'persona-3',
        name: 'CTO Skeptic',
        bio: null,
        systemPrompt: 'You are a battle-hardened CTO.',
    },
];

export const mockPersonaTemplates: DefaultPersonaTemplate[] = [
    {
        name: 'Indie Hacker Girl',
        bio: 'Sharp-witted millennial.',
        systemPrompt: 'You are Denise.',
    },
    {
        name: 'Shaman Agile Coach',
        bio: 'Ancient agile coach.',
        systemPrompt: 'You are Vlad.',
    },
];

export const mockChatGroups: ChatGroupInfo[] = [
    {
        id: 'cg-1',
        name: 'Stealth Horticulture Standup',
        scenario: 'Office of a stealth horticulture startup.',
        createdAt: '2026-04-20T09:00:00Z',
    },
    {
        id: 'cg-2',
        name: 'Architecture Review',
        scenario: null,
        createdAt: '2026-04-22T14:30:00Z',
    },
];

export const mockProviders: LlmProviderEntry[] = [
    {
        id: 'prov-1',
        type: 'ollama',
        baseUrl: 'http://localhost:11434',
        apiKey: null,
        modelName: 'llama3.2',
        supportedComplexities: 1,
        isEnabled: true,
    },
    {
        id: 'prov-2',
        type: 'openrouter',
        baseUrl: 'https://openrouter.ai/api/v1',
        apiKey: 'sk-or-…',
        modelName: 'nvidia/llama-3.1-nemotron-ultra-253b-v1:free',
        supportedComplexities: 7,
        isEnabled: false,
    },
];

export const mockProviderModels = [
    'llama3.2',
    'llama3.1:70b',
    'qwen2.5-coder:32b',
];

// ---------- Hook overrides — shadow exports from `./party-zone` ----------

export const useGetPersonaSuspense = fn(() =>
    suspenseQueryResult({
        data: mockPersonas,
        status: 200,
        headers: new Headers(),
    }),
);

export const useGetPersonaDefaultsSuspense = fn(() =>
    suspenseQueryResult({
        data: mockPersonaTemplates,
        status: 200,
        headers: new Headers(),
    }),
);

export const usePutPersonaId = fn(() => noopMutationResult());
export const useDeletePersonaId = fn(() => noopMutationResult());

export const useGetPartyIdChatGroupsSuspense = fn(() =>
    suspenseQueryResult({
        data: mockChatGroups,
        status: 200,
        headers: new Headers(),
    }),
);

export const usePostPartyIdChatGroups = fn(() => noopMutationResult());

export const useGetLlmConfigProviders = fn(() =>
    queryResult({ data: mockProviders, status: 200, headers: new Headers() }),
);
export const usePostLlmConfigProviders = fn(() => noopMutationResult());
export const usePutLlmConfigProvidersId = fn(() => noopMutationResult());
export const useDeleteLlmConfigProvidersId = fn(() => noopMutationResult());
export const useGetLlmConfigProvidersIdModels = fn(() =>
    queryResult({
        data: mockProviderModels,
        status: 200,
        headers: new Headers(),
    }),
);

// Chat-window hooks (GroupChatWindow consumers)
export const useGetPartyIdSuspense = fn(() =>
    suspenseQueryResult({
        data: {
            party: {
                id: 'root',
                name: 'Root Party',
                participants: [],
            },
            personaParticipants: mockPersonas,
        },
        status: 200,
        headers: new Headers(),
    }),
);
export const usePostPartyIdPrompt = fn(() => noopMutationResult());
export const usePostPartyIdProceed = fn(() => noopMutationResult());
export const usePostPartyIdCancel = fn(() => noopMutationResult());
export const usePostPartyIdRepromptMessageId = fn(() => noopMutationResult());
export const usePutPartyIdParticipants = fn(() => noopMutationResult());
export const usePutPartyIdChatGroupsChatGroupIdScenario = fn(() =>
    noopMutationResult(),
);
export const useDeletePartyIdChatGroupsChatGroupIdMessagesMessageId = fn(() =>
    noopMutationResult(),
);
export const useDeletePartyIdChatGroupsChatGroupIdMessagesAfterMessageId = fn(
    () => noopMutationResult(),
);
