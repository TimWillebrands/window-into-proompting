import { useQueryClient } from '@tanstack/react-query';
import { useEffect, useId, useRef, useState } from 'react';
import {
    getGetPersonaQueryKey,
    getGetWorldsPartyIdLivesQueryKey,
    useDeletePersonaId,
    usePutPersonaId,
} from '#api/party-zone';
import { personaWsStream } from '#lib/persona-ws-stream';
import type { Persona } from '../../../api/model';
import { ROOT_PARTY_ID } from '../../../lib/chat-api';

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
    const [chattiness, setChattiness] = useState(
        Number(persona.chattiness ?? 0.5),
    );
    const [impulsivity, setImpulsivity] = useState(
        Number(persona.impulsivity ?? 0.5),
    );
    const [isGeneratingBio, setIsGeneratingBio] = useState(false);
    const [bioGenError, setBioGenError] = useState('');
    const abortRef = useRef<AbortController | null>(null);

    useEffect(() => () => abortRef.current?.abort(), []);

    const saveMutation = usePutPersonaId({
        mutation: {
            onSuccess: async () => {
                await Promise.all([
                    queryClient.invalidateQueries({
                        queryKey: getGetPersonaQueryKey(),
                    }),
                    // Lives show chattiness.
                    queryClient.invalidateQueries({
                        queryKey:
                            getGetWorldsPartyIdLivesQueryKey(ROOT_PARTY_ID),
                    }),
                ]);
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
            // Off-step legacy dial values must not block saving.
            noValidate
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
                        chattiness: Math.round(chattiness * 100) / 100,
                        impulsivity: Math.round(impulsivity * 100) / 100,
                    },
                });
            }}
        >
            <div className="flex items-center gap-2">
                <img
                    src={`https://robohash.org/${persona.id}.png?size=32x32`}
                    alt=""
                    style={{
                        width: 32,
                        height: 32,
                        borderRadius: '50%',
                        background: '#fff',
                        boxShadow:
                            '0 0 0 1px rgba(255,255,255,0.9), 0 1px 3px rgba(31,55,148,0.25)',
                    }}
                />
                <div>
                    <div style={{ fontWeight: 700, fontSize: 13 }}>
                        {name || 'Unnamed persona'}
                    </div>
                    <div style={{ color: '#666', fontSize: 10 }}>
                        Changes apply after you click Save Changes.
                    </div>
                </div>
            </div>

            <fieldset className="border border-[#9db2c8] p-2 m-0">
                <legend className="text-xp-legend font-semibold text-[11px] px-1">
                    Details
                </legend>
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
                                title={
                                    systemPrompt.trim()
                                        ? 'Write a short bio from the system prompt below'
                                        : 'Write a system prompt first — the bio is generated from it'
                                }
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

            <fieldset className="border border-[#9db2c8] p-2 m-0">
                <legend className="text-xp-legend font-semibold text-[11px] px-1">
                    Personality Dials
                </legend>
                <div className="space-y-2 p-1">
                    <DialSlider
                        label="Chattiness"
                        help="How much they want to talk / act."
                        value={chattiness}
                        onChange={setChattiness}
                    />
                    <DialSlider
                        label="Impulsivity"
                        help="How hard they commit once they start speaking."
                        value={impulsivity}
                        onChange={setImpulsivity}
                    />
                </div>
            </fieldset>

            <fieldset className="border border-[#9db2c8] p-2 m-0">
                <legend className="text-xp-legend font-semibold text-[11px] px-1">
                    System Prompt
                </legend>
                <div className="p-1">
                    <p style={{ color: '#666', margin: '0 0 3px' }}>
                        The instructions this persona follows in every chat —
                        who it is, how it talks, what it cares about.
                    </p>
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
                        if (
                            !confirm(
                                `Delete "${persona.name ?? 'this persona'}"? This cannot be undone.`,
                            )
                        )
                            return;
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

function DialSlider({
    label,
    help,
    value,
    onChange,
}: {
    label: string;
    help: string;
    value: number;
    onChange: (value: number) => void;
}) {
    const id = useId();
    return (
        <div>
            <div className="flex items-center justify-between">
                <label htmlFor={id} style={{ fontWeight: 600, color: '#000' }}>
                    {label}
                </label>
                <span style={{ fontVariantNumeric: 'tabular-nums' }}>
                    {value.toFixed(2)}
                </span>
            </div>
            <div className="flex items-center" style={{ gap: 6 }}>
                <span style={{ color: '#666', fontSize: 10 }}>Low</span>
                <input
                    id={id}
                    type="range"
                    // Integer percent steps: float steps trip step-mismatch validation.
                    min={0}
                    max={100}
                    step={5}
                    value={Math.round(value * 100)}
                    onChange={(event) =>
                        onChange(Number(event.currentTarget.value) / 100)
                    }
                    className="flex-1"
                    style={{
                        border: 'none',
                        background: 'transparent',
                        padding: 0,
                    }}
                />
                <span style={{ color: '#666', fontSize: 10 }}>High</span>
            </div>
            <p style={{ color: '#666', margin: '1px 0 0' }}>{help}</p>
        </div>
    );
}
