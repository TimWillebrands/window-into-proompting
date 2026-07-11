import {
    useDeleteLlmConfigProvidersId,
    usePutLlmConfigProvidersId,
} from '#api/party-zone';
import type { LlmProviderEntry } from '../../../api/model/llmProviderEntry';
import { JOB_COMPLEXITIES } from './constants';

export default function ProviderCard({
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
                    ? 'rgba(255,255,255,0.12)'
                    : 'rgba(0,0,0,0.15)',
                border: `1px solid ${isEnabled ? 'rgba(255,255,255,0.3)' : 'rgba(255,255,255,0.08)'}`,
                borderRadius: 14,
                boxShadow: isEnabled
                    ? 'inset 0 1px 0 rgba(255,255,255,0.25), 0 4px 12px -6px rgba(0,0,0,0.35)'
                    : 'none',
                backdropFilter: 'blur(8px)',
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
                    title={
                        isEnabled
                            ? 'Click to disable — personas stop using this provider'
                            : 'Click to enable this provider'
                    }
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
                    onClick={() => {
                        if (!provider.id) return;
                        if (
                            !confirm(
                                'Remove this provider? Personas can no longer use it to generate replies.',
                            )
                        )
                            return;
                        deleteMutation.mutate({ id: provider.id });
                    }}
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
