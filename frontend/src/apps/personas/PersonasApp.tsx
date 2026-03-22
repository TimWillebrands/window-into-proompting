import { useId, useMemo, useState } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import {
    getGetPersonaQueryKey,
    useDeletePersonaId,
    useGetPersonaSuspense,
    usePutPersonaId,
} from '../../api/party-zone';
import type { Persona } from '../../api/model';

export default function PersonasApp() {
    const personasQuery = useGetPersonaSuspense();
    const personas = useMemo(() => {
        const data = personasQuery.data?.data;
        return Array.isArray(data) ? data : [];
    }, [personasQuery.data]);
    const [selectedId, setSelectedId] = useState<string | null>(null);

    const selectedPersona = useMemo(
        () => personas.find((p) => p.id === selectedId) ?? null,
        [personas, selectedId],
    );

    return (
        <div
            className="app-surface flex h-full"
            style={{ background: '#ECE9D8' }}
        >
            {/* Sidebar */}
            <div
                className="flex flex-col"
                style={{ width: '200px', borderRight: '1px solid #ACA899' }}
            >
                <div className="xp-section-header flex items-center justify-between">
                    <span>Personas</span>
                    <span
                        style={{
                            background: '#fff',
                            color: '#003399',
                            borderRadius: '8px',
                            padding: '0 5px',
                            fontSize: '10px',
                            fontWeight: 700,
                        }}
                    >
                        {personas.length}
                    </span>
                </div>
                <div className="flex-1 overflow-y-auto">
                    {personasQuery.isLoading ? (
                        <p style={{ color: '#808080', padding: '8px' }}>
                            Loading...
                        </p>
                    ) : null}
                    {personasQuery.isError ? (
                        <p style={{ color: '#c00', padding: '8px' }}>
                            Failed to load.
                        </p>
                    ) : null}
                    {personas.map((persona) => (
                        <button
                            key={persona.id}
                            type="button"
                            className="w-full text-left flex items-center gap-2 p-2"
                            style={{
                                background:
                                    selectedId === persona.id
                                        ? '#316AC5'
                                        : 'transparent',
                                color:
                                    selectedId === persona.id ? '#fff' : '#000',
                                border: 'none',
                                borderBottom: '1px solid #D6D2C2',
                            }}
                            onClick={() => setSelectedId(persona.id ?? null)}
                        >
                            <img
                                src={`https://robohash.org/${persona.id}.png?size=24x24`}
                                alt=""
                                style={{
                                    width: 24,
                                    height: 24,
                                    borderRadius: '50%',
                                }}
                            />
                            <div style={{ minWidth: 0 }}>
                                <div
                                    style={{
                                        fontWeight: 600,
                                        overflow: 'hidden',
                                        textOverflow: 'ellipsis',
                                        whiteSpace: 'nowrap',
                                    }}
                                >
                                    {persona.name}
                                </div>
                            </div>
                        </button>
                    ))}
                </div>
                <div style={{ borderTop: '1px solid #ACA899', padding: '4px' }}>
                    <NewPersonaButton onCreated={(id) => setSelectedId(id)} />
                </div>
            </div>

            {/* Editor pane */}
            <div className="flex-1 overflow-y-auto p-3">
                {selectedPersona ? (
                    <PersonaEditor
                        key={selectedPersona.id}
                        persona={selectedPersona}
                        onDeleted={() => setSelectedId(null)}
                    />
                ) : (
                    <div
                        className="h-full flex flex-col items-center justify-center"
                        style={{ color: '#808080' }}
                    >
                        <p>Select a persona to edit, or create a new one.</p>
                    </div>
                )}
            </div>
        </div>
    );
}

function NewPersonaButton({ onCreated }: { onCreated: (id: string) => void }) {
    const queryClient = useQueryClient();
    const upsertMutation = usePutPersonaId({
        mutation: {
            onSuccess: async (response) => {
                await queryClient.invalidateQueries({
                    queryKey: getGetPersonaQueryKey(),
                });
                const created = response.data;
                if (created?.id) {
                    onCreated(created.id);
                }
            },
        },
    });

    return (
        <button
            type="button"
            className="w-full"
            disabled={upsertMutation.isPending}
            onClick={() => {
                const id = crypto.randomUUID();
                upsertMutation.mutate({
                    id,
                    data: {
                        id,
                        name: 'New Persona',
                        systemPrompt: 'You are a helpful persona.',
                    },
                });
            }}
        >
            {upsertMutation.isPending ? 'Creating...' : 'New Persona'}
        </button>
    );
}

function PersonaEditor({
    persona,
    onDeleted,
}: {
    persona: Persona;
    onDeleted: () => void;
}) {
    const queryClient = useQueryClient();
    const nameId = useId();
    const promptId = useId();
    const bioId = useId();

    const [name, setName] = useState(persona.name ?? '');
    const [systemPrompt, setSystemPrompt] = useState(
        persona.systemPrompt ?? '',
    );
    const [bio, setBio] = useState(persona.bio ?? '');

    const saveMutation = usePutPersonaId({
        mutation: {
            onSuccess: async () => {
                await queryClient.invalidateQueries({
                    queryKey: getGetPersonaQueryKey(),
                });
            },
        },
    });

    const deleteMutation = useDeletePersonaId({
        mutation: {
            onSuccess: async () => {
                await queryClient.invalidateQueries({
                    queryKey: getGetPersonaQueryKey(),
                });
                onDeleted();
            },
        },
    });

    return (
        <form
            className="space-y-3"
            onSubmit={(event) => {
                event.preventDefault();
                if (!persona.id) return;
                saveMutation.mutate({
                    id: persona.id,
                    data: {
                        id: persona.id,
                        name,
                        systemPrompt,
                        bio: bio || null,
                    },
                });
            }}
        >
            <fieldset className="xp-groupbox">
                <legend>Details</legend>
                <div className="space-y-2 p-1">
                    <div>
                        <label
                            htmlFor={nameId}
                            style={{ fontWeight: 600, color: '#000' }}
                        >
                            Persona Name
                        </label>
                        <input
                            id={nameId}
                            type="text"
                            value={name}
                            onChange={(event) =>
                                setName(event.currentTarget.value)
                            }
                            className="w-full"
                        />
                    </div>
                    <div>
                        <label
                            htmlFor={bioId}
                            style={{ fontWeight: 600, color: '#000' }}
                        >
                            Bio
                        </label>
                        <textarea
                            id={bioId}
                            rows={3}
                            value={bio}
                            onChange={(event) =>
                                setBio(event.currentTarget.value)
                            }
                            className="w-full"
                        />
                    </div>
                </div>
            </fieldset>

            <fieldset className="xp-groupbox">
                <legend>System Prompt</legend>
                <div className="p-1">
                    <textarea
                        id={promptId}
                        rows={10}
                        value={systemPrompt}
                        onChange={(event) =>
                            setSystemPrompt(event.currentTarget.value)
                        }
                        className="w-full"
                        style={{ fontFamily: 'monospace' }}
                    />
                </div>
            </fieldset>

            {saveMutation.isError ? (
                <p style={{ color: '#c00' }}>
                    {saveMutation.error instanceof Error
                        ? saveMutation.error.message
                        : 'Save failed.'}
                </p>
            ) : null}
            {saveMutation.isSuccess ? (
                <p style={{ color: '#006600' }}>Saved.</p>
            ) : null}

            <div className="flex justify-between">
                <button
                    type="button"
                    disabled={deleteMutation.isPending}
                    onClick={() => {
                        if (!persona.id) return;
                        deleteMutation.mutate({ id: persona.id });
                    }}
                >
                    {deleteMutation.isPending ? 'Deleting...' : 'Delete'}
                </button>
                <button type="submit" disabled={saveMutation.isPending}>
                    {saveMutation.isPending ? 'Saving...' : 'Save Changes'}
                </button>
            </div>
        </form>
    );
}
