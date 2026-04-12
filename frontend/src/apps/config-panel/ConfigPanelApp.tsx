import { useState } from 'react';
import type { LlmProviderEntry } from '../../api/model/llmProviderEntry';
import {
    useDeleteLlmConfigProvidersId,
    useGetLlmConfigProviders,
    usePostLlmConfigProviders,
    usePutLlmConfigProvidersId,
} from '../../api/party-zone';
import { useDesktopContext } from '../../lib/desktop-context';
import { useNavHistoryStore } from '../../lib/nav-history';

const SECTIONS = [
    {
        id: 'llm-providers',
        label: 'Language Model Providers',
        icon: '🤖',
        description: 'Configure AI model endpoints and API keys',
    },
] as const;

const SECTION_NAMES: Record<string, string> = {
    home: 'Control Panel',
    'llm-providers': 'Language Model Providers',
};

export default function ConfigPanelApp() {
    const { stack, index } = useNavHistoryStore();
    const { focusWindow } = useDesktopContext();

    const currentEntry = stack[index];
    const section: string =
        currentEntry?.kind === 'config' ? currentEntry.section : 'home';

    const canGoBack = index > 0;
    const canGoForward = index < stack.length - 1;

    const handleBack = () => {
        const store = useNavHistoryStore.getState();
        const entry = store.back();
        if (entry?.kind === 'window') {
            focusWindow(entry.windowId);
        }
        store.clearNavigating();
    };

    const handleForward = () => {
        const store = useNavHistoryStore.getState();
        const entry = store.forward();
        if (entry?.kind === 'window') {
            focusWindow(entry.windowId);
        }
        store.clearNavigating();
    };

    const navigateTo = (s: string) => {
        useNavHistoryStore.getState().push({ kind: 'config', section: s });
    };

    const navigateHome = () => {
        useNavHistoryStore
            .getState()
            .push({ kind: 'window', windowId: 'config-panel' });
    };

    return (
        <div
            style={{
                display: 'flex',
                flexDirection: 'column',
                height: '100%',
                background: '#ECE9D8',
                overflow: 'hidden',
            }}
        >
            {/* Navigation toolbar */}
            <div
                style={{
                    display: 'flex',
                    alignItems: 'center',
                    gap: 4,
                    padding: '3px 6px',
                    borderBottom: '1px solid #ACA899',
                    background: '#ECE9D8',
                }}
            >
                <button
                    type="button"
                    disabled={!canGoBack}
                    onClick={handleBack}
                    style={{
                        minWidth: 60,
                        display: 'flex',
                        alignItems: 'center',
                        gap: 3,
                    }}
                >
                    <span style={{ fontSize: 10 }}>◀</span> Back
                </button>
                <button
                    type="button"
                    disabled={!canGoForward}
                    onClick={handleForward}
                    style={{
                        minWidth: 72,
                        display: 'flex',
                        alignItems: 'center',
                        gap: 3,
                    }}
                >
                    Forward <span style={{ fontSize: 10 }}>▶</span>
                </button>
                <div
                    style={{
                        flex: 1,
                        marginLeft: 8,
                        padding: '2px 6px',
                        background: '#fff',
                        border: '1px solid #7F9DB9',
                        fontSize: 12,
                        color: '#000',
                    }}
                >
                    {SECTION_NAMES[section] ?? section}
                </div>
            </div>

            {/* Body */}
            <div style={{ display: 'flex', flex: 1, overflow: 'hidden' }}>
                {/* Sidebar */}
                <div
                    style={{
                        width: 170,
                        flexShrink: 0,
                        borderRight: '1px solid #ACA899',
                        display: 'flex',
                        flexDirection: 'column',
                        background: '#D6E4F7',
                        overflow: 'hidden',
                    }}
                >
                    <div
                        style={{
                            background:
                                'linear-gradient(to bottom, #1B5EAD, #3A85C7)',
                            color: '#fff',
                            padding: '8px 10px',
                            fontWeight: 700,
                            fontSize: 13,
                            display: 'flex',
                            alignItems: 'center',
                            gap: 6,
                        }}
                    >
                        <span style={{ fontSize: 18 }}>⚙️</span>
                        Control Panel
                    </div>

                    <div
                        style={{
                            padding: '8px 6px',
                            borderBottom: '1px solid #ACA899',
                        }}
                    >
                        <button
                            type="button"
                            onClick={navigateHome}
                            style={{
                                width: '100%',
                                textAlign: 'left',
                                background: 'none',
                                border: 'none',
                                cursor: 'pointer',
                                padding: '2px 4px',
                                fontSize: 12,
                                color: '#003399',
                                textDecoration: 'underline',
                            }}
                        >
                            🏠 Home
                        </button>
                    </div>

                    <div
                        style={{
                            padding: '6px 8px',
                            fontSize: 11,
                            fontWeight: 700,
                            color: '#1B5EAD',
                            borderBottom: '1px solid #ACA899',
                        }}
                    >
                        See Also
                    </div>
                    <div
                        style={{
                            padding: '6px 8px',
                            fontSize: 12,
                            color: '#555',
                        }}
                    >
                        More settings coming soon.
                    </div>
                </div>

                {/* Main content */}
                <div
                    style={{
                        flex: 1,
                        background:
                            'linear-gradient(135deg, #5C87C2 0%, #4A73B0 100%)',
                        padding: 20,
                        overflowY: 'auto',
                    }}
                >
                    {section === 'home' ? (
                        <ControlPanelHome onNavigate={navigateTo} />
                    ) : section === 'llm-providers' ? (
                        <LlmProvidersSection />
                    ) : (
                        <div style={{ color: '#fff' }}>Unknown section.</div>
                    )}
                </div>
            </div>
        </div>
    );
}

function ControlPanelHome({
    onNavigate,
}: {
    onNavigate: (section: string) => void;
}) {
    return (
        <div>
            <h2
                style={{
                    color: '#fff',
                    fontSize: 22,
                    fontWeight: 300,
                    margin: '0 0 20px 0',
                    borderBottom: '1px solid rgba(255,255,255,0.4)',
                    paddingBottom: 8,
                    textShadow: '0 1px 2px rgba(0,0,0,0.3)',
                }}
            >
                Pick a category
            </h2>
            <div
                style={{
                    display: 'grid',
                    gridTemplateColumns:
                        'repeat(auto-fill, minmax(200px, 1fr))',
                    gap: 12,
                }}
            >
                {SECTIONS.map((s) => (
                    <CategoryTile
                        key={s.id}
                        icon={s.icon}
                        label={s.label}
                        description={s.description}
                        onClick={() => onNavigate(s.id)}
                    />
                ))}
            </div>
        </div>
    );
}

function CategoryTile({
    icon,
    label,
    description,
    onClick,
}: {
    icon: string;
    label: string;
    description: string;
    onClick: () => void;
}) {
    return (
        <button
            type="button"
            onClick={onClick}
            style={{
                display: 'flex',
                alignItems: 'center',
                gap: 12,
                padding: '12px 14px',
                background: 'rgba(255,255,255,0.12)',
                border: '1px solid rgba(255,255,255,0.2)',
                borderRadius: 2,
                cursor: 'pointer',
                textAlign: 'left',
                transition: 'background 0.1s',
            }}
            onMouseEnter={(e) => {
                (e.currentTarget as HTMLButtonElement).style.background =
                    'rgba(255,255,255,0.22)';
            }}
            onMouseLeave={(e) => {
                (e.currentTarget as HTMLButtonElement).style.background =
                    'rgba(255,255,255,0.12)';
            }}
        >
            <span style={{ fontSize: 36, lineHeight: 1, flexShrink: 0 }}>
                {icon}
            </span>
            <div>
                <div
                    style={{
                        color: '#fff',
                        fontWeight: 700,
                        fontSize: 13,
                        marginBottom: 2,
                    }}
                >
                    {label}
                </div>
                <div style={{ color: 'rgba(255,255,255,0.75)', fontSize: 11 }}>
                    {description}
                </div>
            </div>
        </button>
    );
}

// Flags enum values matching backend JobComplexity
const JOB_COMPLEXITIES = [
    { value: 1, label: 'General' },
    { value: 2, label: 'Character Voice' },
    { value: 4, label: 'Character Thoughts' },
] as const;

const EMPTY_ENTRY: LlmProviderEntry = {
    type: 'ollama',
    baseUrl: 'http://localhost:11434',
    apiKey: null,
    modelName: null,
    supportedComplexities: 1,
};

function LlmProvidersSection() {
    const { data, refetch } = useGetLlmConfigProviders();
    const providers: LlmProviderEntry[] = data?.data ?? [];
    const [editing, setEditing] = useState<LlmProviderEntry | null>(null);
    const [isNew, setIsNew] = useState(false);

    const openAdd = () => {
        setEditing({ ...EMPTY_ENTRY });
        setIsNew(true);
    };

    const openEdit = (p: LlmProviderEntry) => {
        setEditing({ ...p });
        setIsNew(false);
    };

    const close = () => {
        setEditing(null);
        setIsNew(false);
        refetch();
    };

    return (
        <div>
            <div
                style={{
                    display: 'flex',
                    alignItems: 'center',
                    justifyContent: 'space-between',
                    marginBottom: 16,
                }}
            >
                <h2
                    style={{
                        color: '#fff',
                        fontSize: 22,
                        fontWeight: 300,
                        margin: 0,
                        textShadow: '0 1px 2px rgba(0,0,0,0.3)',
                    }}
                >
                    Language Model Providers
                </h2>
                <button
                    type="button"
                    onClick={openAdd}
                    style={{
                        padding: '4px 12px',
                        fontSize: 12,
                        background: 'rgba(255,255,255,0.15)',
                        border: '1px solid rgba(255,255,255,0.4)',
                        color: '#fff',
                        cursor: 'pointer',
                    }}
                >
                    + Add Provider
                </button>
            </div>

            {providers.length === 0 && !editing && (
                <div
                    style={{
                        background: 'rgba(255,255,255,0.08)',
                        border: '1px solid rgba(255,255,255,0.15)',
                        padding: '20px',
                        color: 'rgba(255,255,255,0.6)',
                        fontSize: 13,
                        textAlign: 'center',
                    }}
                >
                    No providers configured. Click "Add Provider" to get
                    started.
                </div>
            )}

            <div style={{ display: 'flex', flexDirection: 'column', gap: 8 }}>
                {providers.map((p) => (
                    <ProviderCard
                        key={p.id}
                        provider={p}
                        onEdit={() => openEdit(p)}
                        onDeleted={refetch}
                    />
                ))}
            </div>

            {editing && (
                <ProviderEditor entry={editing} isNew={isNew} onClose={close} />
            )}
        </div>
    );
}

function ProviderCard({
    provider,
    onEdit,
    onDeleted,
}: {
    provider: LlmProviderEntry;
    onEdit: () => void;
    onDeleted: () => void;
}) {
    const deleteMutation = useDeleteLlmConfigProvidersId({
        mutation: { onSuccess: onDeleted },
    });

    const complexityLabels = JOB_COMPLEXITIES.filter(
        (c) => ((provider.supportedComplexities ?? 0) & c.value) !== 0,
    ).map((c) => c.label);

    const isOllama = provider.type === 'ollama';

    return (
        <div
            style={{
                background: 'rgba(255,255,255,0.1)',
                border: '1px solid rgba(255,255,255,0.2)',
                padding: '12px 14px',
                display: 'flex',
                alignItems: 'center',
                gap: 12,
            }}
        >
            <span style={{ fontSize: 24, flexShrink: 0 }}>
                {isOllama ? '🦙' : '🌐'}
            </span>
            <div style={{ flex: 1, minWidth: 0 }}>
                <div
                    style={{
                        color: '#fff',
                        fontWeight: 700,
                        fontSize: 13,
                        marginBottom: 2,
                    }}
                >
                    {isOllama ? 'Ollama' : 'OpenRouter'}
                    <span
                        style={{
                            marginLeft: 8,
                            fontWeight: 400,
                            opacity: 0.7,
                            fontSize: 11,
                            fontFamily: 'monospace',
                        }}
                    >
                        {provider.baseUrl}
                    </span>
                </div>
                <div
                    style={{
                        display: 'flex',
                        gap: 6,
                        flexWrap: 'wrap',
                        alignItems: 'center',
                    }}
                >
                    {provider.modelName && (
                        <span
                            style={{
                                background: 'rgba(255,255,255,0.12)',
                                border: '1px solid rgba(255,255,255,0.2)',
                                color: '#fff',
                                fontSize: 10,
                                padding: '1px 6px',
                            }}
                        >
                            {provider.modelName}
                        </span>
                    )}
                    {complexityLabels.map((label) => (
                        <span
                            key={label}
                            style={{
                                background: 'rgba(100,180,255,0.2)',
                                border: '1px solid rgba(100,180,255,0.4)',
                                color: '#adf',
                                fontSize: 10,
                                padding: '1px 6px',
                            }}
                        >
                            {label}
                        </span>
                    ))}
                </div>
            </div>
            <div style={{ display: 'flex', gap: 4, flexShrink: 0 }}>
                <button
                    type="button"
                    onClick={onEdit}
                    style={{
                        fontSize: 11,
                        padding: '2px 8px',
                        background: 'rgba(255,255,255,0.1)',
                        border: '1px solid rgba(255,255,255,0.3)',
                        color: '#fff',
                        cursor: 'pointer',
                    }}
                >
                    Edit
                </button>
                <button
                    type="button"
                    onClick={() =>
                        provider.id &&
                        deleteMutation.mutate({ id: provider.id })
                    }
                    disabled={deleteMutation.isPending}
                    style={{
                        fontSize: 11,
                        padding: '2px 8px',
                        background: 'rgba(220,60,60,0.2)',
                        border: '1px solid rgba(220,60,60,0.5)',
                        color: '#faa',
                        cursor: 'pointer',
                    }}
                >
                    {deleteMutation.isPending ? '...' : 'Remove'}
                </button>
            </div>
        </div>
    );
}

function ProviderEditor({
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
        });
    };

    const toggleComplexity = (value: number) => {
        const current = form.supportedComplexities ?? 0;
        set({ supportedComplexities: current ^ value || 1 }); // always keep at least General
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
        <div
            style={{
                marginTop: 16,
                background: 'rgba(0,0,0,0.25)',
                border: '1px solid rgba(255,255,255,0.3)',
                padding: 16,
            }}
        >
            <div
                style={{
                    color: '#fff',
                    fontWeight: 700,
                    fontSize: 14,
                    marginBottom: 12,
                }}
            >
                {isNew ? 'Add Provider' : 'Edit Provider'}
            </div>

            {/* Type */}
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

            {/* Base URL */}
            <FormRow label="Base URL">
                <input
                    type="text"
                    value={form.baseUrl}
                    onChange={(e) => set({ baseUrl: e.target.value })}
                    style={inputStyle}
                />
            </FormRow>

            {/* API Key (OpenRouter only) */}
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

            {/* Model name */}
            <FormRow label="Model Name">
                <input
                    type="text"
                    value={form.modelName ?? ''}
                    onChange={(e) => set({ modelName: e.target.value || null })}
                    placeholder={
                        form.type === 'ollama'
                            ? 'e.g. llama3.2'
                            : 'e.g. nvidia/llama-3.1-nemotron-ultra-253b-v1:free'
                    }
                    style={inputStyle}
                />
            </FormRow>

            {/* Job complexity */}
            <FormRow label="Handles">
                <div style={{ display: 'flex', gap: 12 }}>
                    {JOB_COMPLEXITIES.map((c) => (
                        <label
                            key={c.value}
                            style={{
                                color: '#fff',
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

            {/* Actions */}
            <div style={{ display: 'flex', gap: 8, marginTop: 16 }}>
                <button
                    type="button"
                    onClick={handleSave}
                    disabled={isPending || !form.baseUrl}
                    style={{
                        padding: '4px 16px',
                        fontSize: 12,
                        background: 'rgba(60,140,255,0.3)',
                        border: '1px solid rgba(60,140,255,0.6)',
                        color: '#fff',
                        cursor: isPending ? 'wait' : 'pointer',
                    }}
                >
                    {isPending ? 'Saving...' : 'Save'}
                </button>
                <button
                    type="button"
                    onClick={onClose}
                    disabled={isPending}
                    style={{
                        padding: '4px 12px',
                        fontSize: 12,
                        background: 'rgba(255,255,255,0.08)',
                        border: '1px solid rgba(255,255,255,0.25)',
                        color: '#fff',
                        cursor: 'pointer',
                    }}
                >
                    Cancel
                </button>
            </div>
        </div>
    );
}

function FormRow({
    label,
    children,
}: {
    label: string;
    children: React.ReactNode;
}) {
    return (
        <div
            style={{
                display: 'flex',
                alignItems: 'center',
                gap: 10,
                marginBottom: 8,
            }}
        >
            <div
                style={{
                    width: 90,
                    flexShrink: 0,
                    color: 'rgba(255,255,255,0.75)',
                    fontSize: 12,
                    textAlign: 'right',
                }}
            >
                {label}
            </div>
            {children}
        </div>
    );
}

const inputStyle: React.CSSProperties = {
    flex: 1,
    padding: '3px 6px',
    fontSize: 12,
    background: 'rgba(255,255,255,0.9)',
    border: '1px solid #7F9DB9',
    color: '#000',
};

const selectStyle: React.CSSProperties = {
    padding: '3px 6px',
    fontSize: 12,
    background: 'rgba(255,255,255,0.9)',
    border: '1px solid #7F9DB9',
    color: '#000',
    minWidth: 160,
};
