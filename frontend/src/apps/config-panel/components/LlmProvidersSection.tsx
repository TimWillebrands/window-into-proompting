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
