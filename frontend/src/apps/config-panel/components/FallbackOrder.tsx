import type { LlmProviderEntry } from '../../../api/model/llmProviderEntry';
import type { ProviderAvailability } from '../../../api/model/providerAvailability';
import {
    DECISIONS,
    GENERAL,
    JOB_COMPLEXITIES,
    OFFLINE,
    PROVIDER_STATES,
} from './constants';

/** Which shared providers each tier tries, in Priority order, then what it falls back to (ADR 0040). */
export default function FallbackOrder({
    providers,
    availability = [],
}: {
    providers: LlmProviderEntry[];
    availability?: ProviderAvailability[];
}) {
    const stateOf = (id?: string) =>
        availability.find((a) => a.providerId === id)?.state ?? 0;
    const chain = (tier: number) =>
        providers
            .filter(
                (p) =>
                    p.isEnabled !== false &&
                    !p.reserved &&
                    ((p.supportedComplexities ?? 0) & tier) !== 0,
            )
            .sort((a, b) => Number(a.priority ?? 0) - Number(b.priority ?? 0));

    return (
        <div
            style={{
                marginTop: 16,
                background: 'rgba(255,255,255,0.08)',
                border: '1px solid rgba(255,255,255,0.2)',
                borderRadius: 14,
                padding: '10px 14px',
                color: '#fff',
                fontSize: 11,
            }}
        >
            <div style={{ fontWeight: 700, fontSize: 12, marginBottom: 2 }}>
                Fallback order
            </div>
            <div style={{ color: 'rgba(255,255,255,0.65)', marginBottom: 8 }}>
                Lower Priority goes first. Benched providers wait at the back;
                offline ones are skipped until they answer again.
            </div>
            {JOB_COMPLEXITIES.map((tier) => {
                const steps = chain(tier.value);
                return (
                    <div
                        key={tier.value}
                        style={{
                            display: 'flex',
                            gap: 6,
                            flexWrap: 'wrap',
                            alignItems: 'baseline',
                            marginBottom: 4,
                        }}
                    >
                        <span style={{ width: 120, flexShrink: 0 }}>
                            {tier.label}
                        </span>
                        {steps.length === 0 && tier.value === GENERAL && (
                            <span style={{ opacity: 0.6 }}>none</span>
                        )}
                        {steps.map((p, i) => {
                            const state = stateOf(p.id);
                            return (
                                <span key={p.id}>
                                    {i > 0 && (
                                        <span style={{ opacity: 0.6 }}>→ </span>
                                    )}
                                    <span
                                        title={PROVIDER_STATES[state]?.label}
                                        style={{
                                            color: PROVIDER_STATES[state]
                                                ?.color,
                                            textDecoration:
                                                state === OFFLINE
                                                    ? 'line-through'
                                                    : undefined,
                                        }}
                                    >
                                        {p.modelName ?? p.baseUrl}
                                    </span>
                                </span>
                            );
                        })}
                        {tier.value !== GENERAL && (
                            <span style={{ opacity: 0.75 }}>
                                {steps.length > 0 ? '→ ' : ''}
                                {tier.value === DECISIONS
                                    ? 'LLM emulation (General)'
                                    : 'General'}
                            </span>
                        )}
                    </div>
                );
            })}
        </div>
    );
}
