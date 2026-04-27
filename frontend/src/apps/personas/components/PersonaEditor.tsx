import { useQueryClient } from '@tanstack/react-query';
import { useEffect, useId, useRef, useState } from 'react';
import {
    getGetPersonaQueryKey,
    useDeletePersonaId,
    usePutPersonaId,
} from '#api/party-zone';
import { personaWsStream } from '#lib/persona-ws-stream';
import type { Persona } from '../../../api/model';

type PersonaGenerationDeltaData = { data: string };
type PersonaGenerationCompletedData = {
    name?: string;
    systemPrompt?: string;
    bio?: string;
};
type PersonaGenerationErrorData = { message: string };

export default function PersonaEditor({
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
