import { useState } from 'react';
import { usePutLlmConfigProvidersId } from '#api/party-zone';
import type { LlmProviderEntry } from '../../../api/model/llmProviderEntry';
import type { ModelAssignment } from '../../../api/model/modelAssignment';
import type { ProviderAvailability } from '../../../api/model/providerAvailability';
import {
    BENCHED,
    DECISIONS,
    GENERAL,
    INK_MUTED,
    JOB_COMPLEXITIES,
    OFFLINE,
    PROVIDER_STATES,
    providerType,
    SHEET,
    seriesColor,
    shortModel,
} from './constants';
import { percent, shareOf, splitOf } from './split';

/** Persona id the assignments use for work nobody owns. */
export const NOBODY = '00000000-0000-0000-0000-000000000000';

const STATE_ICONS = ['', '⚠', '✕'] as const;

/** Dark ink on the light fills, white on the rest. */
const inkOn = (hex: string) => {
    const [r, g, b] = [1, 3, 5].map((i) => parseInt(hex.slice(i, i + 2), 16));
    return r * 0.299 + g * 0.587 + b * 0.114 > 150 ? '#0b0b0b' : '#fff';
};

const HATCH =
    'repeating-linear-gradient(135deg, rgba(255,255,255,0.55) 0 3px, transparent 3px 7px)';

function Swatch({ color }: { color: string }) {
    return (
        <span
            aria-hidden
            style={{
                width: 10,
                height: 10,
                borderRadius: 3,
                background: color,
                flexShrink: 0,
                display: 'inline-block',
            }}
        />
    );
}

/** How the pool is split per kind of work, and the Shares that decide it (ADR 0048). */
export default function ModelSplit({
    providers,
    availability = [],
    assignments = [],
    onSaved,
}: {
    providers: LlmProviderEntry[];
    availability?: ProviderAvailability[];
    assignments?: ModelAssignment[];
    onSaved: () => void;
}) {
    const save = usePutLlmConfigProvidersId();
    const [draft, setDraft] = useState<Record<string, number>>({});

    const pool = providers.filter((p) => p.isEnabled !== false && !p.reserved);
    // Alone at its priority in every kind of work, a share decides nothing.
    const shares = (p: LlmProviderEntry) =>
        JOB_COMPLEXITIES.some((tier) =>
            splitOf(providers, tier.value).some(
                (step) =>
                    step.parts.length > 1 &&
                    step.parts.some((part) => part.provider.id === p.id),
            ),
        );
    const drafted = providers.map((p) =>
        p.id && draft[p.id] !== undefined ? { ...p, share: draft[p.id] } : p,
    );
    const changed = pool.filter(
        (p) => p.id && draft[p.id] !== undefined && draft[p.id] !== shareOf(p),
    );
    const dirty = changed.length > 0;

    const stateOf = (id?: string) =>
        availability.find((a) => a.providerId === id)?.state ?? 0;
    const personasOn = (tier: number, id?: string) =>
        assignments.filter(
            (a) =>
                a.tier === tier &&
                a.providerId === id &&
                a.personaId !== NOBODY,
        ).length;
    const nobodys = providers.find(
        (p) =>
            p.id ===
            assignments.find(
                (a) => a.personaId === NOBODY && a.tier === GENERAL,
            )?.providerId,
    );

    const saveAll = async () => {
        try {
            await Promise.all(
                changed.map((p) =>
                    save.mutateAsync({
                        id: p.id ?? '',
                        data: { ...p, share: draft[p.id ?? ''] },
                    }),
                ),
            );
        } catch {
            // save.isError says so; the draft stays for another try.
            return;
        }
        setDraft({});
        onSaved();
    };

    return (
        <section style={SHEET} aria-label="The split">
            <h3 style={{ margin: 0, fontSize: 14, fontWeight: 700 }}>
                The split
            </h3>
            <p
                style={{
                    margin: '2px 0 12px',
                    fontSize: 11,
                    color: INK_MUTED,
                }}
            >
                Each persona stays on one model, so their prompts stay cached
                and their voice stays the same. A share says roughly how many
                personas a model gets; for Decisions, how many of the calls.
            </p>

            <div
                style={{
                    display: 'grid',
                    gridTemplateColumns:
                        'minmax(0, 1fr) minmax(90px, 180px) 34px',
                    gap: '6px 10px',
                    alignItems: 'center',
                    marginBottom: 10,
                }}
            >
                {pool.map((p) => {
                    const id = p.id ?? '';
                    const state = stateOf(p.id);
                    const share = draft[id] ?? shareOf(p);
                    const name = `${shortModel(p)} share`;
                    return (
                        <div key={id} style={{ display: 'contents' }}>
                            <span
                                style={{
                                    display: 'flex',
                                    alignItems: 'center',
                                    gap: 6,
                                    minWidth: 0,
                                    fontSize: 12,
                                }}
                            >
                                <Swatch color={seriesColor(providers, p.id)} />
                                <span
                                    title={p.modelName ?? undefined}
                                    style={{
                                        fontWeight: 600,
                                        overflow: 'hidden',
                                        textOverflow: 'ellipsis',
                                        whiteSpace: 'nowrap',
                                    }}
                                >
                                    {shortModel(p)}
                                </span>
                                <span
                                    style={{
                                        color: INK_MUTED,
                                        fontSize: 11,
                                        flexShrink: 0,
                                    }}
                                >
                                    {providerType(p.type).short}
                                    {state !== 0 &&
                                        ` · ${STATE_ICONS[state]} ${PROVIDER_STATES[state]?.label}`}
                                </span>
                            </span>
                            {shares(p) ? (
                                <>
                                    <input
                                        type="range"
                                        aria-label={name}
                                        min={1}
                                        max={Math.max(100, share)}
                                        value={share}
                                        disabled={save.isPending}
                                        onChange={(e) =>
                                            setDraft((d) => ({
                                                ...d,
                                                [id]: Number(e.target.value),
                                            }))
                                        }
                                        style={{ width: '100%', margin: 0 }}
                                    />
                                    <span
                                        style={{
                                            fontSize: 12,
                                            textAlign: 'right',
                                            fontVariantNumeric: 'tabular-nums',
                                        }}
                                    >
                                        {share}
                                    </span>
                                </>
                            ) : (
                                <span
                                    style={{
                                        gridColumn: 'span 2',
                                        fontSize: 11,
                                        color: INK_MUTED,
                                        textAlign: 'right',
                                    }}
                                >
                                    {JOB_COMPLEXITIES.some(
                                        (tier) =>
                                            splitOf(providers, tier.value)[0]
                                                ?.parts[0].provider.id === p.id,
                                    )
                                        ? 'takes everyone'
                                        : 'only when the others fail'}
                                </span>
                            )}
                        </div>
                    );
                })}
            </div>

            {(dirty || save.isError) && (
                <div
                    style={{
                        display: 'flex',
                        gap: 8,
                        alignItems: 'center',
                        marginBottom: 10,
                    }}
                >
                    <button
                        type="button"
                        onClick={saveAll}
                        disabled={!dirty || save.isPending}
                    >
                        {save.isPending ? 'Saving...' : 'Save split'}
                    </button>
                    <button
                        type="button"
                        onClick={() => setDraft({})}
                        disabled={save.isPending}
                    >
                        Undo
                    </button>
                    <span
                        role={save.isError ? 'alert' : undefined}
                        style={{ fontSize: 11, color: INK_MUTED }}
                    >
                        {save.isError
                            ? "Couldn't save the split. Try again."
                            : 'Only personas whose share moved change model.'}
                    </span>
                </div>
            )}

            {JOB_COMPLEXITIES.map((tier) => {
                const [first, ...later] = splitOf(drafted, tier.value);
                const last =
                    tier.value === GENERAL
                        ? null
                        : tier.value === DECISIONS
                          ? 'LLM emulation on General'
                          : 'General';
                // Decisions have nothing to cache, so calls take turns instead of personas staying put.
                const perCall = tier.value === DECISIONS;
                return (
                    <div
                        key={tier.value}
                        style={{
                            display: 'grid',
                            gridTemplateColumns: '128px minmax(0, 1fr)',
                            gap: 10,
                            padding: '7px 0',
                            borderTop: '1px solid rgba(0,0,0,0.08)',
                        }}
                    >
                        <div>
                            <div style={{ fontSize: 12, fontWeight: 600 }}>
                                {tier.label}
                            </div>
                            <div style={{ fontSize: 10, color: INK_MUTED }}>
                                {tier.hint}
                            </div>
                        </div>
                        <div style={{ minWidth: 0 }}>
                            {first ? (
                                <>
                                    <div
                                        role="img"
                                        aria-label={`${tier.label}: ${first.parts
                                            .map(
                                                (part) =>
                                                    `${shortModel(part.provider)} ${percent(part.fraction)}`,
                                            )
                                            .join(', ')}`}
                                        style={{ display: 'flex', gap: 2 }}
                                    >
                                        {first.parts.map((part, i) => {
                                            const color = seriesColor(
                                                providers,
                                                part.provider.id,
                                            );
                                            const state = stateOf(
                                                part.provider.id,
                                            );
                                            const count = personasOn(
                                                tier.value,
                                                part.provider.id,
                                            );
                                            const ends = `${i === 0 ? '4px' : '0'} ${i === first.parts.length - 1 ? '4px 4px' : '0 0'} ${i === 0 ? '4px' : '0'}`;
                                            return (
                                                <div
                                                    key={part.provider.id}
                                                    title={`${part.provider.modelName ?? part.provider.baseUrl}: ${percent(part.fraction)} of ${perCall ? 'calls' : 'personas'}${dirty || perCall ? '' : `, ${count} now`}${state === 0 ? '' : ` (${PROVIDER_STATES[state]?.label}: the others take its ${perCall ? 'calls' : 'personas'} meanwhile)`}`}
                                                    style={{
                                                        flex: `${part.fraction} 1 0`,
                                                        minWidth: 4,
                                                        height: 16,
                                                        borderRadius: ends,
                                                        background:
                                                            state === OFFLINE
                                                                ? `${HATCH}, ${color}`
                                                                : color,
                                                        opacity:
                                                            state === OFFLINE
                                                                ? 0.55
                                                                : state ===
                                                                    BENCHED
                                                                  ? 0.7
                                                                  : 1,
                                                        color: inkOn(color),
                                                        fontSize: 10,
                                                        fontWeight: 700,
                                                        lineHeight: '16px',
                                                        textAlign: 'center',
                                                        overflow: 'hidden',
                                                        whiteSpace: 'nowrap',
                                                    }}
                                                >
                                                    {part.fraction >= 0.14 &&
                                                        percent(part.fraction)}
                                                </div>
                                            );
                                        })}
                                    </div>
                                    {perCall && first.parts.length > 1 && (
                                        <div
                                            style={{
                                                marginTop: 2,
                                                fontSize: 10,
                                                color: INK_MUTED,
                                                textAlign: 'center',
                                            }}
                                        >
                                            every call takes the next one
                                        </div>
                                    )}
                                    {!perCall &&
                                        !dirty &&
                                        assignments.length > 0 && (
                                            <div
                                                aria-hidden
                                                style={{
                                                    display: 'flex',
                                                    gap: 2,
                                                    marginTop: 2,
                                                    fontSize: 10,
                                                    color: INK_MUTED,
                                                }}
                                            >
                                                {first.parts.map((part) => (
                                                    <span
                                                        key={part.provider.id}
                                                        style={{
                                                            flex: `${part.fraction} 1 0`,
                                                            minWidth: 4,
                                                            textAlign: 'center',
                                                            overflow: 'hidden',
                                                            whiteSpace:
                                                                'nowrap',
                                                        }}
                                                    >
                                                        {part.fraction >=
                                                            0.14 &&
                                                            `${personasOn(tier.value, part.provider.id)} now`}
                                                    </span>
                                                ))}
                                            </div>
                                        )}
                                </>
                            ) : (
                                <div
                                    style={{
                                        fontSize: 11,
                                        lineHeight: '16px',
                                        color:
                                            tier.value === GENERAL
                                                ? '#b3261e'
                                                : INK_MUTED,
                                        fontWeight:
                                            tier.value === GENERAL ? 700 : 400,
                                    }}
                                >
                                    {tier.value === GENERAL
                                        ? '⚠ No model handles this, so personas can’t speak.'
                                        : `No model of its own: uses ${last}.`}
                                </div>
                            )}
                            {first && (later.length > 0 || last) && (
                                <div
                                    style={{
                                        marginTop: 3,
                                        fontSize: 10,
                                        color: INK_MUTED,
                                        display: 'flex',
                                        flexWrap: 'wrap',
                                        gap: '2px 5px',
                                        alignItems: 'center',
                                    }}
                                >
                                    If those fail:
                                    {[
                                        ...later.flatMap((step) =>
                                            step.parts.map((part) => (
                                                <span
                                                    key={part.provider.id}
                                                    style={{
                                                        display: 'inline-flex',
                                                        alignItems: 'center',
                                                        gap: 3,
                                                        textDecoration:
                                                            stateOf(
                                                                part.provider
                                                                    .id,
                                                            ) === OFFLINE
                                                                ? 'line-through'
                                                                : undefined,
                                                    }}
                                                >
                                                    <Swatch
                                                        color={seriesColor(
                                                            providers,
                                                            part.provider.id,
                                                        )}
                                                    />
                                                    {shortModel(part.provider)}
                                                </span>
                                            )),
                                        ),
                                        ...(last
                                            ? [<span key="last">{last}</span>]
                                            : []),
                                    ].flatMap((step, i) =>
                                        i === 0
                                            ? [step]
                                            : [
                                                  <span
                                                      key={`then-${step.key}`}
                                                      aria-hidden
                                                  >
                                                      →
                                                  </span>,
                                                  step,
                                              ],
                                    )}
                                </div>
                            )}
                        </div>
                    </div>
                );
            })}

            {nobodys && (
                <p
                    style={{
                        margin: '8px 0 0',
                        paddingTop: 8,
                        borderTop: '1px solid rgba(0,0,0,0.08)',
                        fontSize: 11,
                        color: INK_MUTED,
                    }}
                >
                    Work that's nobody's own (the narrator, memories, Places)
                    goes to the biggest share: now{' '}
                    <strong style={{ color: '#0b0b0b' }}>
                        {shortModel(nobodys)}
                    </strong>
                    .
                </p>
            )}
        </section>
    );
}
