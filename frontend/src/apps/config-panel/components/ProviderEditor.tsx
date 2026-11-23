import {
    CheckBox,
    ComboBox,
    CommandButton,
    FieldRow,
    RadioButton,
    RadioGroup,
    TextBox,
} from '@partytown/ui';
import { useId, useState } from 'react';
import {
    useGetLlmConfigProvidersIdModels,
    useGetLlmConfigTailnets,
    usePostLlmConfigProviders,
    usePutLlmConfigProvidersId,
} from '#api/party-zone';
import type { LlmProviderEntry } from '../../../api/model/llmProviderEntry';
import {
    DECISIONS,
    JOB_COMPLEXITIES,
    PROVIDER_TYPES,
    providerType,
    REASONING_EFFORTS,
} from './constants';
import ProviderModal from './ProviderModal';
import { DEFAULT_SHARE } from './split';

const CLAUDE_EFFORTS = REASONING_EFFORTS.filter((r) =>
    [0, 2, 3, 4].includes(r.value),
);

export default function ProviderEditor({
    entry,
    isNew,
    onClose,
}: {
    entry: LlmProviderEntry;
    isNew: boolean;
    onClose: () => void;
}) {
    const [form, setForm] = useState<LlmProviderEntry>(entry);
    const usedById = useId();
    const handlesId = useId();

    const addMutation = usePostLlmConfigProviders({
        mutation: { onSuccess: onClose },
    });
    const updateMutation = usePutLlmConfigProvidersId({
        mutation: { onSuccess: onClose },
    });
    const models = useGetLlmConfigProvidersIdModels(entry.id ?? '', {
        query: { enabled: !!entry.id },
    });

    const configuredTailnets = useGetLlmConfigTailnets().data?.data ?? [];
    const tailnets = [
        ...new Set([
            ...configuredTailnets,
            ...(form.tailnet ? [form.tailnet] : []),
        ]),
    ];

    const set = (patch: Partial<LlmProviderEntry>) =>
        setForm((prev) => ({ ...prev, ...patch }));

    const kind = providerType(form.type);
    const isClaude = form.type === 'anthropic';

    const handleTypeChange = (type: string) => {
        const next = providerType(type);
        set({
            type,
            baseUrl: next.baseUrl,
            apiKey: next.keyHint ? (form.apiKey ?? '') : null,
            tailnet: type === 'ollama' ? form.tailnet : null,
            reasoningEffort:
                type === 'anthropic' &&
                !CLAUDE_EFFORTS.some((r) => r.value === form.reasoningEffort)
                    ? 0
                    : form.reasoningEffort,
            supportedComplexities:
                type === 'anthropic'
                    ? (form.supportedComplexities ?? 1) & ~DECISIONS || 1
                    : form.supportedComplexities,
        });
    };

    const toggleComplexity = (value: number) => {
        const current = form.supportedComplexities ?? 0;
        set({ supportedComplexities: current ^ value || 1 });
    };

    const handleSave = () => {
        if (isNew) {
            addMutation.mutate({ data: form });
        } else {
            if (form.id) updateMutation.mutate({ id: form.id, data: form });
        }
    };

    const isPending = addMutation.isPending || updateMutation.isPending;

    return (
        <ProviderModal
            title={isNew ? 'Add Provider' : 'Edit Provider'}
            onClose={onClose}
            buttons={
                <>
                    <CommandButton
                        type="button"
                        isDefault
                        onClick={handleSave}
                        disabled={isPending || !form.baseUrl}
                    >
                        {isPending ? 'Saving...' : 'Save'}
                    </CommandButton>
                    <CommandButton
                        type="button"
                        onClick={onClose}
                        disabled={isPending}
                    >
                        Cancel
                    </CommandButton>
                </>
            }
        >
            <div className="flex flex-col gap-2">
                <FieldRow label="Type">
                    <ComboBox
                        value={form.type}
                        onChange={(e) => handleTypeChange(e.target.value)}
                        options={Object.entries(PROVIDER_TYPES).map(
                            ([value, t]) => ({ value, label: t.option }),
                        )}
                    />
                </FieldRow>

                <FieldRow label="Base URL">
                    <TextBox
                        value={form.baseUrl}
                        onChange={(e) => set({ baseUrl: e.target.value })}
                    />
                </FieldRow>

                {form.type === 'ollama' && tailnets.length > 0 && (
                    <FieldRow label="Connect via">
                        <ComboBox
                            value={form.tailnet ?? ''}
                            onChange={(e) =>
                                set({ tailnet: e.target.value || null })
                            }
                            options={[
                                { value: '', label: 'Direct' },
                                ...tailnets.map((name) => ({
                                    value: name,
                                    label: `Tailnet: ${name}`,
                                })),
                            ]}
                        />
                    </FieldRow>
                )}

                {kind.keyHint && (
                    <FieldRow label="API Key">
                        <TextBox
                            type="password"
                            value={form.apiKey ?? ''}
                            onChange={(e) =>
                                set({ apiKey: e.target.value || null })
                            }
                            placeholder={kind.keyHint}
                        />
                    </FieldRow>
                )}

                <FieldRow label="Model Name">
                    <TextBox
                        list={entry.id ? `models-list-${entry.id}` : undefined}
                        value={form.modelName ?? ''}
                        onChange={(e) =>
                            set({ modelName: e.target.value || null })
                        }
                        placeholder={kind.modelHint}
                    />
                    {entry.id && (
                        <datalist id={`models-list-${entry.id}`}>
                            {(models.data?.data ?? []).map((model) => (
                                <option key={model} value={model} />
                            ))}
                        </datalist>
                    )}
                </FieldRow>

                {(form.type === 'ollama' || isClaude) && (
                    <FieldRow label="Reasoning">
                        <ComboBox
                            aria-label="Reasoning"
                            value={form.reasoningEffort ?? 0}
                            onChange={(e) =>
                                set({
                                    reasoningEffort: Number(e.target.value),
                                })
                            }
                            title={
                                isClaude
                                    ? "Claude's effort: how much it thinks before answering."
                                    : 'How long a llama.cpp model may think before answering. Less is faster, more is more deliberate.'
                            }
                            options={(isClaude
                                ? CLAUDE_EFFORTS
                                : REASONING_EFFORTS
                            ).map((r) => ({
                                value: String(r.value),
                                label: isClaude
                                    ? r.label
                                    : `${r.label} (${r.hint})`,
                            }))}
                        />
                    </FieldRow>
                )}

                <FieldRow label="Used by" controlId={usedById}>
                    <RadioGroup
                        id={usedById}
                        aria-label="Used by"
                        name="provider-used-by"
                        value={form.reserved ? 'reserved' : 'all'}
                        onValueChange={(v) =>
                            set({ reserved: v === 'reserved' })
                        }
                    >
                        <RadioButton value="all">All personas</RadioButton>
                        <RadioButton value="reserved">
                            Only personas pinned to it
                        </RadioButton>
                    </RadioGroup>
                </FieldRow>

                {!form.reserved && (
                    <FieldRow label="Handles" controlId={handlesId}>
                        <fieldset
                            id={handlesId}
                            aria-label="Handles"
                            className="flex flex-wrap items-center gap-x-3 gap-y-1"
                            title="Which kinds of generation jobs this provider is trusted with — heavier jobs need a stronger model"
                        >
                            {JOB_COMPLEXITIES.filter(
                                (c) => !isClaude || c.value !== DECISIONS,
                            ).map((c) => (
                                <CheckBox
                                    key={c.value}
                                    title={c.hint}
                                    checked={
                                        ((form.supportedComplexities ?? 0) &
                                            c.value) !==
                                        0
                                    }
                                    onChange={() => toggleComplexity(c.value)}
                                >
                                    {c.label}
                                </CheckBox>
                            ))}
                        </fieldset>
                    </FieldRow>
                )}

                {!form.reserved && (
                    <FieldRow label="Share">
                        <TextBox
                            type="number"
                            min={1}
                            aria-label="Share"
                            value={form.share ?? DEFAULT_SHARE}
                            onChange={(e) =>
                                set({ share: Number(e.target.value) })
                            }
                            title="Its part of the personas, next to the other models at the same priority: 50 and 50 is half each"
                        />
                    </FieldRow>
                )}

                {!form.reserved && (
                    <FieldRow label="Priority">
                        <TextBox
                            type="number"
                            value={form.priority ?? 0}
                            onChange={(e) =>
                                set({ priority: Number(e.target.value) })
                            }
                            title="Lower goes first; equal priorities split personas by share"
                        />
                    </FieldRow>
                )}
            </div>
        </ProviderModal>
    );
}
