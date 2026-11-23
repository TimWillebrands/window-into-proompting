import { GroupBox, Icon, Text, Toolbar, ToolbarButton } from '@partytown/ui';
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

export default function ModelsSection() {
    const providersQuery = useGetLlmConfigProviders();
    const routesQuery = useGetLlmConfigPersonaRoutes();
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
        <div className="flex flex-col">
            <Toolbar aria-label="Language Models">
                <ToolbarButton
                    icon={<Icon name="add" />}
                    onClick={() => setEditing({ ...EMPTY_ENTRY })}
                >
                    Add Provider
                </ToolbarButton>
            </Toolbar>

            <div className="flex flex-col gap-3 p-3">
                {providers.length === 0 ? (
                    <GroupBox title="Providers">
                        <div className="flex items-center gap-3">
                            <Icon name="models" size={32} />
                            <div className="flex flex-col gap-1">
                                <strong>
                                    Personas need a language model to speak.
                                </strong>
                                <Text as="p" tone="muted">
                                    Add a provider (a local server, OpenRouter
                                    or Claude) and chats can start generating.
                                </Text>
                            </div>
                        </div>
                    </GroupBox>
                ) : (
                    <>
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
                    </>
                )}
            </div>

            {editing && (
                <ProviderEditor entry={editing} isNew={isNew} onClose={close} />
            )}
        </div>
    );
}
