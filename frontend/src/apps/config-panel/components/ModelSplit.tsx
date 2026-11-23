import {
    CommandButton,
    GroupBox,
    ShareBar,
    Slider,
    StatusText,
    Swatch,
    Text,
} from '@partytown/ui';
import { useState } from 'react';
import { usePutLlmConfigProvidersId } from '#api/party-zone';
import type { LlmProviderEntry } from '../../../api/model/llmProviderEntry';
import type { ModelAssignment } from '../../../api/model/modelAssignment';
import type { ProviderAvailability } from '../../../api/model/providerAvailability';
import {
    BENCHED,
    DECISIONS,
    GENERAL,
    JOB_COMPLEXITIES,
    OFFLINE,
    PROVIDER_STATES,
    providerType,
    seriesColor,
    shortModel,
} from './constants';
import { percent, shareOf, splitOf } from './split';

export const NOBODY = '00000000-0000-0000-0000-000000000000';

const stateOfShare = (state: number) =>
    state === OFFLINE ? 'off' : state === BENCHED ? 'dim' : 'normal';

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
            return;
        }
        setDraft({});
        onSaved();
    };

    return (
        <GroupBox title="The split">
            <Text as="p" tone="muted">
                Each persona stays on one model, so their prompts stay cached
                and their voice stays the same. A share says roughly how many
                personas a model gets; for Decisions, how many of the calls.
            </Text>

            <div className="grid grid-cols-[minmax(0,1fr)_minmax(90px,180px)_34px] items-center gap-x-3 gap-y-1.5">
                {pool.map((p) => {
                    const id = p.id ?? '';
                    const state = stateOf(p.id);
                    const share = draft[id] ?? shareOf(p);
                    const name = `${shortModel(p)} share`;
                    return (
                        <div key={id} className="contents">
                            <span className="flex min-w-0 items-center gap-1.5">
                                <Swatch colour={seriesColor(providers, p.id)} />
                                <strong
                                    className="truncate"
                                    title={p.modelName ?? undefined}
                                >
                                    {shortModel(p)}
                                </strong>
                                <Text
                                    as="span"
                                    tone="muted"
                                    className="flex-none"
                                >
                                    {providerType(p.type).short}
                                </Text>
                                {state !== 0 && (
                                    <StatusText
                                        as="span"
                                        className="flex-none"
                                        tone={
                                            PROVIDER_STATES[state]?.tone ??
                                            'caution'
                                        }
                                    >
                                        {PROVIDER_STATES[state]?.label}
                                    </StatusText>
                                )}
                            </span>
                            {shares(p) ? (
                                <>
                                    <Slider
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
                                        className="w-full"
                                    />
                                    <span className="text-end">{share}</span>
                                </>
                            ) : (
                                <Text
                                    as="span"
                                    tone="muted"
                                    className="col-span-2 text-end"
                                >
                                    {JOB_COMPLEXITIES.some(
                                        (tier) =>
                                            splitOf(providers, tier.value)[0]
                                                ?.parts[0].provider.id === p.id,
                                    )
                                        ? 'takes everyone'
                                        : 'only when the others fail'}
                                </Text>
                            )}
                        </div>
                    );
                })}
            </div>

            {(dirty || save.isError) && (
                <div className="flex flex-wrap items-center gap-2">
                    <CommandButton
                        isDefault
                        type="button"
                        onClick={saveAll}
                        disabled={!dirty || save.isPending}
                    >
                        {save.isPending ? 'Saving...' : 'Save split'}
                    </CommandButton>
                    <CommandButton
                        onClick={() => setDraft({})}
                        disabled={save.isPending}
                    >
                        Undo
                    </CommandButton>
                    {save.isError ? (
                        <StatusText as="span" tone="danger" role="alert">
                            Couldn't save the split. Try again.
                        </StatusText>
                    ) : (
                        <Text as="span" tone="muted">
                            Only personas whose share moved change model.
                        </Text>
                    )}
                </div>
            )}

            <div className="grid grid-cols-[128px_minmax(0,1fr)] gap-x-3 gap-y-3">
                {JOB_COMPLEXITIES.map((tier) => {
                    const [first, ...later] = splitOf(drafted, tier.value);
                    const last =
                        tier.value === GENERAL
                            ? null
                            : tier.value === DECISIONS
                              ? 'LLM emulation on General'
                              : 'General';
                    const perCall = tier.value === DECISIONS;
                    const captions =
                        !perCall && !dirty && assignments.length > 0;
                    return (
                        <div key={tier.value} className="contents">
                            <div className="flex flex-col">
                                <strong>{tier.label}</strong>
                                <Text as="p" tone="muted">
                                    {tier.hint}
                                </Text>
                            </div>
                            <div className="flex min-w-0 flex-col gap-1">
                                {first ? (
                                    <>
                                        <ShareBar
                                            label={`${tier.label}: ${first.parts
                                                .map(
                                                    (part) =>
                                                        `${shortModel(part.provider)} ${percent(part.fraction)}`,
                                                )
                                                .join(', ')}`}
                                            segments={first.parts.map(
                                                (part) => {
                                                    const state = stateOf(
                                                        part.provider.id,
                                                    );
                                                    const count = personasOn(
                                                        tier.value,
                                                        part.provider.id,
                                                    );
                                                    const wide =
                                                        part.fraction >= 0.14;
                                                    return {
                                                        key:
                                                            part.provider.id ??
                                                            '',
                                                        share: part.fraction,
                                                        colour: seriesColor(
                                                            providers,
                                                            part.provider.id,
                                                        ),
                                                        state: stateOfShare(
                                                            state,
                                                        ),
                                                        label: wide
                                                            ? percent(
                                                                  part.fraction,
                                                              )
                                                            : undefined,
                                                        caption:
                                                            captions && wide
                                                                ? `${count} now`
                                                                : captions
                                                                  ? ''
                                                                  : undefined,
                                                        title: `${part.provider.modelName ?? part.provider.baseUrl}: ${percent(part.fraction)} of ${perCall ? 'calls' : 'personas'}${dirty || perCall ? '' : `, ${count} now`}${state === 0 ? '' : ` (${PROVIDER_STATES[state]?.label}: the others take its ${perCall ? 'calls' : 'personas'} meanwhile)`}`,
                                                    };
                                                },
                                            )}
                                        />
                                        {perCall && first.parts.length > 1 && (
                                            <Text
                                                as="p"
                                                tone="muted"
                                                className="text-center"
                                            >
                                                every call takes the next one
                                            </Text>
                                        )}
                                    </>
                                ) : tier.value === GENERAL ? (
                                    <StatusText tone="danger">
                                        No model handles this, so personas can’t
                                        speak.
                                    </StatusText>
                                ) : (
                                    <Text as="p" tone="muted">
                                        No model of its own: uses {last}.
                                    </Text>
                                )}
                                {first && (later.length > 0 || last) && (
                                    <Text
                                        as="div"
                                        tone="muted"
                                        className="flex flex-wrap items-center gap-x-1 gap-y-0.5"
                                    >
                                        If those fail:
                                        {[
                                            ...later.flatMap((step) =>
                                                step.parts.map((part) => (
                                                    <span
                                                        key={part.provider.id}
                                                        className="inline-flex items-center gap-1"
                                                    >
                                                        <Swatch
                                                            size="small"
                                                            colour={seriesColor(
                                                                providers,
                                                                part.provider
                                                                    .id,
                                                            )}
                                                        />
                                                        {stateOf(
                                                            part.provider.id,
                                                        ) === OFFLINE ? (
                                                            <s>
                                                                {shortModel(
                                                                    part.provider,
                                                                )}
                                                            </s>
                                                        ) : (
                                                            shortModel(
                                                                part.provider,
                                                            )
                                                        )}
                                                    </span>
                                                )),
                                            ),
                                            ...(last
                                                ? [
                                                      <span key="last">
                                                          {last}
                                                      </span>,
                                                  ]
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
                                    </Text>
                                )}
                            </div>
                        </div>
                    );
                })}
            </div>

            {nobodys && (
                <Text as="p" tone="muted">
                    Work that's nobody's own (the narrator, memories, Places)
                    goes to the biggest share: now{' '}
                    <strong>{shortModel(nobodys)}</strong>.
                </Text>
            )}
        </GroupBox>
    );
}
