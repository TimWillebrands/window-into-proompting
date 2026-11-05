import { useState } from 'react';
import {
    useGetLlmConfigProvidersIdModels,
    useGetLlmConfigTailnets,
    usePostLlmConfigProviders,
    usePutLlmConfigProvidersId,
} from '#api/party-zone';
import type { LlmProviderEntry } from '../../../api/model/llmProviderEntry';
import { inputStyle, JOB_COMPLEXITIES, selectStyle } from './constants';
import FormRow from './FormRow';

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

    const handleTypeChange = (type: string) => {
        set({
            type,
            baseUrl:
                type === 'openrouter'
                    ? 'https://openrouter.ai/api/v1'
                    : 'http://localhost:11434',
            apiKey: type === 'openrouter' ? (form.apiKey ?? '') : null,
            tailnet: type === 'openrouter' ? null : form.tailnet,
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
                    <option value="ollama">Ollama (local)</option>
                    <option value="openrouter">OpenRouter</option>
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

            {form.type === 'openrouter' && (
                <FormRow label="API Key">
                    <input
                        type="password"
                        value={form.apiKey ?? ''}
                        onChange={(e) =>
                            set({ apiKey: e.target.value || null })
                        }
                        placeholder="sk-or-..."
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
                        placeholder={
                            form.type === 'ollama'
                                ? 'e.g. llama3.2'
                                : 'e.g. nvidia/llama-3.1-nemotron-ultra-253b-v1:free'
                        }
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

            {form.type === 'ollama' && (
                <FormRow label="Thinking">
                    <label
                        style={{
                            color: '#000',
                            fontSize: 12,
                            display: 'flex',
                            alignItems: 'center',
                            gap: 4,
                            cursor: 'pointer',
                        }}
                        title="For llama.cpp models that reason before answering: replies come much faster, with less deliberation"
                    >
                        <input
                            type="checkbox"
                            checked={!!form.disableThinking}
                            onChange={(e) =>
                                set({ disableThinking: e.target.checked })
                            }
                        />
                        Skip thinking for faster replies
                    </label>
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
                            label: 'Only personas assigned to it in Persona Models',
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
                        {JOB_COMPLEXITIES.map((c) => (
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
                <FormRow label="Priority">
                    <input
                        type="number"
                        value={form.priority ?? 0}
                        onChange={(e) =>
                            set({ priority: Number(e.target.value) })
                        }
                        title="Lower goes first; equal priorities share the load"
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
