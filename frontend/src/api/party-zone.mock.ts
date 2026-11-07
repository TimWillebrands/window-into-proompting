import { fn } from 'storybook/test';
import type {
    ChatGroupInfo,
    CommissionSummary,
    DefaultPersonaTemplate,
    LlmProviderEntry,
    MemoryGraphDto,
    Persona,
    PersonaRoute,
    StanceRecord,
} from './model';
import { StanceTargetKind } from './model';

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
    {
        id: 'prov-3',
        type: 'openrouter',
        baseUrl: 'https://openrouter.ai/api/v1',
        apiKey: 'sk-or-…',
        modelName: 'anthropic/claude-opus-5.5',
        supportedComplexities: 1,
        isEnabled: true,
        reserved: true,
    },
];

export const mockPersonaRoutes: PersonaRoute[] = [
    { personaId: mockPersonas[0].id, tier: 2, providerId: 'prov-3' },
    { personaId: mockPersonas[0].id, tier: 4, providerId: 'prov-2' },
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
export const useGetWorldsPartyIdLivesSuspense = fn(() =>
    suspenseQueryResult({
        data: mockPersonas.slice(0, 1).map((p) => ({
            personaId: p.id ?? '',
            name: p.name ?? '',
            driver: 'LLM' as const,
            chattiness: 0.5,
            busy: false,
            isPlayer: false,
            lastActivity: null,
        })),
        status: 200,
        headers: new Headers(),
    }),
);
export const usePostWorldsPartyIdResidents = fn(() => noopMutationResult());
export const useDeleteWorldsPartyIdResidentsPersonaId = fn(() =>
    noopMutationResult(),
);

export const useGetPartyIdChatGroupsSuspense = fn(() =>
    suspenseQueryResult({
        data: mockChatGroups,
        status: 200,
        headers: new Headers(),
    }),
);

// Hand-crafted dataset covering all six node kinds and all four edge kinds so the
// MemoryGraphApp story exercises every code path in the visual encoding without
// standing up Postgres+AGE.
export const mockMemoryGraph: MemoryGraphDto = {
    nodes: [
        { id: 'room:cg-1', kind: 'Room' },
        { id: 'msg:cg-1:42', kind: 'Message' },
        { id: 'msg:cg-1:43', kind: 'Message' },
        {
            id: 'event:ev-1',
            kind: 'Event',
            description: 'Denise pitched a stealth horticulture demo.',
            createdAt: '2026-05-10T10:00:00Z',
        },
        {
            id: 'event:ev-2',
            kind: 'Event',
            description: 'Vlad reframed the standup as a kanban.',
            createdAt: '2026-05-10T10:05:00Z',
        },
        {
            id: 'concept:horticulture',
            kind: 'Concept',
            display: 'Horticulture',
        },
        { id: 'concept:kanban', kind: 'Concept', display: 'Kanban' },
        {
            id: 'part:persona-1:00000000-0000-0000-0000-000000000000',
            kind: 'Participant',
        },
        {
            id: 'part:persona-2:00000000-0000-0000-0000-000000000000',
            kind: 'Participant',
        },
        { id: 'persona:persona-1', kind: 'Persona' },
        { id: 'persona:persona-2', kind: 'Persona' },
    ],
    links: [
        { source: 'event:ev-1', target: 'msg:cg-1:42', kind: 'ANCHORED_TO' },
        { source: 'event:ev-2', target: 'msg:cg-1:43', kind: 'ANCHORED_TO' },
        { source: 'event:ev-1', target: 'concept:horticulture', kind: 'ABOUT' },
        { source: 'event:ev-2', target: 'concept:kanban', kind: 'ABOUT' },
        {
            source: 'event:ev-1',
            target: 'part:persona-2:00000000-0000-0000-0000-000000000000',
            kind: 'ABOUT',
        },
        {
            source: 'part:persona-1:00000000-0000-0000-0000-000000000000',
            target: 'event:ev-1',
            kind: 'RECOLLECTS',
            snippet: 'you pitched horticulture to a sceptical Vlad',
            ts: '2026-05-10T10:00:01Z',
        },
        {
            source: 'part:persona-2:00000000-0000-0000-0000-000000000000',
            target: 'event:ev-2',
            kind: 'RECOLLECTS',
            snippet: "you turned Denise's pitch into a kanban",
            ts: '2026-05-10T10:05:01Z',
        },
        {
            source: 'persona:persona-1',
            target: 'part:persona-1:00000000-0000-0000-0000-000000000000',
            kind: 'HAS_PARTICIPANT',
        },
        {
            source: 'persona:persona-2',
            target: 'part:persona-2:00000000-0000-0000-0000-000000000000',
            kind: 'HAS_PARTICIPANT',
        },
    ],
};

export const useGetPartiesPartyIdMemoryGraphSuspense = fn(() =>
    suspenseQueryResult({
        data: mockMemoryGraph,
        status: 200,
        headers: new Headers(),
    }),
);

export const usePostPartyIdChatGroups = fn(() => noopMutationResult());

export const useGetLlmConfigProviders = fn(() =>
    queryResult({ data: mockProviders, status: 200, headers: new Headers() }),
);
export const useGetLlmConfigPersonaRoutes = fn(() =>
    queryResult({
        data: mockPersonaRoutes,
        status: 200,
        headers: new Headers(),
    }),
);
export const usePutLlmConfigPersonaRoutesPersonaId = fn(() =>
    noopMutationResult(),
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
export const useGetLlmConfigTailnets = fn(() =>
    queryResult({
        data: ['home', 'work'],
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

// Stance floor (ADR 0016). Two appends toward Vlad show latest-wins: the newer edge is
// current, the older one is dimmed history.
export const mockStances: StanceRecord[] = [
    {
        id: 'stance-2',
        valence: -0.7,
        reasoning:
            "Vlad exaggerates — you've caught him inflating three stories",
        ts: '2026-05-11T09:00:00Z',
        targetKind: StanceTargetKind.Participant,
        targetPersonaId: 'persona-2',
        targetConceptName: null,
        targetConceptDisplay: null,
        isCurrent: true,
    },
    {
        id: 'stance-1',
        valence: 0.7,
        reasoning: 'you admire how Vlad tells a story',
        ts: '2026-05-10T09:00:00Z',
        targetKind: StanceTargetKind.Participant,
        targetPersonaId: 'persona-2',
        targetConceptName: null,
        targetConceptDisplay: null,
        isCurrent: false,
    },
    {
        id: 'stance-3',
        valence: 0.8,
        reasoning: 'Lisp is elegant once it clicks',
        ts: '2026-05-11T10:00:00Z',
        targetKind: StanceTargetKind.Concept,
        targetPersonaId: null,
        targetConceptName: 'lisp',
        targetConceptDisplay: 'Lisp',
        isCurrent: true,
    },
];

export const useGetPersona = fn(() =>
    queryResult({ data: mockPersonas, status: 200, headers: new Headers() }),
);
export const useGetPartiesPartyIdMemoryParticipantsPersonaIdStances = fn(() =>
    queryResult({ data: mockStances, status: 200, headers: new Headers() }),
);
export const usePostPartiesPartyIdMemoryParticipantsPersonaIdStances = fn(() =>
    noopMutationResult(),
);

const worksCommissions: CommissionSummary[] = [
    {
        id: 'c-working',
        architectId: 'p1',
        architectName: 'Denise',
        kind: 'Change',
        title: 'Stop Life turns dying on the three-minute wall',
        why: 'A quarter of what people do here fails, most of it the same timeout.',
        brief: 'A slow model call should end as a fizzle before the grain times out.',
        model: 'deepseek-v4.1-flash-free',
        budgetUsd: 2,
        status: 'Working',
        report: null,
        spentUsd: 0.2134,
        tokens: 182_000,
        files: 0,
        added: 0,
        removed: 0,
        paths: [],
        stop: null,
        error: null,
        proposition: null,
        checks: [],
        note: null,
        filedAt: 0,
        updatedAt: 0,
    },
    {
        id: 'c-proposed',
        architectId: 'p2',
        architectName: 'Justin de Castelnaudary Ruysch',
        kind: 'Change',
        title: 'Let rooms keep their light after dark',
        why: 'Every place goes grey at once, and nobody here lives like that.',
        brief: 'Lit places should stay lit while someone is in them.',
        model: 'glm-5.3-flash-free',
        budgetUsd: 3,
        status: 'Proposed',
        report: 'Rooms now stay lit while occupied.',
        spentUsd: 1.82,
        tokens: 910_000,
        files: 4,
        added: 61,
        removed: 12,
        paths: ['backend/World/Places/Sleep.cs'],
        stop: null,
        error: null,
        proposition: 3,
        checks: [
            { name: 'backend-build', passed: true, seconds: 20, detail: '' },
            {
                name: 'backend-tests',
                passed: false,
                seconds: 46,
                detail: 'Failed: 1, Passed: 1128',
            },
            {
                name: 'frontend-tests',
                passed: true,
                seconds: 25,
                detail: 'Tests 224 passed',
            },
        ],
        note: null,
        filedAt: 0,
        updatedAt: 0,
    },
];

export const useGetWorldsPartyIdWorks = fn(() =>
    queryResult({
        status: 200,
        data: {
            enabled: true,
            builderUp: true,
            builderBusy: true,
            reserveUsd: 41.2,
            models: [],
            commissions: worksCommissions,
        },
    }),
);

export const useGetWorldsPartyIdWorksCommissionsCommissionIdLines = fn(() =>
    queryResult({
        status: 200,
        data: [
            {
                n: 1,
                at: 1,
                kind: 'thought',
                text: 'CLAUDE.md first, then the Life grain.',
            },
            {
                n: 2,
                at: 2,
                kind: 'tool',
                text: 'read backend/World/PersonaLifeGrain.cs',
            },
            {
                n: 3,
                at: 3,
                kind: 'say',
                text: 'The turn budget is 150 seconds but recall runs before it.',
            },
            {
                n: 4,
                at: 4,
                kind: 'tool',
                text: 'bash dotnet test backend-test/backend-test.csproj',
            },
            {
                n: 5,
                at: 5,
                kind: 'failed',
                text: 'bash dotnet test backend-test/backend-test.csproj',
            },
            { n: 6, at: 6, kind: 'status', text: 'turn ended: end_turn' },
            { n: 7, at: 7, kind: 'check', text: 'backend-build pass 20' },
        ],
    }),
);

export const usePostWorldsPartyIdWorksCommissionsCommissionIdReject = fn(() =>
    noopMutationResult(),
);
