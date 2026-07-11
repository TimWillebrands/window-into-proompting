import { useState } from 'react';
import { useGetLlmConfigProviders } from '#api/party-zone';
import type { LlmProviderEntry } from '../../../api/model/llmProviderEntry';
import { EMPTY_ENTRY } from './constants';
import ProviderCard from './ProviderCard';
import ProviderEditor from './ProviderEditor';
import ProviderModal from './ProviderModal';

export default function LlmProvidersSection() {
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
                        padding: '4px 14px',
                        fontSize: 12,
                        fontWeight: 600,
                        background: 'rgba(255,255,255,0.18)',
                        border: '1px solid rgba(255,255,255,0.5)',
                        borderRadius: 999,
                        boxShadow:
                            'inset 0 1px 0 rgba(255,255,255,0.4), 0 4px 10px -4px rgba(0,0,0,0.3)',
                        color: '#fff',
                        cursor: 'pointer',
                        backdropFilter: 'blur(8px)',
                    }}
                >
                    + Add Provider
                </button>
            </div>

            {providers.length === 0 && !editing && (
                <div
                    style={{
                        background: 'rgba(255,255,255,0.1)',
                        border: '1px solid rgba(255,255,255,0.25)',
                        borderRadius: 16,
                        boxShadow: 'inset 0 1px 0 rgba(255,255,255,0.2)',
                        backdropFilter: 'blur(8px)',
                        padding: '24px 20px',
                        color: 'rgba(255,255,255,0.85)',
                        fontSize: 13,
                        textAlign: 'center',
                    }}
                >
                    <div style={{ fontSize: 26, marginBottom: 6 }}>🤖</div>
                    <p style={{ margin: '0 0 4px', fontWeight: 700 }}>
                        Personas need a language model to speak.
                    </p>
                    <p
                        style={{
                            margin: 0,
                            color: 'rgba(255,255,255,0.65)',
                            fontSize: 12,
                        }}
                    >
                        Add a provider — Ollama for local models, or OpenRouter
                        with an API key — and chats can start generating.
                    </p>
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
