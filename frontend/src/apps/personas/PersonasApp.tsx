import { useQueryClient } from '@tanstack/react-query';
import { useEffect, useId, useMemo, useRef, useState } from 'react';
import type { DefaultPersonaTemplate, Persona } from '../../api/model';
import {
    getGetPersonaQueryKey,
    useDeletePersonaId,
    useGetPersonaDefaultsSuspense,
    useGetPersonaSuspense,
    usePutPersonaId,
} from '../../api/party-zone';
import { personaWsStream } from '../../lib/persona-ws-stream';

type PersonaGenerationDeltaData = { data: string };
type PersonaGenerationCompletedData = {
    name?: string;
    systemPrompt?: string;
    bio?: string;
};
type PersonaGenerationErrorData = { message: string };

export default function PersonasApp() {
    const personasQuery = useGetPersonaSuspense();
    const personas = useMemo(() => {
        const data = personasQuery.data?.data;
        return Array.isArray(data) ? data : [];
    }, [personasQuery.data]);
    const [selectedId, setSelectedId] = useState<string | null>(null);
    const [showGenerateDialog, setShowGenerateDialog] = useState(false);

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
                <div
                    style={{
                        borderTop: '1px solid #ACA899',
                        padding: '4px',
                        display: 'flex',
                        flexDirection: 'column',
                        gap: '3px',
                    }}
                >
                    <NewPersonaButton onCreated={(id) => setSelectedId(id)} />
                    <TemplateButton onCreated={(id) => setSelectedId(id)} />
                    <button
                        type="button"
                        className="w-full"
                        onClick={() => setShowGenerateDialog(true)}
                    >
                        Generate Persona...
                    </button>
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

            {showGenerateDialog && (
                <GeneratePersonaDialog
                    onClose={() => setShowGenerateDialog(false)}
                    onCreated={(id) => {
                        setSelectedId(id);
                        setShowGenerateDialog(false);
                    }}
                />
            )}
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

function TemplateDropdownContent({
    onSelect,
    onClose,
}: {
    onSelect: (template: DefaultPersonaTemplate) => void;
    onClose: () => void;
}) {
    const defaultsQuery = useGetPersonaDefaultsSuspense();
    const templates: DefaultPersonaTemplate[] = useMemo(() => {
        const data = defaultsQuery.data?.data;
        return Array.isArray(data) ? data : [];
    }, [defaultsQuery.data]);

    return (
        <>
            <button
                type="button"
                aria-label="Close"
                style={{
                    position: 'fixed',
                    inset: 0,
                    zIndex: 99,
                    background: 'transparent',
                    border: 'none',
                    cursor: 'default',
                }}
                onClick={onClose}
            />
            <div
                style={{
                    position: 'absolute',
                    bottom: '100%',
                    left: 0,
                    right: 0,
                    background: '#fff',
                    border: '1px solid #ACA899',
                    boxShadow: '2px 2px 4px rgba(0,0,0,0.2)',
                    zIndex: 100,
                    marginBottom: '2px',
                }}
            >
                {templates.map((t) => (
                    <button
                        key={t.name}
                        type="button"
                        className="w-full text-left"
                        style={{
                            padding: '6px 8px',
                            border: 'none',
                            background: 'transparent',
                            cursor: 'pointer',
                            borderBottom: '1px solid #E8E4DC',
                            display: 'flex',
                            alignItems: 'center',
                            gap: '6px',
                        }}
                        onMouseEnter={(e) => {
                            e.currentTarget.style.background = '#316AC5';
                            e.currentTarget.style.color = '#fff';
                        }}
                        onMouseLeave={(e) => {
                            e.currentTarget.style.background = 'transparent';
                            e.currentTarget.style.color = '#000';
                        }}
                        onClick={() => onSelect(t)}
                    >
                        <img
                            src={`https://robohash.org/${encodeURIComponent(t.name ?? '')}.png?size=20x20`}
                            alt=""
                            style={{
                                width: 20,
                                height: 20,
                                borderRadius: '50%',
                                flexShrink: 0,
                            }}
                        />
                        <span style={{ fontWeight: 600 }}>{t.name}</span>
                    </button>
                ))}
            </div>
        </>
    );
}

function TemplateButton({ onCreated }: { onCreated: (id: string) => void }) {
    const [open, setOpen] = useState(false);
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
                setOpen(false);
            },
        },
    });

    const handleSelect = (template: DefaultPersonaTemplate) => {
        const id = crypto.randomUUID();
        upsertMutation.mutate({
            id,
            data: {
                id,
                name: template.name ?? 'Template Persona',
                systemPrompt: template.systemPrompt ?? '',
                bio: template.bio ?? null,
            },
        });
    };

    return (
        <div style={{ position: 'relative' }}>
            <button
                type="button"
                className="w-full"
                disabled={upsertMutation.isPending}
                onClick={() => setOpen((v) => !v)}
            >
                {upsertMutation.isPending ? 'Creating...' : 'From Template...'}
            </button>
            {open && (
                <TemplateDropdownContent
                    onSelect={handleSelect}
                    onClose={() => setOpen(false)}
                />
            )}
        </div>
    );
}

function GeneratePersonaDialog({
    onClose,
    onCreated,
}: {
    onClose: () => void;
    onCreated: (id: string) => void;
}) {
    const [prompt, setPrompt] = useState('');
    const [preview, setPreview] = useState('');
    const [isStreaming, setIsStreaming] = useState(false);
    const [isSaving, setIsSaving] = useState(false);
    const [error, setError] = useState('');
    const inputId = useId();
    const previewRef = useRef<HTMLTextAreaElement>(null);
    const abortRef = useRef<AbortController | null>(null);
    const queryClient = useQueryClient();

    // Abort on unmount
    useEffect(() => () => abortRef.current?.abort(), []);

    // Auto-scroll preview as content streams in
    // biome-ignore lint/correctness/useExhaustiveDependencies: preview change is the trigger
    useEffect(() => {
        if (previewRef.current) {
            previewRef.current.scrollTop = previewRef.current.scrollHeight;
        }
    }, [preview]);

    const upsertMutation = usePutPersonaId({
        mutation: {
            onSuccess: async (response) => {
                await queryClient.invalidateQueries({
                    queryKey: getGetPersonaQueryKey(),
                });
                const created = response.data;
                if (created?.id) onCreated(created.id);
            },
            onError: () => {
                setError('Failed to save persona.');
                setIsSaving(false);
            },
        },
    });

    const handleGenerate = async () => {
        if (!prompt.trim() || isStreaming) return;
        abortRef.current?.abort();
        const abort = new AbortController();
        abortRef.current = abort;

        setError('');
        setPreview('');
        setIsStreaming(true);

        try {
            let accumulated = '';
            for await (const envelope of personaWsStream(
                { type: 'generate', prompt },
                abort.signal,
            )) {
                if (envelope.type === 'persona.generation.delta') {
                    const delta = envelope.data as PersonaGenerationDeltaData;
                    accumulated += delta.data;
                    setPreview(accumulated);
                } else if (envelope.type === 'persona.generation.completed') {
                    const completed =
                        envelope.data as PersonaGenerationCompletedData;
                    setIsStreaming(false);
                    setIsSaving(true);
                    const id = crypto.randomUUID();
                    upsertMutation.mutate({
                        id,
                        data: {
                            id,
                            name: completed.name ?? 'Generated Persona',
                            systemPrompt: completed.systemPrompt ?? '',
                            bio: completed.bio ?? null,
                        },
                    });
                } else if (envelope.type === 'persona.generation.error') {
                    const err = envelope.data as PersonaGenerationErrorData;
                    setError(err.message);
                }
            }
        } catch (e) {
            if (!(e instanceof DOMException && e.name === 'AbortError')) {
                setError('Generation failed. Please try again.');
            }
        } finally {
            setIsStreaming(false);
        }
    };

    const handleCancel = () => {
        abortRef.current?.abort();
        onClose();
    };

    const isPending = isStreaming || isSaving;

    return (
        <div
            style={{
                position: 'absolute',
                inset: 0,
                background: 'rgba(0,0,0,0.4)',
                display: 'flex',
                alignItems: 'center',
                justifyContent: 'center',
                zIndex: 200,
            }}
        >
            <div
                className="xp-groupbox"
                style={{
                    background: '#ECE9D8',
                    padding: '16px',
                    width: '400px',
                    boxShadow: '4px 4px 8px rgba(0,0,0,0.3)',
                    display: 'flex',
                    flexDirection: 'column',
                    gap: '8px',
                }}
            >
                <div className="xp-section-header">Generate Persona</div>
                <div>
                    <label
                        htmlFor={inputId}
                        style={{
                            display: 'block',
                            fontWeight: 600,
                            marginBottom: '4px',
                        }}
                    >
                        Describe your persona
                    </label>
                    <input
                        id={inputId}
                        type="text"
                        value={prompt}
                        onChange={(e) => setPrompt(e.currentTarget.value)}
                        placeholder="e.g. female punk rock engineer with a twist"
                        className="w-full"
                        disabled={isPending}
                        onKeyDown={(e) => {
                            if (e.key === 'Enter') handleGenerate();
                        }}
                    />
                </div>

                {(isStreaming || preview) && (
                    <div>
                        <div
                            style={{
                                fontWeight: 600,
                                fontSize: '11px',
                                marginBottom: '3px',
                                color: isStreaming ? '#316AC5' : '#000',
                            }}
                        >
                            {isStreaming ? 'Generating...' : 'Generated'}
                        </div>
                        <textarea
                            ref={previewRef}
                            readOnly
                            value={preview}
                            rows={10}
                            className="w-full"
                            style={{
                                fontFamily: 'monospace',
                                fontSize: '10px',
                                resize: 'none',
                                background: '#fff',
                            }}
                        />
                    </div>
                )}

                {isSaving && (
                    <p style={{ color: '#808080', fontSize: '11px' }}>
                        Saving persona...
                    </p>
                )}
                {error && (
                    <p style={{ color: '#c00', fontSize: '11px' }}>{error}</p>
                )}

                <div className="flex justify-between" style={{ gap: '6px' }}>
                    <button
                        type="button"
                        onClick={handleCancel}
                        disabled={isSaving}
                    >
                        Cancel
                    </button>
                    <button
                        type="button"
                        onClick={handleGenerate}
                        disabled={isPending || !prompt.trim()}
                    >
                        {isStreaming
                            ? 'Generating...'
                            : isSaving
                              ? 'Saving...'
                              : 'Generate'}
                    </button>
                </div>
            </div>
        </div>
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
    const [isGeneratingBio, setIsGeneratingBio] = useState(false);
    const [bioGenError, setBioGenError] = useState('');
    const abortRef = useRef<AbortController | null>(null);

    // Abort any in-flight bio generation on unmount
    useEffect(() => () => abortRef.current?.abort(), []);

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

    const handleGenerateBio = async () => {
        abortRef.current?.abort();
        const abort = new AbortController();
        abortRef.current = abort;

        setBioGenError('');
        setIsGeneratingBio(true);

        try {
            let accumulated = '';
            for await (const envelope of personaWsStream(
                { type: 'generate-bio', systemPrompt },
                abort.signal,
            )) {
                if (envelope.type === 'persona.generation.delta') {
                    const delta = envelope.data as PersonaGenerationDeltaData;
                    accumulated += delta.data;
                    setBio(accumulated);
                } else if (envelope.type === 'persona.generation.completed') {
                    const completed =
                        envelope.data as PersonaGenerationCompletedData;
                    setBio(completed.bio ?? accumulated);
                } else if (envelope.type === 'persona.generation.error') {
                    const err = envelope.data as PersonaGenerationErrorData;
                    setBioGenError(err.message);
                }
            }
        } catch (e) {
            if (!(e instanceof DOMException && e.name === 'AbortError')) {
                setBioGenError('Bio generation failed.');
            }
        } finally {
            setIsGeneratingBio(false);
        }
    };

    return (
        <form
            className="space-y-3"
            onSubmit={(event) => {
                event.preventDefault();
                if (!persona.id || isGeneratingBio) return;
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
                        <div
                            className="flex items-center justify-between"
                            style={{ marginBottom: '2px' }}
                        >
                            <label
                                htmlFor={bioId}
                                style={{ fontWeight: 600, color: '#000' }}
                            >
                                Bio
                            </label>
                            <button
                                type="button"
                                style={{ fontSize: '11px', padding: '1px 6px' }}
                                disabled={
                                    isGeneratingBio || !systemPrompt.trim()
                                }
                                onClick={handleGenerateBio}
                            >
                                {isGeneratingBio
                                    ? 'Generating...'
                                    : 'Generate Bio'}
                            </button>
                        </div>
                        {bioGenError && (
                            <p style={{ color: '#c00', fontSize: '11px' }}>
                                {bioGenError}
                            </p>
                        )}
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
