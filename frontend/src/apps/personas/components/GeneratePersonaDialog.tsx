import { useQueryClient } from '@tanstack/react-query';
import { useEffect, useId, useRef, useState } from 'react';
import { getGetPersonaQueryKey, usePutPersonaId } from '#api/party-zone';
import { personaWsStream } from '#lib/persona-ws-stream';

type PersonaGenerationDeltaData = { data: string };
type PersonaGenerationCompletedData = {
    name?: string;
    systemPrompt?: string;
    bio?: string;
};
type PersonaGenerationErrorData = { message: string };

export default function GeneratePersonaDialog({
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

    useEffect(() => () => abortRef.current?.abort(), []);

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
