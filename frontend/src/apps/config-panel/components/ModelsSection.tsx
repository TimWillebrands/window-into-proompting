import { useMemo, useState } from 'react';
import {
    useGetLlmConfigAssignments,
    useGetLlmConfigPersonaRoutes,
    useGetLlmConfigProviders,
    useGetLlmConfigProvidersAvailability,
    useGetPersona,
} from '#api/party-zone';
import type { LlmProviderEntry } from '../../../api/model/llmProviderEntry';
import { NARRATOR_PERSONA_ID } from '../../../lib/driver';
import { EMPTY_ENTRY } from './constants';
import ModelSplit from './ModelSplit';
import PersonaMatrix from './PersonaMatrix';
import ProviderCard from './ProviderCard';
import ProviderEditor from './ProviderEditor';
import ProviderModal from './ProviderModal';

const heading: React.CSSProperties = {
    color: '#fff',
    fontSize: 13,
    fontWeight: 700,
    margin: '18px 0 8px',
    textShadow: '0 1px 2px rgba(0,0,0,0.3)',
};

/** One screen for models: the providers, how personas are split over them, and who is pinned. */
export default function ModelsSection() {
    const providersQuery = useGetLlmConfigProviders();
    const routesQuery = useGetLlmConfigPersonaRoutes();
    // The router pings every 30s; polling a bit faster shows a box coming back soon after.
    const availabilityQuery = useGetLlmConfigProvidersAvailability({
        query: { refetchInterval: 15_000 },
    });
    const assignmentsQuery = useGetLlmConfigAssignments({
        query: { refetchInterval: 15_000 },
    });
    const personasQuery = useGetPersona();

    const providers: LlmProviderEntry[] = providersQuery.data?.data ?? [];
    const routes = routesQuery.data?.data ?? [];
    const availability = availabilityQuery.data?.data ?? [];
    const assignments = assignmentsQuery.data?.data ?? [];
    const personas = useMemo(() => {
        const data = personasQuery.data?.data;
        return (Array.isArray(data) ? data : [])
            .filter((p) => p.id !== NARRATOR_PERSONA_ID)
            .sort((a, b) => (a.name ?? '').localeCompare(b.name ?? ''));
    }, [personasQuery.data]);

    const [editing, setEditing] = useState<LlmProviderEntry | null>(null);
    const isNew = !editing?.id;

    const refresh = () => {
        providersQuery.refetch();
        routesQuery.refetch();
        availabilityQuery.refetch();
        assignmentsQuery.refetch();
    };
    const close = () => {
        setEditing(null);
        refresh();
    };
    const pinnedTo = (providerId?: string) =>
        new Set(
            routes
                .filter((r) => r.providerId === providerId)
                .map((r) => r.personaId),
        ).size;

    return (
        <div>
            <div
                style={{
                    display: 'flex',
                    alignItems: 'center',
                    justifyContent: 'space-between',
                    marginBottom: 4,
                }}
            >
                <h2
                    style={{
                        color: '#fff',
                        fontSize: 22,
                        fontWeight: 300,
                        margin: 0,
                        textShadow: '0 1px 2px rgba(0,0,0,0.3)',
                    }}
                >
                    Language Models
                </h2>
                <button
                    type="button"
                    onClick={() => setEditing({ ...EMPTY_ENTRY })}
                    style={{
                        padding: '4px 14px',
                        fontSize: 12,
                        fontWeight: 600,
                        background: 'rgba(255,255,255,0.18)',
                        border: '1px solid rgba(255,255,255,0.5)',
                        borderRadius: 999,
                        boxShadow:
                            'inset 0 1px 0 rgba(255,255,255,0.4), 0 4px 10px -4px rgba(0,0,0,0.3)',
                        color: '#fff',
                        cursor: 'pointer',
                        backdropFilter: 'blur(8px)',
                    }}
                >
                    + Add Provider
                </button>
            </div>

            {providers.length === 0 ? (
                <div
                    style={{
                        marginTop: 12,
                        background: 'rgba(255,255,255,0.1)',
                        border: '1px solid rgba(255,255,255,0.25)',
                        borderRadius: 16,
                        boxShadow: 'inset 0 1px 0 rgba(255,255,255,0.2)',
                        backdropFilter: 'blur(8px)',
                        padding: '24px 20px',
                        color: 'rgba(255,255,255,0.85)',
                        fontSize: 13,
                        textAlign: 'center',
                    }}
                >
                    <div style={{ fontSize: 26, marginBottom: 6 }}>🤖</div>
                    <p style={{ margin: '0 0 4px', fontWeight: 700 }}>
                        Personas need a language model to speak.
                    </p>
                    <p
                        style={{
                            margin: 0,
                            color: 'rgba(255,255,255,0.65)',
                            fontSize: 12,
                        }}
                    >
                        Add a provider (a local server, OpenRouter or Claude)
                        and chats can start generating.
                    </p>
                </div>
            ) : (
                <>
                    <h3 style={{ ...heading, marginTop: 10 }}>Providers</h3>
                    <div
                        style={{
                            display: 'flex',
                            flexDirection: 'column',
                            gap: 8,
                        }}
                    >
                        {providers.map((p) => (
                            <ProviderCard
                                key={p.id}
                                provider={p}
                                providers={providers}
                                availability={availability.find(
                                    (a) => a.providerId === p.id,
                                )}
                                personaCount={pinnedTo(p.id)}
                                onEdit={() => setEditing({ ...p })}
                                onDeleted={refresh}
                                onToggled={refresh}
                            />
                        ))}
                    </div>

                    <h3 style={heading}>Who speaks through which</h3>
                    <div
                        style={{
                            display: 'flex',
                            flexDirection: 'column',
                            gap: 10,
                        }}
                    >
                        <ModelSplit
                            providers={providers}
                            availability={availability}
                            assignments={assignments}
                            onSaved={refresh}
                        />
                        <PersonaMatrix
                            personas={personas}
                            providers={providers}
                            routes={routes}
                            assignments={assignments}
                            onChanged={refresh}
                        />
                    </div>
                </>
            )}

            {editing && (
                <ProviderModal
                    title={isNew ? 'Add Provider' : 'Edit Provider'}
                    onClose={close}
                >
                    <ProviderEditor
                        entry={editing}
                        isNew={isNew}
                        onClose={close}
                    />
                </ProviderModal>
            )}
        </div>
    );
}
