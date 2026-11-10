import { useState } from 'react';
import { usePutLlmConfigPersonaRoutesPersonaId } from '#api/party-zone';
import type { LlmProviderEntry } from '../../../api/model/llmProviderEntry';
import type { ModelAssignment } from '../../../api/model/modelAssignment';
import type { Persona } from '../../../api/model/persona';
import type { PersonaRoute } from '../../../api/model/personaRoute';
import {
    DECISIONS,
    GENERAL,
    INK_MUTED,
    inputStyle,
    JOB_COMPLEXITIES,
    providerLabel,
    SHEET,
    seriesColor,
    shortModel,
} from './constants';
import { NOBODY } from './ModelSplit';

const rule = '1px solid rgba(0,0,0,0.08)';
const td: React.CSSProperties = { padding: '3px 6px 3px 0', borderTop: rule };

const cellBase: React.CSSProperties = {
    display: 'block',
    width: '100%',
    minWidth: 0,
    height: 24,
    padding: '0 4px',
    fontSize: 11,
    boxSizing: 'border-box',
    borderRadius: 4,
    overflow: 'hidden',
    textOverflow: 'ellipsis',
    whiteSpace: 'nowrap',
};

/** What a tier without a model of its own falls back to. */
const fallbackOf = (tier: number) =>
    tier === GENERAL
        ? 'no model'
        : tier === DECISIONS
          ? 'emulation'
          : 'General';

/** One persona's model for one kind of work: the split's pick, or a pin. */
function PinCell({
    label,
    tier,
    providers,
    pinnedId,
    lands,
    disabled,
    onPin,
}: {
    label: string;
    tier: number;
    providers: LlmProviderEntry[];
    pinnedId?: string;
    lands?: LlmProviderEntry;
    disabled: boolean;
    onPin: (providerId: string | undefined) => void;
}) {
    const pinned = providers.find((p) => p.id === pinnedId);
    const away = pinned && pinned.id !== lands?.id;
    const landing = lands ? shortModel(lands) : fallbackOf(tier);
    // A decisions-only model can't speak; a disabled one stays listed only while chosen.
    const options = providers.filter(
        (p) =>
            p.id === pinnedId ||
            (p.isEnabled !== false &&
                (tier === DECISIONS || p.supportedComplexities !== DECISIONS)),
    );
    const option = (p: LlmProviderEntry) => (
        <option key={p.id} value={p.id ?? ''}>
            {p.id === pinnedId
                ? `📌 ${shortModel(p)}${away ? ` (now ${landing})` : ''}`
                : providerLabel(p)}
        </option>
    );
    const reserved = options.filter((p) => p.reserved);
    const split = options.filter((p) => !p.reserved);

    return (
        <select
            aria-label={label}
            title={
                away
                    ? `Pinned to ${shortModel(pinned)}, which can't be used right now. On ${landing} until it's back.`
                    : pinned
                      ? `Pinned to ${pinned.modelName}`
                      : `Follows the split: ${lands?.modelName ?? landing}`
            }
            value={pinnedId ?? ''}
            disabled={disabled}
            onChange={(e) => onPin(e.target.value || undefined)}
            style={{
                ...cellBase,
                border: `1px solid ${away ? '#c98500' : pinned ? '#7F9DB9' : 'rgba(0,0,0,0.12)'}`,
                borderLeft: `4px solid ${seriesColor(providers, lands?.id)}`,
                background: pinned ? '#fff' : 'transparent',
                color: pinned ? '#0b0b0b' : INK_MUTED,
                fontWeight: pinned ? 700 : 400,
            }}
        >
            <option value="">{pinned ? 'Follow the split' : landing}</option>
            {reserved.length > 0 && (
                <optgroup label="Only for pinned personas">
                    {reserved.map(option)}
                </optgroup>
            )}
            {split.length > 0 && (
                <optgroup label="In the split">{split.map(option)}</optgroup>
            )}
        </select>
    );
}

/** Who is on which model per kind of work; a cell pins that work to one model (a Persona Route). */
export default function PersonaMatrix({
    personas,
    providers,
    routes,
    assignments,
    onChanged,
}: {
    personas: Persona[];
    providers: LlmProviderEntry[];
    routes: PersonaRoute[];
    assignments: ModelAssignment[];
    onChanged: () => void;
}) {
    const save = usePutLlmConfigPersonaRoutesPersonaId();
    const [query, setQuery] = useState('');
    const [pinnedOnly, setPinnedOnly] = useState(false);

    const routesOf = (personaId?: string) =>
        routes.filter((r) => r.personaId === personaId);
    const landsOn = (personaId: string, tier: number) =>
        providers.find(
            (p) =>
                p.id ===
                assignments.find(
                    (a) => a.personaId === personaId && a.tier === tier,
                )?.providerId,
        );
    const pinnedCount = new Set(routes.map((r) => r.personaId)).size;
    const shown = personas.filter(
        (p) =>
            p.name?.toLowerCase().includes(query.trim().toLowerCase()) &&
            (!pinnedOnly || routesOf(p.id).length > 0),
    );

    const put = (personaId: string, data: PersonaRoute[]) =>
        save.mutate({ personaId, data }, { onSuccess: onChanged });
    const busy = (personaId?: string) =>
        save.isPending && save.variables?.personaId === personaId;

    return (
        <section style={SHEET} aria-label="Personas">
            <div
                style={{
                    display: 'flex',
                    alignItems: 'baseline',
                    gap: 8,
                    flexWrap: 'wrap',
                }}
            >
                <h3 style={{ margin: 0, fontSize: 14, fontWeight: 700 }}>
                    Personas
                </h3>
                <span style={{ fontSize: 11, color: INK_MUTED }}>
                    {pinnedCount === 0
                        ? 'Everyone follows the split.'
                        : `${pinnedCount} of ${personas.length} ${pinnedCount === 1 ? 'has' : 'have'} a model of their own.`}
                </span>
                {save.isError && (
                    <span
                        role="alert"
                        style={{ fontSize: 11, color: '#b3261e' }}
                    >
                        Couldn't save that change. Try again.
                    </span>
                )}
            </div>
            <p
                style={{
                    margin: '2px 0 10px',
                    fontSize: 11,
                    color: INK_MUTED,
                }}
            >
                Pick a model in a cell to pin that kind of work for one persona;
                📌 marks a pin.
            </p>

            <div
                style={{
                    display: 'flex',
                    gap: 10,
                    alignItems: 'center',
                    marginBottom: 8,
                }}
            >
                <input
                    type="search"
                    aria-label="Find a persona"
                    placeholder="Find a persona..."
                    value={query}
                    onChange={(e) => setQuery(e.target.value)}
                    style={{ ...inputStyle, width: 180, padding: '4px 8px' }}
                />
                <label
                    style={{
                        fontSize: 11,
                        display: 'flex',
                        alignItems: 'center',
                        gap: 4,
                        cursor: 'pointer',
                    }}
                >
                    <input
                        type="checkbox"
                        checked={pinnedOnly}
                        onChange={(e) => setPinnedOnly(e.target.checked)}
                    />
                    Only pinned
                </label>
            </div>

            <div style={{ overflowX: 'auto' }}>
                <table
                    style={{
                        width: '100%',
                        minWidth: 540,
                        tableLayout: 'fixed',
                        borderCollapse: 'collapse',
                    }}
                >
                    <thead>
                        <tr
                            style={{
                                fontSize: 11,
                                color: INK_MUTED,
                                textAlign: 'left',
                            }}
                        >
                            <th style={{ width: 132, paddingBottom: 4 }}>
                                Persona
                            </th>
                            {JOB_COMPLEXITIES.map((tier) => (
                                <th
                                    key={tier.value}
                                    title={tier.hint}
                                    style={{ paddingBottom: 4 }}
                                >
                                    {tier.label.replace('Character ', '')}
                                </th>
                            ))}
                            <th style={{ width: 24 }} />
                        </tr>
                    </thead>
                    <tbody>
                        {shown.map((persona) => {
                            const personaId = persona.id ?? '';
                            const mine = routesOf(personaId);
                            return (
                                <tr
                                    key={personaId}
                                    style={{
                                        opacity: busy(personaId) ? 0.6 : 1,
                                    }}
                                >
                                    <th
                                        scope="row"
                                        title={persona.name ?? undefined}
                                        style={{
                                            ...td,
                                            fontSize: 12,
                                            fontWeight: 600,
                                            textAlign: 'left',
                                            overflow: 'hidden',
                                            textOverflow: 'ellipsis',
                                            whiteSpace: 'nowrap',
                                        }}
                                    >
                                        {persona.name}
                                    </th>
                                    {JOB_COMPLEXITIES.map((tier) => (
                                        <td key={tier.value} style={td}>
                                            <PinCell
                                                label={`${persona.name}: ${tier.label}`}
                                                tier={tier.value}
                                                providers={providers}
                                                pinnedId={
                                                    mine.find(
                                                        (r) =>
                                                            r.tier ===
                                                            tier.value,
                                                    )?.providerId
                                                }
                                                lands={landsOn(
                                                    personaId,
                                                    tier.value,
                                                )}
                                                disabled={busy(personaId)}
                                                onPin={(providerId) => {
                                                    const others = mine.filter(
                                                        (r) =>
                                                            r.tier !==
                                                            tier.value,
                                                    );
                                                    put(
                                                        personaId,
                                                        providerId
                                                            ? [
                                                                  ...others,
                                                                  {
                                                                      personaId,
                                                                      tier: tier.value,
                                                                      providerId,
                                                                  },
                                                              ]
                                                            : others,
                                                    );
                                                }}
                                            />
                                        </td>
                                    ))}
                                    <td style={{ ...td, paddingRight: 0 }}>
                                        {mine.length > 0 && (
                                            <button
                                                type="button"
                                                className="xp-bare"
                                                aria-label={`Put ${persona.name} back on the split`}
                                                title="Back to the split"
                                                disabled={busy(personaId)}
                                                onClick={() =>
                                                    put(personaId, [])
                                                }
                                                style={{
                                                    cursor: 'pointer',
                                                    fontSize: 13,
                                                    color: INK_MUTED,
                                                }}
                                            >
                                                ↺
                                            </button>
                                        )}
                                    </td>
                                </tr>
                            );
                        })}

                        {shown.length === 0 && (
                            <tr>
                                <td
                                    colSpan={6}
                                    style={{
                                        ...td,
                                        padding: '10px 0',
                                        fontSize: 12,
                                        color: INK_MUTED,
                                    }}
                                >
                                    {personas.length === 0
                                        ? 'No personas yet.'
                                        : pinnedOnly && query.trim() === ''
                                          ? 'Nobody is pinned: everyone follows the split.'
                                          : 'No persona matches that.'}
                                </td>
                            </tr>
                        )}

                        {!pinnedOnly && query.trim() === '' && (
                            <tr
                                title="The narrator, memories and Places aren't one persona's work. They go to the biggest share."
                                style={{ color: INK_MUTED }}
                            >
                                <th
                                    scope="row"
                                    style={{
                                        ...td,
                                        fontSize: 12,
                                        fontWeight: 400,
                                        fontStyle: 'italic',
                                        textAlign: 'left',
                                    }}
                                >
                                    Nobody's own
                                </th>
                                {JOB_COMPLEXITIES.map((tier) => {
                                    const lands = landsOn(NOBODY, tier.value);
                                    return (
                                        <td key={tier.value} style={td}>
                                            <span
                                                style={{
                                                    ...cellBase,
                                                    lineHeight: '22px',
                                                    padding: '0 8px',
                                                    border: '1px solid transparent',
                                                    borderLeft: `4px solid ${seriesColor(providers, lands?.id)}`,
                                                }}
                                            >
                                                {lands
                                                    ? shortModel(lands)
                                                    : fallbackOf(tier.value)}
                                            </span>
                                        </td>
                                    );
                                })}
                                <td style={td} />
                            </tr>
                        )}
                    </tbody>
                </table>
            </div>
        </section>
    );
}
