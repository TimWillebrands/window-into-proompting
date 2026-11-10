import { useState } from 'react';
import {
    useGetLlmConfigProvidersIdModels,
    useGetLlmConfigTailnets,
    usePostLlmConfigProviders,
    usePutLlmConfigProvidersId,
} from '#api/party-zone';
import type { LlmProviderEntry } from '../../../api/model/llmProviderEntry';
import {
    DECISIONS,
    inputStyle,
    JOB_COMPLEXITIES,
    PROVIDER_TYPES,
    providerType,
    REASONING_EFFORTS,
    selectStyle,
} from './constants';

/** Claude takes effort low/medium/high; the token budgets are llama.cpp's. */
const CLAUDE_EFFORTS = REASONING_EFFORTS.filter((r) =>
    [0, 2, 3, 4].includes(r.value),
);

import FormRow from './FormRow';
import { DEFAULT_SHARE } from './split';

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
    // A saved Tailnet stays listed if its config is gone, so saving won't silently drop it.
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
            // Claude can't serve /systemone, so it never keeps Decisions.
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
        <div>
            <FormRow label="Type">
                <select
                    value={form.type}
                    onChange={(e) => handleTypeChange(e.target.value)}
                    style={selectStyle}
                >
                    {Object.entries(PROVIDER_TYPES).map(([value, t]) => (
                        <option key={value} value={value}>
                            {t.option}
                        </option>
                    ))}
                </select>
            </FormRow>

            <FormRow label="Base URL">
                <input
                    type="text"
                    value={form.baseUrl}
                    onChange={(e) => set({ baseUrl: e.target.value })}
                    style={inputStyle}
                />
            </FormRow>

            {form.type === 'ollama' && tailnets.length > 0 && (
                <FormRow label="Connect via">
                    <select
                        value={form.tailnet ?? ''}
                        onChange={(e) =>
                            set({ tailnet: e.target.value || null })
                        }
                        style={selectStyle}
                    >
                        <option value="">Direct</option>
                        {tailnets.map((name) => (
                            <option key={name} value={name}>
                                Tailnet: {name}
                            </option>
                        ))}
                    </select>
                </FormRow>
            )}

            {kind.keyHint && (
                <FormRow label="API Key">
                    <input
                        type="password"
                        value={form.apiKey ?? ''}
                        onChange={(e) =>
                            set({ apiKey: e.target.value || null })
                        }
                        placeholder={kind.keyHint}
                        style={inputStyle}
                    />
                </FormRow>
            )}

            <FormRow label="Model Name">
                <div style={{ flex: 1, position: 'relative' }}>
                    <input
                        type="text"
                        list={entry.id ? `models-list-${entry.id}` : undefined}
                        value={form.modelName ?? ''}
                        onChange={(e) =>
                            set({ modelName: e.target.value || null })
                        }
                        placeholder={kind.modelHint}
                        style={inputStyle}
                    />
                    {entry.id && (
                        <datalist id={`models-list-${entry.id}`}>
                            {(models.data?.data ?? []).map((model) => (
                                <option key={model} value={model} />
                            ))}
                        </datalist>
                    )}
                </div>
            </FormRow>

            {(form.type === 'ollama' || isClaude) && (
                <FormRow label="Reasoning">
                    <select
                        aria-label="Reasoning"
                        value={form.reasoningEffort ?? 0}
                        onChange={(e) =>
                            set({ reasoningEffort: Number(e.target.value) })
                        }
                        title={
                            isClaude
                                ? "Claude's effort: how much it thinks before answering."
                                : 'How long a llama.cpp model may think before answering. Less is faster, more is more deliberate.'
                        }
                        style={selectStyle}
                    >
                        {(isClaude ? CLAUDE_EFFORTS : REASONING_EFFORTS).map(
                            (r) => (
                                <option key={r.value} value={r.value}>
                                    {isClaude
                                        ? r.label
                                        : `${r.label} (${r.hint})`}
                                </option>
                            ),
                        )}
                    </select>
                </FormRow>
            )}

            <FormRow label="Used by">
                <div
                    style={{ display: 'flex', flexDirection: 'column', gap: 4 }}
                >
                    {[
                        { reserved: false, label: 'All personas' },
                        {
                            reserved: true,
                            label: 'Only personas pinned to it',
                        },
                    ].map((o) => (
                        <label
                            key={o.label}
                            style={{
                                color: '#000',
                                fontSize: 12,
                                display: 'flex',
                                alignItems: 'center',
                                gap: 4,
                                cursor: 'pointer',
                            }}
                        >
                            <input
                                type="radio"
                                name="provider-used-by"
                                checked={!!form.reserved === o.reserved}
                                onChange={() => set({ reserved: o.reserved })}
                            />
                            {o.label}
                        </label>
                    ))}
                </div>
            </FormRow>

            {!form.reserved && (
                <FormRow label="Handles">
                    <div
                        style={{
                            display: 'flex',
                            gap: 12,
                            flexWrap: 'wrap',
                            alignItems: 'center',
                        }}
                        title="Which kinds of generation jobs this provider is trusted with — heavier jobs need a stronger model"
                    >
                        {JOB_COMPLEXITIES.filter(
                            (c) => !isClaude || c.value !== DECISIONS,
                        ).map((c) => (
                            <label
                                key={c.value}
                                title={c.hint}
                                style={{
                                    color: '#000',
                                    fontSize: 12,
                                    display: 'flex',
                                    alignItems: 'center',
                                    gap: 4,
                                    cursor: 'pointer',
                                }}
                            >
                                <input
                                    type="checkbox"
                                    checked={
                                        ((form.supportedComplexities ?? 0) &
                                            c.value) !==
                                        0
                                    }
                                    onChange={() => toggleComplexity(c.value)}
                                />
                                {c.label}
                            </label>
                        ))}
                    </div>
                </FormRow>
            )}

            {!form.reserved && (
                <FormRow label="Share">
                    <input
                        type="number"
                        min={1}
                        aria-label="Share"
                        value={form.share ?? DEFAULT_SHARE}
                        onChange={(e) => set({ share: Number(e.target.value) })}
                        title="Its part of the personas, next to the other models at the same priority: 50 and 50 is half each"
                        style={inputStyle}
                    />
                </FormRow>
            )}

            {!form.reserved && (
                <FormRow label="Priority">
                    <input
                        type="number"
                        value={form.priority ?? 0}
                        onChange={(e) =>
                            set({ priority: Number(e.target.value) })
                        }
                        title="Lower goes first; equal priorities split personas by share"
                        style={inputStyle}
                    />
                </FormRow>
            )}

            <div style={{ display: 'flex', gap: 8, marginTop: 16 }}>
                <button
                    type="button"
                    onClick={handleSave}
                    disabled={isPending || !form.baseUrl}
                >
                    {isPending ? 'Saving...' : 'Save'}
                </button>
                <button type="button" onClick={onClose} disabled={isPending}>
                    Cancel
                </button>
            </div>
        </div>
    );
}
