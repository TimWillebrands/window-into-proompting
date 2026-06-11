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
import { ROOT_PARTY_ID } from '../../lib/chat-api';

/**
 * Stance Floor — the curator's debug surface for ADR 0016. Author an Acquired Stance from
 * one Persona toward another Participant, a Concept, or themselves, and watch the append-only
 * log: every write adds an edge, latest-wins per target (the current one is highlighted), and
 * history stays visible. Consolidation v1 (#89) added the run button (sleep on the
 * unconsolidated Recollections now), per-edge attribution (who wrote it, under which run),
 * and one-click retract — a neutralizing append, never a delete.
 */
export default function StanceFloorApp() {
    const partyId = ROOT_PARTY_ID;
    const personasQuery = useGetPersona();
    const personas = useMemo(() => {
        const data = personasQuery.data?.data;
        return Array.isArray(data) ? data.filter((p) => p.id && p.name) : [];
    }, [personasQuery.data]);

    const [sourceId, setSourceId] = useState<string | null>(null);

    return (
        <div
            className="app-surface flex h-full"
            style={{ background: '#ECE9D8', fontSize: 12 }}
        >
            {/* Author column */}
            <div
                className="flex flex-col"
                style={{
                    width: 280,
                    borderRight: '1px solid #ACA899',
                    padding: 8,
                    gap: 8,
                    overflowY: 'auto',
                }}
            >
                <SectionTitle>Whose stance?</SectionTitle>
                <select
                    value={sourceId ?? ''}
                    onChange={(e) => setSourceId(e.target.value || null)}
                >
                    <option value="">— pick a persona —</option>
                    {personas.map((p) => (
                        <option key={p.id} value={p.id as string}>
                            {p.name}
                        </option>
                    ))}
                </select>

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
                ) : (
                    <p style={{ color: '#808080' }}>
                        Pick the persona who holds the stance to begin.
                    </p>
                )}
            </div>

            {/* Stance log */}
            <div className="flex-1" style={{ overflowY: 'auto', padding: 8 }}>
                {sourceId ? (
                    <StanceLog partyId={partyId} sourceId={sourceId} />
                ) : (
                    <div
                        className="h-full flex items-center justify-center"
                        style={{ color: '#808080' }}
                    >
                        <p>Select a persona to see where they stand.</p>
                    </div>
                )}
            </div>
        </div>
    );
}

/**
 * Curator trigger for Consolidation v1: one click walks the persona's unconsolidated
 * Recollections (one LLM call, in rest) and auto-appends the crystallised Stances — they
 * show up in the log with a `consolidation` badge, retractable like anything else.
 */
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
                    // 200 carries the run result; the error shape (ProblemDetails) only
                    // reaches onSuccess in theory, but the union forces the narrowing.
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
        <>
            <SectionTitle>Consolidation</SectionTitle>
            <button
                type="button"
                disabled={consolidate.isPending}
                onClick={() =>
                    consolidate.mutate({ partyId, personaId: sourceId })
                }
            >
                {consolidate.isPending ? 'Sleeping on it…' : 'Consolidate now'}
            </button>
            {report ? (
                <p style={{ color: '#555', margin: 0 }}>{report}</p>
            ) : null}
        </>
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
        <>
            <SectionTitle>Toward…</SectionTitle>
            <div style={{ display: 'flex', flexDirection: 'column', gap: 2 }}>
                <KindRadio
                    kind={StanceTargetKind.Participant}
                    current={targetKind}
                    onPick={setTargetKind}
                    label="A participant"
                />
                <KindRadio
                    kind={StanceTargetKind.Concept}
                    current={targetKind}
                    onPick={setTargetKind}
                    label="A concept"
                />
                <KindRadio
                    kind={StanceTargetKind.Self}
                    current={targetKind}
                    onPick={setTargetKind}
                    label="Themselves"
                />
            </div>

            {targetKind === StanceTargetKind.Participant ? (
                <select
                    value={targetPersonaId}
                    onChange={(e) => setTargetPersonaId(e.target.value)}
                >
                    <option value="">— pick a participant —</option>
                    {others.map((p) => (
                        <option key={p.id} value={p.id}>
                            {p.name}
                        </option>
                    ))}
                </select>
            ) : null}

            {targetKind === StanceTargetKind.Concept ? (
                <input
                    type="text"
                    placeholder="Concept (e.g. Lisp)"
                    value={conceptName}
                    onChange={(e) => setConceptName(e.target.value)}
                />
            ) : null}

            <SectionTitle>Valence ({valence.toFixed(2)})</SectionTitle>
            <input
                type="range"
                min={-1}
                max={1}
                step={0.05}
                value={valence}
                onChange={(e) => setValence(Number(e.target.value))}
            />
            <div
                style={{
                    display: 'flex',
                    justifyContent: 'space-between',
                    color: '#808080',
                    fontSize: 10,
                }}
            >
                <span>−1 hostile</span>
                <span>+1 warm</span>
            </div>

            <SectionTitle>Reasoning (second person)</SectionTitle>
            <textarea
                rows={3}
                placeholder="Vlad exaggerates — you've watched him inflate three stories"
                value={reasoning}
                onChange={(e) => setReasoning(e.target.value)}
                maxLength={600}
            />

            <button
                type="button"
                disabled={!canSubmit || append.isPending}
                onClick={submit}
            >
                {append.isPending ? 'Appending…' : 'Append stance'}
            </button>
            {error ? <p style={{ color: '#c00' }}>{error}</p> : null}
        </>
    );
}

function StanceLog({
    partyId,
    sourceId,
}: {
    partyId: string;
    sourceId: string;
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
        return <p style={{ color: '#808080' }}>Loading stances…</p>;
    }
    if (stances.length === 0) {
        return (
            <p style={{ color: '#808080' }}>
                No stances yet. Author one on the left.
            </p>
        );
    }

    return (
        <table style={{ width: '100%', borderCollapse: 'collapse' }}>
            <thead>
                <tr style={{ textAlign: 'left', color: '#555' }}>
                    <th style={th}>Target</th>
                    <th style={th}>Valence</th>
                    <th style={th}>Reasoning</th>
                    <th style={th}>By</th>
                    <th style={th}>When</th>
                    <th style={th} />
                </tr>
            </thead>
            <tbody>
                {stances.map((s) => (
                    <tr
                        key={s.id}
                        style={{
                            background: s.isCurrent ? '#FFFFFF' : 'transparent',
                            color: s.isCurrent ? '#000' : '#999',
                            borderBottom: '1px solid #D6D2C2',
                        }}
                    >
                        <td style={td}>
                            {targetLabel(s)}
                            {s.isCurrent ? (
                                <span
                                    style={{
                                        marginLeft: 4,
                                        fontSize: 9,
                                        background: '#316AC5',
                                        color: '#fff',
                                        borderRadius: 6,
                                        padding: '0 4px',
                                    }}
                                >
                                    current
                                </span>
                            ) : null}
                        </td>
                        <td style={td}>{Number(s.valence).toFixed(2)}</td>
                        <td style={td}>{s.reasoning}</td>
                        <td style={{ ...td, whiteSpace: 'nowrap' }}>
                            <OriginBadge stance={s} />
                        </td>
                        <td style={{ ...td, whiteSpace: 'nowrap' }}>
                            {new Date(s.ts).toLocaleString()}
                        </td>
                        <td style={{ ...td, whiteSpace: 'nowrap' }}>
                            {s.isCurrent &&
                            s.origin !== StanceOrigin.Retract ? (
                                <button
                                    type="button"
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
                                </button>
                            ) : null}
                        </td>
                    </tr>
                ))}
            </tbody>
        </table>
    );
}

/**
 * Attribution per ADR 0016: every append is logged with its writer. Consolidation edges
 * carry the run id that walked the recollections; retract edges point at what they mask.
 */
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
                    <span style={{ color: '#808080' }}>
                        ({(stance.runId ?? '').slice(0, 8)})
                    </span>
                </span>
            );
        case StanceOrigin.Retract:
            return (
                <span title={`masks ${stance.retractsId ?? '?'}`}>
                    retract{' '}
                    <span style={{ color: '#808080' }}>
                        (⊘{(stance.retractsId ?? '').slice(0, 8)})
                    </span>
                </span>
            );
        default:
            return <span>curator</span>;
    }
}

function targetLabel(s: {
    targetKind: StanceTargetKind;
    targetConceptDisplay: string | null;
    targetConceptName: string | null;
    targetPersonaId: string | null;
}): string {
    switch (s.targetKind) {
        case StanceTargetKind.Self:
            return 'self';
        case StanceTargetKind.Concept:
            return `#${s.targetConceptDisplay ?? s.targetConceptName ?? '?'}`;
        default:
            // Persona display-name enrichment is out of scope for the debug surface; the id
            // tail is enough to disambiguate targets at a glance.
            return `@${(s.targetPersonaId ?? '').slice(0, 8)}`;
    }
}

function KindRadio({
    kind,
    current,
    onPick,
    label,
}: {
    kind: StanceTargetKind;
    current: StanceTargetKind;
    onPick: (k: StanceTargetKind) => void;
    label: string;
}) {
    return (
        <label style={{ display: 'flex', alignItems: 'center', gap: 4 }}>
            <input
                type="radio"
                checked={current === kind}
                onChange={() => onPick(kind)}
            />
            {label}
        </label>
    );
}

function SectionTitle({ children }: { children: React.ReactNode }) {
    return (
        <div style={{ fontWeight: 700, color: '#003399', marginTop: 4 }}>
            {children}
        </div>
    );
}

const th: React.CSSProperties = {
    padding: '2px 6px',
    borderBottom: '1px solid #ACA899',
    fontWeight: 600,
};
const td: React.CSSProperties = {
    padding: '3px 6px',
    verticalAlign: 'top',
};
