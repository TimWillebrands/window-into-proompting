import { useMemo, useState } from 'react';
import {
    useGetLlmConfigPersonaRoutes,
    useGetLlmConfigProviders,
    useGetPersona,
} from '#api/party-zone';
import type { LlmProviderEntry } from '../../../api/model/llmProviderEntry';
import type { Persona } from '../../../api/model/persona';
import type { PersonaRoute } from '../../../api/model/personaRoute';
import { NARRATOR_PERSONA_ID } from '../../../lib/driver';
import { inputStyle } from './constants';
import PersonaModelCard from './PersonaModelCard';
import ProviderModal from './ProviderModal';

const headerButton: React.CSSProperties = {
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
};

function PersonaPicker({
    personas,
    onPick,
}: {
    personas: Persona[];
    onPick: (id: string) => void;
}) {
    const [query, setQuery] = useState('');
    const matches = personas.filter((p) =>
        p.name?.toLowerCase().includes(query.trim().toLowerCase()),
    );

    return (
        <div>
            <input
                type="search"
                aria-label="Find a persona"
                placeholder="Find a persona..."
                value={query}
                onChange={(e) => setQuery(e.target.value)}
                style={{ ...inputStyle, marginBottom: 8 }}
            />
            {matches.length === 0 ? (
                <p style={{ color: '#555', fontSize: 12, margin: '8px 2px' }}>
                    {personas.length === 0
                        ? 'Every persona already has its own models.'
                        : 'No persona has that name.'}
                </p>
            ) : (
                <div style={{ maxHeight: 280, overflowY: 'auto' }}>
                    {matches.map((p) => (
                        <button
                            key={p.id}
                            type="button"
                            className="xp-bare xp-glass-row"
                            onClick={() => onPick(p.id ?? '')}
                        >
                            <span style={{ fontWeight: 600 }}>{p.name}</span>
                        </button>
                    ))}
                </div>
            )}
        </div>
    );
}

export default function PersonaModelsSection({
    onOpenProviders,
}: {
    onOpenProviders: () => void;
}) {
    const routesQuery = useGetLlmConfigPersonaRoutes();
    const providersQuery = useGetLlmConfigProviders();
    const personasQuery = useGetPersona();
    const [picking, setPicking] = useState(false);
    // Stay on screen after edits, so clearing the last tier doesn't whisk the card away.
    const [kept, setKept] = useState<string[]>([]);

    const routes: PersonaRoute[] = routesQuery.data?.data ?? [];
    const providers: LlmProviderEntry[] = providersQuery.data?.data ?? [];
    const personas = useMemo(() => {
        const data = personasQuery.data?.data;
        return (Array.isArray(data) ? data : [])
            .filter((p) => p.id !== NARRATOR_PERSONA_ID)
            .sort((a, b) => (a.name ?? '').localeCompare(b.name ?? ''));
    }, [personasQuery.data]);

    const isShown = (p: Persona) =>
        kept.includes(p.id ?? '') || routes.some((r) => r.personaId === p.id);
    const shown = personas.filter(isShown);
    const assignedCount = shown.filter((p) =>
        routes.some((r) => r.personaId === p.id),
    ).length;

    const keep = (id: string) =>
        setKept((prev) => (prev.includes(id) ? prev : [...prev, id]));

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
                    Persona Models
                </h2>
                {providers.length > 0 && (
                    <button
                        type="button"
                        onClick={() => setPicking(true)}
                        style={headerButton}
                    >
                        + Assign Persona
                    </button>
                )}
            </div>
            <p
                style={{
                    color: 'rgba(255,255,255,0.8)',
                    fontSize: 12,
                    margin: '0 0 16px',
                }}
            >
                Every persona uses the shared providers. Pick a provider here to
                give one persona its own model for a kind of work.
                {assignedCount > 0 &&
                    ` ${assignedCount} ${assignedCount === 1 ? 'persona has' : 'personas have'} their own models.`}
            </p>

            {shown.length === 0 && (
                <div
                    style={{
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
                    <div style={{ fontSize: 26, marginBottom: 6 }}>🎭</div>
                    {providers.length === 0 ? (
                        <>
                            <p style={{ margin: '0 0 10px', fontWeight: 700 }}>
                                Add a provider first.
                            </p>
                            <button
                                type="button"
                                onClick={onOpenProviders}
                                style={headerButton}
                            >
                                Open Language Model Providers
                            </button>
                        </>
                    ) : (
                        <>
                            <p style={{ margin: '0 0 4px', fontWeight: 700 }}>
                                No persona has its own models yet.
                            </p>
                            <p
                                style={{
                                    margin: 0,
                                    color: 'rgba(255,255,255,0.65)',
                                    fontSize: 12,
                                }}
                            >
                                Assign a persona to send their voice, thoughts
                                or everyday work to a provider you choose.
                            </p>
                        </>
                    )}
                </div>
            )}

            <div style={{ display: 'flex', flexDirection: 'column', gap: 8 }}>
                {shown.map((p) => (
                    <PersonaModelCard
                        key={p.id}
                        persona={p}
                        routes={routes.filter((r) => r.personaId === p.id)}
                        providers={providers}
                        onChanged={() => {
                            keep(p.id ?? '');
                            routesQuery.refetch();
                        }}
                        onRemoved={() => {
                            setKept((prev) => prev.filter((k) => k !== p.id));
                            routesQuery.refetch();
                        }}
                    />
                ))}
            </div>

            {picking && (
                <ProviderModal
                    title="Assign Persona"
                    onClose={() => setPicking(false)}
                >
                    <PersonaPicker
                        personas={personas.filter((p) => !isShown(p))}
                        onPick={(id) => {
                            keep(id);
                            setPicking(false);
                        }}
                    />
                </ProviderModal>
            )}
        </div>
    );
}
