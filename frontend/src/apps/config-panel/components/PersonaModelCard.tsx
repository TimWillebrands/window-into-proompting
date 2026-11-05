import { usePutLlmConfigPersonaRoutesPersonaId } from '#api/party-zone';
import type { LlmProviderEntry } from '../../../api/model/llmProviderEntry';
import type { Persona } from '../../../api/model/persona';
import type { PersonaRoute } from '../../../api/model/personaRoute';
import {
    DECISIONS,
    PERSONA_TIERS,
    providerLabel,
    selectStyle,
} from './constants';

function TierSlot({
    label,
    hint,
    providerId,
    providers,
    disabled,
    onChange,
}: {
    label: string;
    hint: string;
    providerId: string | undefined;
    providers: LlmProviderEntry[];
    disabled: boolean;
    onChange: (providerId: string | undefined) => void;
}) {
    const chosen = providers.find((p) => p.id === providerId);
    const isOff = chosen?.isEnabled === false;
    // A disabled provider stays listed only while it's the current choice; a decisions-only one can't speak.
    const options = providers.filter(
        (p) =>
            p.id === providerId ||
            (p.isEnabled !== false && p.supportedComplexities !== DECISIONS),
    );
    const reserved = options.filter((p) => p.reserved);
    const shared = options.filter((p) => !p.reserved);
    const option = (p: LlmProviderEntry) => (
        <option key={p.id} value={p.id ?? ''}>
            {providerLabel(p)}
            {p.isEnabled === false ? ' (disabled)' : ''}
        </option>
    );

    return (
        <label
            style={{
                display: 'flex',
                flexDirection: 'column',
                gap: 3,
                minWidth: 0,
                padding: '8px 10px',
                borderRadius: 10,
                background: chosen
                    ? 'rgba(100,180,255,0.2)'
                    : 'rgba(255,255,255,0.07)',
                border: `1px solid ${chosen ? 'rgba(100,180,255,0.55)' : 'rgba(255,255,255,0.15)'}`,
            }}
        >
            <span style={{ color: '#fff', fontSize: 12, fontWeight: 700 }}>
                {label}
            </span>
            <span
                style={{
                    color: 'rgba(255,255,255,0.7)',
                    fontSize: 10,
                    marginBottom: 3,
                }}
            >
                {hint}
            </span>
            <select
                aria-label={label}
                value={providerId ?? ''}
                disabled={disabled}
                onChange={(e) => onChange(e.target.value || undefined)}
                style={selectStyle}
            >
                <option value="">Shared providers</option>
                {reserved.length > 0 && (
                    <optgroup label="Reserved">{reserved.map(option)}</optgroup>
                )}
                {shared.length > 0 && (
                    <optgroup label="Shared">{shared.map(option)}</optgroup>
                )}
            </select>
            {isOff && (
                <span style={{ color: '#fca', fontSize: 10, marginTop: 2 }}>
                    Provider is disabled. Using shared providers until it's back
                    on.
                </span>
            )}
        </label>
    );
}

export default function PersonaModelCard({
    persona,
    routes,
    providers,
    onChanged,
    onRemoved,
}: {
    persona: Persona;
    routes: PersonaRoute[];
    providers: LlmProviderEntry[];
    onChanged: () => void;
    onRemoved: () => void;
}) {
    const save = usePutLlmConfigPersonaRoutesPersonaId();
    const personaId = persona.id ?? '';

    const setTier = (tier: number, providerId: string | undefined) => {
        const others = routes.filter((r) => r.tier !== tier);
        save.mutate(
            {
                personaId,
                data: providerId
                    ? [...others, { personaId, tier, providerId }]
                    : others,
            },
            { onSuccess: onChanged },
        );
    };

    const remove = () => {
        if (routes.length === 0) {
            onRemoved();
            return;
        }
        save.mutate({ personaId, data: [] }, { onSuccess: onRemoved });
    };

    return (
        <div
            style={{
                background: 'rgba(255,255,255,0.12)',
                border: '1px solid rgba(255,255,255,0.3)',
                borderRadius: 14,
                boxShadow:
                    'inset 0 1px 0 rgba(255,255,255,0.25), 0 4px 12px -6px rgba(0,0,0,0.35)',
                backdropFilter: 'blur(8px)',
                padding: '12px 14px',
            }}
        >
            <div
                style={{
                    display: 'flex',
                    alignItems: 'center',
                    gap: 8,
                    marginBottom: 10,
                }}
            >
                <span
                    aria-hidden
                    style={{
                        width: 26,
                        height: 26,
                        flexShrink: 0,
                        borderRadius: 999,
                        display: 'flex',
                        alignItems: 'center',
                        justifyContent: 'center',
                        background: 'rgba(255,255,255,0.22)',
                        border: '1px solid rgba(255,255,255,0.45)',
                        color: '#fff',
                        fontSize: 12,
                        fontWeight: 700,
                    }}
                >
                    {persona.name?.charAt(0).toUpperCase()}
                </span>
                <span
                    style={{
                        flex: 1,
                        minWidth: 0,
                        color: '#fff',
                        fontSize: 13,
                        fontWeight: 700,
                        overflow: 'hidden',
                        textOverflow: 'ellipsis',
                        whiteSpace: 'nowrap',
                    }}
                >
                    {persona.name}
                </span>
                {save.isError && (
                    <span role="alert" style={{ color: '#fca', fontSize: 11 }}>
                        Couldn't save that change. Try again.
                    </span>
                )}
                {save.isPending && (
                    <span
                        style={{
                            color: 'rgba(255,255,255,0.75)',
                            fontSize: 11,
                        }}
                    >
                        Saving...
                    </span>
                )}
                <button
                    type="button"
                    onClick={remove}
                    disabled={save.isPending}
                    title="Put this persona back on the shared providers"
                    style={{
                        fontSize: 11,
                        padding: '2px 8px',
                        background: 'rgba(255,255,255,0.1)',
                        border: '1px solid rgba(255,255,255,0.3)',
                        color: '#fff',
                        cursor: 'pointer',
                    }}
                >
                    Remove
                </button>
            </div>
            <div
                style={{
                    display: 'grid',
                    gridTemplateColumns: 'repeat(auto-fit, minmax(170px, 1fr))',
                    gap: 8,
                }}
            >
                {PERSONA_TIERS.map((c) => (
                    <TierSlot
                        key={c.value}
                        label={c.label}
                        hint={c.hint}
                        providerId={
                            routes.find((r) => r.tier === c.value)?.providerId
                        }
                        providers={providers}
                        disabled={save.isPending}
                        onChange={(id) => setTier(c.value, id)}
                    />
                ))}
            </div>
        </div>
    );
}
