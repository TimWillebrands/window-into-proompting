import { useState } from 'react';
import type { LlmProviderEntry } from '../../api/model/llmProviderEntry';
import {
    useDeleteLlmConfigProvidersId,
    useGetLlmConfigProviders,
    useGetLlmConfigProvidersIdModels,
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
    isEnabled: true,
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
                        onToggled={refetch}
                    />
                ))}
            </div>

            {editing && (
                <ProviderModal
                    title={isNew ? 'Add Provider' : 'Edit Provider'}
                    onClose={close}
                >
                    <ProviderEditor
                        entry={editing}
                        isNew={isNew}
                        onClose={close}
                    />
                </ProviderModal>
            )}
        </div>
    );
}

function ProviderCard({
    provider,
    onEdit,
    onDeleted,
    onToggled,
}: {
    provider: LlmProviderEntry;
    onEdit: () => void;
    onDeleted: () => void;
    onToggled: () => void;
}) {
    const deleteMutation = useDeleteLlmConfigProvidersId({
        mutation: { onSuccess: onDeleted },
    });
    const toggleMutation = usePutLlmConfigProvidersId({
        mutation: { onSuccess: onToggled },
    });

    const complexityLabels = JOB_COMPLEXITIES.filter(
        (c) => ((provider.supportedComplexities ?? 0) & c.value) !== 0,
    ).map((c) => c.label);

    const isOllama = provider.type === 'ollama';
    const isEnabled = provider.isEnabled !== false;

    const handleToggle = () => {
        if (provider.id) {
            toggleMutation.mutate({
                id: provider.id,
                data: { ...provider, isEnabled: !isEnabled },
            });
        }
    };

    return (
        <div
            style={{
                background: isEnabled
                    ? 'rgba(255,255,255,0.1)'
                    : 'rgba(0,0,0,0.15)',
                border: `1px solid ${isEnabled ? 'rgba(255,255,255,0.2)' : 'rgba(255,255,255,0.08)'}`,
                padding: '12px 14px',
                display: 'flex',
                alignItems: 'center',
                gap: 12,
                opacity: isEnabled ? 1 : 0.55,
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
                        display: 'flex',
                        alignItems: 'center',
                        gap: 6,
                    }}
                >
                    {isOllama ? 'Ollama' : 'OpenRouter'}
                    {!isEnabled && (
                        <span
                            style={{
                                fontSize: 10,
                                fontWeight: 400,
                                background: 'rgba(255,160,0,0.25)',
                                border: '1px solid rgba(255,160,0,0.4)',
                                color: '#fca',
                                padding: '0px 5px',
                            }}
                        >
                            disabled
                        </span>
                    )}
                    <span
                        style={{
                            marginLeft: 2,
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
                    onClick={handleToggle}
                    disabled={toggleMutation.isPending}
                    style={{
                        fontSize: 11,
                        padding: '2px 8px',
                        background: isEnabled
                            ? 'rgba(60,180,60,0.2)'
                            : 'rgba(255,255,255,0.08)',
                        border: `1px solid ${isEnabled ? 'rgba(60,180,60,0.5)' : 'rgba(255,255,255,0.2)'}`,
                        color: isEnabled ? '#afa' : 'rgba(255,255,255,0.5)',
                        cursor: toggleMutation.isPending ? 'wait' : 'pointer',
                    }}
                >
                    {toggleMutation.isPending
                        ? '...'
                        : isEnabled
                          ? 'Enabled'
                          : 'Disabled'}
                </button>
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

function ProviderModal({
    title,
    onClose,
    children,
}: {
    title: string;
    onClose: () => void;
    children: React.ReactNode;
}) {
    return (
        // biome-ignore lint/a11y/noStaticElementInteractions: backdrop click dismisses modal; real dialog is inside
        <div
            role="presentation"
            style={{
                position: 'fixed',
                inset: 0,
                zIndex: 1000,
                background: 'rgba(0,0,0,0.5)',
                display: 'flex',
                alignItems: 'center',
                justifyContent: 'center',
            }}
            onClick={onClose}
            onKeyDown={(e) => {
                if (e.key === 'Escape') {
                    onClose();
                }
            }}
        >
            <div
                role="dialog"
                aria-label={title}
                className="window"
                style={{
                    width: 480,
                    maxHeight: '85vh',
                    display: 'flex',
                    flexDirection: 'column',
                }}
                onClick={(e) => e.stopPropagation()}
                onKeyDown={(e) => e.stopPropagation()}
            >
                <div className="title-bar box-content">
                    <div className="title-bar-text">{title}</div>
                    <div className="title-bar-controls">
                        <button
                            type="button"
                            aria-label="Close"
                            onClick={onClose}
                        />
                    </div>
                </div>
                <div
                    className="window-body"
                    style={{
                        overflowY: 'auto',
                    }}
                >
                    {children}
                </div>
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
    const models = useGetLlmConfigProvidersIdModels(entry.id ?? '', {
        query: { enabled: !!entry.id },
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
        <div>
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

            {/* Job complexity */}
            <FormRow label="Handles">
                <div style={{ display: 'flex', gap: 12 }}>
                    {JOB_COMPLEXITIES.map((c) => (
                        <label
                            key={c.value}
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

            {/* Actions */}
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
                flexDirection: 'column',
                gap: 4,
                marginBottom: 12,
            }}
        >
            <div
                style={{
                    color: '#1B5EAD',
                    fontSize: 12,
                    fontWeight: 600,
                }}
            >
                {label}
            </div>
            {children}
        </div>
    );
}

const inputStyle: React.CSSProperties = {
    width: '100%',
    padding: '6px 8px',
    fontSize: 12,
    background: 'rgba(255,255,255,0.9)',
    border: '1px solid #7F9DB9',
    color: '#000',
    boxSizing: 'border-box',
};

const selectStyle: React.CSSProperties = {
    width: '100%',
    padding: '6px 8px',
    fontSize: 12,
    background: 'rgba(255,255,255,0.9)',
    border: '1px solid #7F9DB9',
    color: '#000',
    boxSizing: 'border-box',
};
