import {
    CommandButton,
    FieldRow,
    GroupBox,
    Slider,
    Stack,
    StatusText,
    Text,
    TextArea,
    TextBox,
} from '@partytown/ui';
import { useQueryClient } from '@tanstack/react-query';
import { useEffect, useRef, useState } from 'react';
import {
    getGetPersonaQueryKey,
    getGetWorldsPartyIdLivesQueryKey,
    useDeletePersonaId,
    usePutPersonaId,
} from '#api/party-zone';
import { personaWsStream } from '#lib/persona-ws-stream';
import type { Persona } from '../../../api/model';
import Portrait from '../../../components/Portrait';
import { AURA_MAX, WORLD_ID } from '../../../lib/world';
import Residence from './Residence';

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
    onSaved,
}: {
    persona: Persona;
    onDeleted: () => void;
    onSaved?: () => void;
}) {
    const queryClient = useQueryClient();

    const [name, setName] = useState(persona.name ?? '');
    const [systemPrompt, setSystemPrompt] = useState(
        persona.systemPrompt ?? '',
    );
    const [bio, setBio] = useState(persona.bio ?? '');
    const [aura, setAura] = useState(persona.aura ?? '');
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
                    queryClient.invalidateQueries({
                        queryKey: getGetWorldsPartyIdLivesQueryKey(WORLD_ID),
                    }),
                ]);
                onSaved?.();
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
        <Stack
            as="form"
            noValidate
            gap="lg"
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
                        aura: aura.trim() || null,
                        chattiness: Math.round(chattiness * 100) / 100,
                        impulsivity: Math.round(impulsivity * 100) / 100,
                    },
                });
            }}
        >
            <Stack direction="row" align="center" gap="md">
                <Portrait who={persona.id} alt="" width={32} height={32} />
                <Stack>
                    <strong>{name || 'Unnamed persona'}</strong>
                    <Text as="p" tone="muted">
                        Changes apply after you click Save Changes.
                    </Text>
                </Stack>
            </Stack>

            <GroupBox title="Details">
                <FieldRow label="Persona Name">
                    <TextBox
                        value={name}
                        onChange={(event) => setName(event.currentTarget.value)}
                    />
                </FieldRow>
                <FieldRow label="Aura">
                    <TextBox
                        maxLength={AURA_MAX}
                        value={aura}
                        placeholder="How they come across up close (optional)"
                        onChange={(event) => setAura(event.currentTarget.value)}
                    />
                </FieldRow>
                <FieldRow label="Bio" error={bioGenError || undefined}>
                    <TextArea
                        rows={3}
                        value={bio}
                        onChange={(event) => setBio(event.currentTarget.value)}
                    />
                    <CommandButton
                        size="small"
                        title={
                            systemPrompt.trim()
                                ? 'Write a short bio from the system prompt below'
                                : 'Write a system prompt first — the bio is generated from it'
                        }
                        disabled={isGeneratingBio || !systemPrompt.trim()}
                        onClick={handleGenerateBio}
                    >
                        {isGeneratingBio ? 'Generating...' : 'Generate Bio'}
                    </CommandButton>
                </FieldRow>
            </GroupBox>

            <GroupBox title="Personality Dials">
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
            </GroupBox>

            <GroupBox title="System Prompt">
                <Text as="p" tone="muted">
                    The instructions this persona follows in every chat — who it
                    is, how it talks, what it cares about.
                </Text>
                <TextArea
                    mono
                    aria-label="System Prompt"
                    rows={10}
                    value={systemPrompt}
                    onChange={(event) =>
                        setSystemPrompt(event.currentTarget.value)
                    }
                />
            </GroupBox>

            <Residence
                persona={persona}
                unsaved={
                    name !== (persona.name ?? '') || bio !== (persona.bio ?? '')
                }
            />

            {saveMutation.isError ? (
                <StatusText tone="danger">
                    {saveMutation.error instanceof Error
                        ? saveMutation.error.message
                        : 'Save failed.'}
                </StatusText>
            ) : null}
            {saveMutation.isSuccess ? (
                <StatusText tone="go">Saved.</StatusText>
            ) : null}

            <Stack direction="row" justify="between" gap="md">
                <CommandButton
                    tone="danger"
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
                </CommandButton>
                <CommandButton isDefault disabled={saveMutation.isPending}>
                    {saveMutation.isPending ? 'Saving...' : 'Save Changes'}
                </CommandButton>
            </Stack>
        </Stack>
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
    return (
        <FieldRow label={label} hint={help}>
            <Stack
                as="span"
                direction="row"
                align="center"
                gap="md"
                className="w-full"
            >
                <Slider
                    min={0}
                    max={100}
                    step={5}
                    startLabel="Low"
                    endLabel="High"
                    value={Math.round(value * 100)}
                    onChange={(event) =>
                        onChange(Number(event.currentTarget.value) / 100)
                    }
                    className="flex-1"
                />
                <span>{value.toFixed(2)}</span>
            </Stack>
        </FieldRow>
    );
}
