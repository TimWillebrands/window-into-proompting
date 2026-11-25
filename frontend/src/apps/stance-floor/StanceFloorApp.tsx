import {
    ComboBox,
    CommandButton,
    ControlGrid,
    FieldRow,
    GroupBox,
    Icon,
    Meter,
    ProgressBar,
    RadioButton,
    RadioGroup,
    Slider,
    Stack,
    StatusText,
    TaskPane,
    TaskPaneSection,
    Text,
    TextArea,
    TextBox,
    WindowLayout,
} from '@partytown/ui';
import { useQueryClient } from '@tanstack/react-query';
import { useEffect, useMemo, useState } from 'react';
import {
    getGetPartiesPartyIdMemoryParticipantsPersonaIdStancesQueryKey,
    useGetPartiesPartyIdMemoryParticipantsPersonaIdStances,
    useGetPersona,
    usePostPartiesPartyIdMemoryParticipantsPersonaIdConsolidate,
    usePostPartiesPartyIdMemoryParticipantsPersonaIdStances,
    usePostPartiesPartyIdMemoryParticipantsPersonaIdStancesStanceIdRetract,
} from '#api/party-zone';
import { StanceOrigin, StanceTargetKind } from '../../api/model';
import { WORLD_ID } from '../../lib/world';

export default function StanceFloorApp() {
    const partyId = WORLD_ID;
    const personasQuery = useGetPersona();
    const personas = useMemo(() => {
        const data = personasQuery.data?.data;
        return Array.isArray(data) ? data.filter((p) => p.id && p.name) : [];
    }, [personasQuery.data]);

    const [sourceId, setSourceId] = useState<string | null>(null);

    const personaNameById = useMemo(
        () => new Map(personas.map((p) => [p.id as string, p.name as string])),
        [personas],
    );

    return (
        <WindowLayout
            taskPaneLabel="Stance Floor"
            taskPaneWidth="wide"
            contentLabel="Stances"
            taskPane={
                <TaskPane>
                    <TaskPaneSection
                        title="Whose stance?"
                        special
                        collapsible={false}
                    >
                        <ComboBox
                            aria-label="Whose stance?"
                            value={sourceId ?? ''}
                            onChange={(e) =>
                                setSourceId(e.target.value || null)
                            }
                            options={[
                                { value: '', label: '— pick a persona —' },
                                ...personas.map((p) => ({
                                    value: p.id as string,
                                    label: p.name,
                                })),
                            ]}
                        />
                        {sourceId ? null : (
                            <Text as="p" tone="muted">
                                Pick the persona who holds the stance to begin.
                            </Text>
                        )}
                    </TaskPaneSection>

                    {sourceId ? (
                        <>
                            <ConsolidatePanel
                                partyId={partyId}
                                sourceId={sourceId}
                            />
                            <StanceAuthor
                                partyId={partyId}
                                sourceId={sourceId}
                                personas={personas.map((p) => ({
                                    id: p.id as string,
                                    name: p.name as string,
                                }))}
                            />
                        </>
                    ) : null}
                </TaskPane>
            }
        >
            {sourceId ? (
                <Stack pad="md">
                    <StanceLog
                        partyId={partyId}
                        sourceId={sourceId}
                        personaNameById={personaNameById}
                    />
                </Stack>
            ) : (
                <Stack align="center" justify="center" pad="md">
                    <GroupBox
                        title="Stances are how a persona feels"
                        icon={<Icon name="scales" />}
                        className="max-w-[340px]"
                    >
                        <Text as="p" tone="muted">
                            Pick a persona on the left to see its opinions about
                            other participants, concepts, and itself — and to
                            author or retract them.
                        </Text>
                    </GroupBox>
                </Stack>
            )}
        </WindowLayout>
    );
}

function ConsolidatePanel({
    partyId,
    sourceId,
}: {
    partyId: string;
    sourceId: string;
}) {
    const queryClient = useQueryClient();
    const [report, setReport] = useState<string | null>(null);

    const consolidate =
        usePostPartiesPartyIdMemoryParticipantsPersonaIdConsolidate({
            mutation: {
                onSuccess: async (res) => {
                    const r = res.data;
                    setReport(
                        !r || !('runId' in r)
                            ? 'Consolidation failed — see logs.'
                            : r.skipped
                              ? 'A run is already in flight — try again shortly.'
                              : `Walked ${r.recollectionsWalked} recollection(s), appended ${r.stancesAppended} stance(s).`,
                    );
                    await queryClient.invalidateQueries({
                        queryKey:
                            getGetPartiesPartyIdMemoryParticipantsPersonaIdStancesQueryKey(
                                partyId,
                                sourceId,
                            ),
                    });
                },
                onError: () => setReport('Consolidation failed — see logs.'),
            },
        });

    // biome-ignore lint/correctness/useExhaustiveDependencies: stale report belongs to the previous persona
    useEffect(() => setReport(null), [sourceId]);

    return (
        <TaskPaneSection title="Consolidation">
            <CommandButton
                title="Walk this persona's unconsolidated recollections (one LLM call) and append the stances they crystallise into"
                disabled={consolidate.isPending}
                onClick={() =>
                    consolidate.mutate({ partyId, personaId: sourceId })
                }
            >
                {consolidate.isPending ? 'Sleeping on it…' : 'Consolidate now'}
            </CommandButton>
            {report ? (
                <Text as="p" tone="muted">
                    {report}
                </Text>
            ) : null}
        </TaskPaneSection>
    );
}

function StanceAuthor({
    partyId,
    sourceId,
    personas,
}: {
    partyId: string;
    sourceId: string;
    personas: { id: string; name: string }[];
}) {
    const queryClient = useQueryClient();
    const [targetKind, setTargetKind] = useState<StanceTargetKind>(
        StanceTargetKind.Participant,
    );
    const [targetPersonaId, setTargetPersonaId] = useState<string>('');
    const [conceptName, setConceptName] = useState<string>('');
    const [valence, setValence] = useState<number>(0);
    const [reasoning, setReasoning] = useState<string>('');
    const [error, setError] = useState<string | null>(null);

    // biome-ignore lint/correctness/useExhaustiveDependencies: sourceId triggers the reset; setters are stable
    useEffect(() => {
        setTargetPersonaId('');
        setConceptName('');
    }, [sourceId]);

    const others = personas.filter((p) => p.id !== sourceId);

    const append = usePostPartiesPartyIdMemoryParticipantsPersonaIdStances({
        mutation: {
            onSuccess: async () => {
                setReasoning('');
                setError(null);
                await queryClient.invalidateQueries({
                    queryKey:
                        getGetPartiesPartyIdMemoryParticipantsPersonaIdStancesQueryKey(
                            partyId,
                            sourceId,
                        ),
                });
            },
            onError: () =>
                setError('Append failed — check valence and reasoning.'),
        },
    });

    const canSubmit =
        reasoning.trim().length > 0 &&
        (targetKind !== StanceTargetKind.Participant ||
            targetPersonaId.length > 0) &&
        (targetKind !== StanceTargetKind.Concept ||
            conceptName.trim().length > 0);

    function submit() {
        if (!canSubmit) return;
        append.mutate({
            partyId,
            personaId: sourceId,
            data: {
                valence,
                reasoning: reasoning.trim(),
                targetKind,
                targetPersonaId:
                    targetKind === StanceTargetKind.Participant
                        ? targetPersonaId
                        : null,
                targetConceptName:
                    targetKind === StanceTargetKind.Concept
                        ? conceptName.trim()
                        : null,
            },
        });
    }

    return (
        <TaskPaneSection title="Toward…">
            <RadioGroup
                name="stance-target"
                aria-label="Toward…"
                value={targetKind}
                onValueChange={(v) => setTargetKind(v as StanceTargetKind)}
            >
                <RadioButton value={StanceTargetKind.Participant}>
                    A participant
                </RadioButton>
                <RadioButton value={StanceTargetKind.Concept}>
                    A concept
                </RadioButton>
                <RadioButton value={StanceTargetKind.Self}>
                    Themselves
                </RadioButton>
            </RadioGroup>

            {targetKind === StanceTargetKind.Participant ? (
                <ComboBox
                    aria-label="Participant"
                    value={targetPersonaId}
                    onChange={(e) => setTargetPersonaId(e.target.value)}
                    options={[
                        { value: '', label: '— pick a participant —' },
                        ...others.map((p) => ({ value: p.id, label: p.name })),
                    ]}
                />
            ) : null}

            {targetKind === StanceTargetKind.Concept ? (
                <TextBox
                    aria-label="Concept"
                    placeholder="Concept (e.g. Lisp)"
                    value={conceptName}
                    onChange={(e) => setConceptName(e.target.value)}
                />
            ) : null}

            <FieldRow
                label={`Valence (${valence.toFixed(2)})`}
                layout="stacked"
            >
                <Slider
                    min={-1}
                    max={1}
                    step={0.05}
                    value={valence}
                    startLabel="−1 hostile"
                    endLabel="+1 warm"
                    onChange={(e) => setValence(Number(e.target.value))}
                    className="w-full"
                />
            </FieldRow>

            <FieldRow label="Reasoning (second person)" layout="stacked">
                <TextArea
                    rows={3}
                    placeholder="Vlad exaggerates — you've watched him inflate three stories"
                    value={reasoning}
                    onChange={(e) => setReasoning(e.target.value)}
                    maxLength={600}
                />
            </FieldRow>

            <CommandButton
                isDefault
                type="button"
                disabled={!canSubmit || append.isPending}
                onClick={submit}
            >
                {append.isPending ? 'Appending…' : 'Append stance'}
            </CommandButton>
            {error ? <StatusText tone="danger">{error}</StatusText> : null}
        </TaskPaneSection>
    );
}

function StanceLog({
    partyId,
    sourceId,
    personaNameById,
}: {
    partyId: string;
    sourceId: string;
    personaNameById: Map<string, string>;
}) {
    const queryClient = useQueryClient();
    const stancesQuery = useGetPartiesPartyIdMemoryParticipantsPersonaIdStances(
        partyId,
        sourceId,
    );
    const stances = useMemo(() => {
        const data = stancesQuery.data?.data;
        return Array.isArray(data) ? data : [];
    }, [stancesQuery.data]);

    const retract =
        usePostPartiesPartyIdMemoryParticipantsPersonaIdStancesStanceIdRetract({
            mutation: {
                onSuccess: async () => {
                    await queryClient.invalidateQueries({
                        queryKey:
                            getGetPartiesPartyIdMemoryParticipantsPersonaIdStancesQueryKey(
                                partyId,
                                sourceId,
                            ),
                    });
                },
            },
        });

    if (stancesQuery.isLoading) {
        return (
            <Stack gap="md">
                <ProgressBar marquee label="Loading stances" className="w-40" />
                <Text as="p" tone="muted">
                    Loading stances…
                </Text>
            </Stack>
        );
    }
    if (stances.length === 0) {
        return (
            <Text as="p" tone="muted">
                This persona holds no stances yet. Author one on the left, or
                run Consolidation to crystallise them from its recollections.
            </Text>
        );
    }

    const current = (
        <StatusText as="span" icon={<Icon name="check" />}>
            current
        </StatusText>
    );

    return (
        <Stack gap="sm">
            <Text as="p" tone="muted">
                Append-only log — the latest stance per target counts (marked{' '}
                {current}); older entries stay for history.
            </Text>
            <ControlGrid
                label="Stances"
                minWidth="760px"
                columns={[
                    { key: 'target', header: 'Target', width: '140px' },
                    { key: 'valence', header: 'Valence', width: '110px' },
                    { key: 'reasoning', header: 'Reasoning' },
                    { key: 'by', header: 'By', width: '140px', nowrap: true },
                    {
                        key: 'when',
                        header: 'When',
                        width: '150px',
                        nowrap: true,
                    },
                    { key: 'retract', header: '', width: '84px', align: 'end' },
                ]}
                rows={stances.map((s) => ({
                    id: s.id,
                    muted: !s.isCurrent,
                    cells: {
                        target: (
                            <Stack
                                as="span"
                                direction="row"
                                wrap
                                align="center"
                                gap="xs"
                            >
                                {targetLabel(s, personaNameById)}
                                {s.isCurrent ? current : null}
                            </Stack>
                        ),
                        valence: (
                            <Stack
                                as="span"
                                direction="row"
                                align="center"
                                gap="sm"
                                title="−1 hostile · 0 neutral · +1 warm"
                            >
                                <Meter
                                    diverging
                                    value={Number(s.valence)}
                                    label={`Valence ${Number(s.valence).toFixed(2)}`}
                                    className="w-12"
                                />
                                {Number(s.valence).toFixed(2)}
                            </Stack>
                        ),
                        reasoning: (
                            <Text as="span" size="reading">
                                {s.reasoning}
                            </Text>
                        ),
                        by: <OriginBadge stance={s} />,
                        when: new Date(s.ts).toLocaleString(),
                        retract:
                            s.isCurrent && s.origin !== StanceOrigin.Retract ? (
                                <CommandButton
                                    size="small"
                                    disabled={retract.isPending}
                                    title="Append a neutralizing edge — history stays"
                                    onClick={() =>
                                        retract.mutate({
                                            partyId,
                                            personaId: sourceId,
                                            stanceId: s.id,
                                        })
                                    }
                                >
                                    Retract
                                </CommandButton>
                            ) : null,
                    },
                }))}
            />
        </Stack>
    );
}

function OriginBadge({
    stance,
}: {
    stance: {
        origin?: StanceOrigin;
        runId?: string | null;
        retractsId?: string | null;
    };
}) {
    switch (stance.origin) {
        case StanceOrigin.Consolidation:
            return (
                <span title={`run ${stance.runId ?? '?'}`}>
                    consolidation{' '}
                    <Text as="span" tone="muted">
                        ({(stance.runId ?? '').slice(0, 8)})
                    </Text>
                </span>
            );
        case StanceOrigin.Retract:
            return (
                <span title={`masks ${stance.retractsId ?? '?'}`}>
                    retract{' '}
                    <Text as="span" tone="muted">
                        (⊘{(stance.retractsId ?? '').slice(0, 8)})
                    </Text>
                </span>
            );
        default:
            return <span>curator</span>;
    }
}

function targetLabel(
    s: {
        targetKind: StanceTargetKind;
        targetConceptDisplay: string | null;
        targetConceptName: string | null;
        targetPersonaId: string | null;
    },
    personaNameById: Map<string, string>,
): string {
    switch (s.targetKind) {
        case StanceTargetKind.Self:
            return 'self';
        case StanceTargetKind.Concept:
            return `#${s.targetConceptDisplay ?? s.targetConceptName ?? '?'}`;
        default: {
            const id = s.targetPersonaId ?? '';
            return `@${personaNameById.get(id) ?? id.slice(0, 8)}`;
        }
    }
}
