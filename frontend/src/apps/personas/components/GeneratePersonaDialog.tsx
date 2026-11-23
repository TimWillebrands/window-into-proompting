import {
    CommandButton,
    Dialog,
    FieldRow,
    Icon,
    StatusText,
    TextArea,
    TextBox,
} from '@partytown/ui';
import { useQueryClient } from '@tanstack/react-query';
import { useEffect, useRef, useState } from 'react';
import { getGetPersonaQueryKey, usePutPersonaId } from '#api/party-zone';
import { personaWsStream } from '#lib/persona-ws-stream';

type PersonaGenerationDeltaData = { data: string };
type PersonaGenerationCompletedData = {
    name?: string;
    systemPrompt?: string;
    bio?: string;
    chattiness?: number;
    impulsivity?: number;
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
                            chattiness: completed.chattiness,
                            impulsivity: completed.impulsivity,
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
        <Dialog
            contained
            title="Generate Persona"
            icon={<Icon name="sparkle" />}
            onClose={handleCancel}
            className="w-[400px]"
            buttons={
                <>
                    <CommandButton
                        isDefault
                        type="button"
                        onClick={handleGenerate}
                        disabled={isPending || !prompt.trim()}
                    >
                        {isStreaming
                            ? 'Generating...'
                            : isSaving
                              ? 'Saving...'
                              : 'Generate'}
                    </CommandButton>
                    <CommandButton onClick={handleCancel} disabled={isSaving}>
                        Cancel
                    </CommandButton>
                </>
            }
        >
            <div className="flex flex-col gap-2">
                <FieldRow label="Describe your persona" layout="stacked">
                    <TextBox
                        value={prompt}
                        onChange={(e) => setPrompt(e.currentTarget.value)}
                        placeholder="e.g. female punk rock engineer with a twist"
                        disabled={isPending}
                        onKeyDown={(e) => {
                            if (e.key === 'Enter') handleGenerate();
                        }}
                    />
                </FieldRow>

                {(isStreaming || preview) && (
                    <FieldRow
                        label={isStreaming ? 'Generating...' : 'Generated'}
                        layout="stacked"
                    >
                        <TextArea
                            ref={previewRef}
                            mono
                            readOnly
                            value={preview}
                            rows={10}
                        />
                    </FieldRow>
                )}

                {isSaving && (
                    <StatusText icon={<Icon name="hourglass" />}>
                        Saving persona...
                    </StatusText>
                )}
                {error && <StatusText tone="danger">{error}</StatusText>}
            </div>
        </Dialog>
    );
}
