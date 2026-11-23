import {
    CheckBox,
    ComboBox,
    ControlGrid,
    type ControlGridColumn,
    GroupBox,
    Icon,
    StatusText,
    Swatch,
    Text,
    TextBox,
    ToolbarButton,
} from '@partytown/ui';
import { useState } from 'react';
import { usePutLlmConfigPersonaRoutesPersonaId } from '#api/party-zone';
import type { LlmProviderEntry } from '../../../api/model/llmProviderEntry';
import type { ModelAssignment } from '../../../api/model/modelAssignment';
import type { Persona } from '../../../api/model/persona';
import type { PersonaRoute } from '../../../api/model/personaRoute';
import {
    DECISIONS,
    GENERAL,
    JOB_COMPLEXITIES,
    providerLabel,
    seriesColor,
    shortModel,
} from './constants';
import { NOBODY } from './ModelSplit';

const TAKES_TURNS = 'takes turns';

const fallbackOf = (tier: number) =>
    tier === GENERAL
        ? 'no model'
        : tier === DECISIONS
          ? 'emulation'
          : 'General';

function PinCell({
    label,
    tier,
    providers,
    pinnedId,
    lands,
    balanced,
    disabled,
    onPin,
}: {
    label: string;
    tier: number;
    providers: LlmProviderEntry[];
    pinnedId?: string;
    lands?: LlmProviderEntry;
    balanced?: boolean;
    disabled: boolean;
    onPin: (providerId: string | undefined) => void;
}) {
    const pinned = providers.find((p) => p.id === pinnedId);
    const away = pinned && pinned.id !== lands?.id;
    const landing = lands
        ? shortModel(lands)
        : balanced
          ? TAKES_TURNS
          : fallbackOf(tier);
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
        <span className="flex min-w-0 items-center gap-1">
            <Swatch size="small" colour={seriesColor(providers, lands?.id)} />
            <ComboBox
                aria-label={label}
                className="min-w-0 flex-1"
                title={
                    away
                        ? `Pinned to ${shortModel(pinned)}, which can't be used right now. On ${landing} until it's back.`
                        : pinned
                          ? `Pinned to ${pinned.modelName}`
                          : balanced
                            ? 'Every call takes the next decision model, by share'
                            : `Follows the split: ${lands?.modelName ?? landing}`
                }
                value={pinnedId ?? ''}
                disabled={disabled}
                onChange={(e) => onPin(e.target.value || undefined)}
            >
                <option value="">
                    {pinned ? 'Follow the split' : landing}
                </option>
                {reserved.length > 0 && (
                    <optgroup label="Only for pinned personas">
                        {reserved.map(option)}
                    </optgroup>
                )}
                {split.length > 0 && (
                    <optgroup label="In the split">
                        {split.map(option)}
                    </optgroup>
                )}
            </ComboBox>
            {away && <Icon name="warning" />}
        </span>
    );
}

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
    const assigned = (personaId: string, tier: number) =>
        assignments.find((a) => a.personaId === personaId && a.tier === tier);
    const landsOn = (personaId: string, tier: number) =>
        providers.find((p) => p.id === assigned(personaId, tier)?.providerId);
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

    const showNobody = !pinnedOnly && query.trim() === '';
    const columns: ControlGridColumn[] = [
        {
            key: 'persona',
            header: 'Persona',
            width: '132px',
            rowHeader: true,
            nowrap: true,
        },
        ...JOB_COMPLEXITIES.map((tier) => ({
            key: String(tier.value),
            header: tier.label.replace('Character ', ''),
            title: tier.hint,
        })),
        { key: 'reset', header: '', width: '36px', align: 'end' },
    ];

    return (
        <GroupBox title="Personas">
            <div className="flex flex-wrap items-baseline gap-2">
                <Text as="span" tone="muted">
                    {pinnedCount === 0
                        ? 'Everyone follows the split.'
                        : `${pinnedCount} of ${personas.length} ${pinnedCount === 1 ? 'has' : 'have'} a model of their own.`}
                </Text>
                {save.isError && (
                    <StatusText as="span" tone="danger" role="alert">
                        Couldn't save that change. Try again.
                    </StatusText>
                )}
            </div>
            <Text as="p" tone="muted">
                Pick a model in a cell to pin that kind of work for one persona;
                📌 marks a pin.
            </Text>

            <div className="flex items-center gap-3">
                <TextBox
                    type="search"
                    aria-label="Find a persona"
                    placeholder="Find a persona..."
                    value={query}
                    onChange={(e) => setQuery(e.target.value)}
                    className="w-44"
                />
                <CheckBox
                    checked={pinnedOnly}
                    onChange={(e) => setPinnedOnly(e.target.checked)}
                >
                    Only pinned
                </CheckBox>
            </div>

            {shown.length === 0 && (
                <Text as="p" tone="muted">
                    {personas.length === 0
                        ? 'No personas yet.'
                        : pinnedOnly && query.trim() === ''
                          ? 'Nobody is pinned: everyone follows the split.'
                          : 'No persona matches that.'}
                </Text>
            )}

            <ControlGrid
                label="Personas"
                minWidth="540px"
                columns={columns}
                rows={[
                    ...shown.map((persona) => {
                        const personaId = persona.id ?? '';
                        const mine = routesOf(personaId);
                        return {
                            id: personaId,
                            busy: busy(personaId),
                            cells: {
                                persona: (
                                    <span title={persona.name ?? undefined}>
                                        {persona.name}
                                    </span>
                                ),
                                ...Object.fromEntries(
                                    JOB_COMPLEXITIES.map((tier) => [
                                        String(tier.value),
                                        <PinCell
                                            key={tier.value}
                                            label={`${persona.name}: ${tier.label}`}
                                            tier={tier.value}
                                            providers={providers}
                                            pinnedId={
                                                mine.find(
                                                    (r) =>
                                                        r.tier === tier.value,
                                                )?.providerId
                                            }
                                            lands={landsOn(
                                                personaId,
                                                tier.value,
                                            )}
                                            balanced={
                                                assigned(personaId, tier.value)
                                                    ?.balanced
                                            }
                                            disabled={busy(personaId)}
                                            onPin={(providerId) => {
                                                const others = mine.filter(
                                                    (r) =>
                                                        r.tier !== tier.value,
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
                                        />,
                                    ]),
                                ),
                                reset: mine.length > 0 && (
                                    <ToolbarButton
                                        icon={<Icon name="undo" />}
                                        aria-label={`Put ${persona.name} back on the split`}
                                        title="Back to the split"
                                        disabled={busy(personaId)}
                                        onClick={() => put(personaId, [])}
                                    />
                                ),
                            },
                        };
                    }),
                    ...(showNobody
                        ? [
                              {
                                  id: NOBODY,
                                  muted: true,
                                  title: "The narrator, memories and Places aren't one persona's work. They go to the biggest share.",
                                  cells: {
                                      persona: "Nobody's own",
                                      ...Object.fromEntries(
                                          JOB_COMPLEXITIES.map((tier) => {
                                              const lands = landsOn(
                                                  NOBODY,
                                                  tier.value,
                                              );
                                              return [
                                                  String(tier.value),
                                                  <span
                                                      key={tier.value}
                                                      className="flex min-w-0 items-center gap-1"
                                                  >
                                                      <Swatch
                                                          size="small"
                                                          colour={seriesColor(
                                                              providers,
                                                              lands?.id,
                                                          )}
                                                      />
                                                      <span className="truncate">
                                                          {lands
                                                              ? shortModel(
                                                                    lands,
                                                                )
                                                              : assigned(
                                                                      NOBODY,
                                                                      tier.value,
                                                                  )?.balanced
                                                                ? TAKES_TURNS
                                                                : fallbackOf(
                                                                      tier.value,
                                                                  )}
                                                      </span>
                                                  </span>,
                                              ];
                                          }),
                                      ),
                                  },
                              },
                          ]
                        : []),
                ]}
            />
        </GroupBox>
    );
}
