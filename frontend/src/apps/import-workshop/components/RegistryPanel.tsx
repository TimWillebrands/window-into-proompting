import { useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';
import {
    useGetPersonaSuspense,
    usePostImportIdRegistryCastNameDecision,
    usePutImportIdRegistryCastName,
    usePutImportIdRegistryConceptsName,
    usePutImportIdSettings,
} from '#api/party-zone';
import type {
    ImportConcept,
    ImportRegistryView,
    ImportSettings,
    RegistryCastEntry,
} from '../../../api/model';
import { invalidateImportSession, num } from '../lib/workshop-utils';

/// The registry as suggestions: scene runs propose cast and concepts, the human
/// blesses, reshapes or ignores them. Confirmed entries pin canonical names in later
/// runs and claim participants at commit.
export default function RegistryPanel({
    sessionId,
    registry,
    settings,
}: {
    sessionId: string;
    registry: ImportRegistryView;
    settings: ImportSettings | undefined;
}) {
    const cast = registry.cast ?? [];
    const concepts = registry.concepts ?? [];
    return (
        <div className="flex flex-col gap-3">
            <SettingsSection sessionId={sessionId} settings={settings} />
            <section className="flex flex-col gap-1">
                <span className="font-semibold">
                    Cast{' '}
                    <span style={{ color: '#555', fontWeight: 400 }}>
                        — persona = gets a card and speaks; concept = referenced
                        background character
                    </span>
                </span>
                {cast.length === 0 ? (
                    <p style={{ color: '#555' }}>
                        No cast yet — scene runs propose trait owners and
                        participants here.
                    </p>
                ) : null}
                {cast.map((entry) => (
                    <CastRow
                        key={entry.name}
                        sessionId={sessionId}
                        entry={entry}
                    />
                ))}
            </section>
            <section className="flex flex-col gap-1">
                <span className="font-semibold">
                    Concepts{' '}
                    <span style={{ color: '#555', fontWeight: 400 }}>
                        — recurring named things; confirmed ones become
                        canonical vocabulary
                    </span>
                </span>
                {concepts.length === 0 ? (
                    <p style={{ color: '#555' }}>No concepts yet.</p>
                ) : null}
                {concepts.map((concept) => (
                    <ConceptRow
                        key={concept.name}
                        sessionId={sessionId}
                        concept={concept}
                    />
                ))}
            </section>
        </div>
    );
}

/// Anchor-as-vividness: the band anchor + spacing place imported memories in time, and
/// salience decay makes an older anchor genuinely hazier.
function SettingsSection({
    sessionId,
    settings,
}: {
    sessionId: string;
    settings: ImportSettings | undefined;
}) {
    const queryClient = useQueryClient();
    const [floorDraft, setFloorDraft] = useState<string>(
        String(num(settings?.weightFloor, 0.5)),
    );
    const [anchorDraft, setAnchorDraft] = useState<string>(
        settings?.anchor ? settings.anchor.slice(0, 16) : '',
    );
    const [spacingDraft, setSpacingDraft] = useState<string>(
        String(num(settings?.spacingMinutes, 5)),
    );

    const updateSettings = usePutImportIdSettings({
        mutation: {
            onSuccess: () => invalidateImportSession(queryClient, sessionId),
        },
    });

    return (
        <section className="xp-glass-card flex flex-col gap-1 p-2">
            <span className="font-semibold">Import settings</span>
            <div className="flex flex-wrap items-end gap-2">
                <label className="flex flex-col gap-[2px]">
                    weight floor
                    <input
                        type="number"
                        min={0}
                        max={1}
                        step={0.05}
                        className="w-16"
                        value={floorDraft}
                        onChange={(e) => setFloorDraft(e.target.value)}
                    />
                </label>
                <label className="flex flex-col gap-[2px]">
                    memory anchor
                    <input
                        type="datetime-local"
                        value={anchorDraft}
                        onChange={(e) => setAnchorDraft(e.target.value)}
                    />
                </label>
                <label className="flex flex-col gap-[2px]">
                    spacing (minutes)
                    <input
                        type="number"
                        min={1}
                        className="w-16"
                        value={spacingDraft}
                        onChange={(e) => setSpacingDraft(e.target.value)}
                    />
                </label>
                <button
                    type="button"
                    className="xp-glass-chip cursor-pointer"
                    disabled={updateSettings.isPending}
                    onClick={() =>
                        updateSettings.mutate({
                            id: sessionId,
                            data: {
                                weightFloor:
                                    floorDraft === ''
                                        ? null
                                        : Number(floorDraft),
                                anchor: anchorDraft
                                    ? new Date(anchorDraft).toISOString()
                                    : null,
                                spacingMinutes:
                                    spacingDraft === ''
                                        ? null
                                        : Number(spacingDraft),
                            },
                        })
                    }
                >
                    Apply
                </button>
            </div>
            <p style={{ color: '#555' }}>
                Memories are timestamped from the anchor onwards. An older
                anchor means hazier memories: salience decays with age, so
                personas recall an import anchored last month far more dimly
                than one anchored yesterday.
            </p>
            {updateSettings.isError ? (
                <span style={{ color: '#c00' }}>
                    {(updateSettings.error as Error).message}
                </span>
            ) : null}
        </section>
    );
}

const MATCH_STATE_LABELS: Record<string, { label: string; color: string }> = {
    unmatched: { label: 'no library match', color: '#708090' },
    proposed: { label: 'match proposed', color: '#b8860b' },
    'confirmed-match': { label: 'matched', color: '#2e8b57' },
    'confirmed-mint': { label: 'minting new', color: '#316ac5' },
};

function CastRow({
    sessionId,
    entry,
}: {
    sessionId: string;
    entry: RegistryCastEntry;
}) {
    const queryClient = useQueryClient();
    const [aliasDraft, setAliasDraft] = useState(
        (entry.aliases ?? []).join(', '),
    );
    const [pickerOpen, setPickerOpen] = useState(false);

    const upsert = usePutImportIdRegistryCastName({
        mutation: {
            onSuccess: () => invalidateImportSession(queryClient, sessionId),
        },
    });
    const decide = usePostImportIdRegistryCastNameDecision({
        mutation: {
            onSuccess: () => {
                setPickerOpen(false);
                invalidateImportSession(queryClient, sessionId);
            },
        },
    });

    const name = entry.name ?? '';
    const matchState = MATCH_STATE_LABELS[entry.matchState ?? ''] ?? {
        label: entry.matchState ?? '?',
        color: '#708090',
    };
    const isConcept = entry.routing === 'concept';
    // One line per row stating the commit effect of the current state.
    const effect = isConcept
        ? entry.confirmed
            ? {
                  color: '#2e8b57',
                  text: 'at commit: becomes a Concept — reachable in memories, never speaks, no card needed',
              }
            : {
                  color: '#b8860b',
                  text: '⚠ concept routing only takes effect once confirmed — as-is this still mints a persona (card required)',
              }
        : entry.matchState === 'confirmed-match'
          ? {
                color: '#2e8b57',
                text: `at commit: memories and card updates go to library persona “${entry.proposedPersonaName ?? name}”`,
            }
          : entry.matchState === 'confirmed-mint'
            ? {
                  color: '#316ac5',
                  text: 'at commit: mints a new persona — its card comes from Finalize, review it in Cards',
              }
            : null;
    const saveEdit = (edit: {
        aliases?: string[] | null;
        routing?: string | null;
        confirmed?: boolean | null;
    }) => upsert.mutate({ id: sessionId, name, data: edit });

    return (
        <div className="xp-glass-card flex flex-col gap-1 p-2">
            <div className="flex flex-wrap items-center gap-1">
                <span className="font-semibold">{name}</span>
                <span
                    className="xp-glass-chip"
                    style={{ color: matchState.color }}
                >
                    {matchState.label}
                    {entry.matchState === 'proposed' &&
                    entry.proposedPersonaName
                        ? `: ${entry.proposedPersonaName}?`
                        : ''}
                    {entry.matchState === 'confirmed-match' &&
                    entry.proposedPersonaName
                        ? ` → ${entry.proposedPersonaName}`
                        : ''}
                </span>
                <label
                    className="xp-glass-chip flex items-center gap-1"
                    title="Confirmed entries pin canonical names in later runs and claim participants at commit"
                >
                    <input
                        type="checkbox"
                        checked={entry.confirmed === true}
                        disabled={upsert.isPending}
                        onChange={(e) =>
                            saveEdit({ confirmed: e.target.checked })
                        }
                    />
                    confirmed
                </label>
                <span className="flex-1" />
                <select
                    value={entry.routing ?? 'persona'}
                    disabled={upsert.isPending}
                    title="Persona: dossier'd cast, gets a card. Concept: recurring background character, reachable in memories but never speaks."
                    onChange={(e) => saveEdit({ routing: e.target.value })}
                >
                    <option value="persona">persona</option>
                    <option value="concept">person-as-concept</option>
                </select>
            </div>
            <div className="flex items-center gap-1">
                <span style={{ color: '#555' }}>aliases:</span>
                <input
                    type="text"
                    className="min-w-0 flex-1"
                    placeholder="comma-separated"
                    value={aliasDraft}
                    onChange={(e) => setAliasDraft(e.target.value)}
                    onBlur={() => {
                        const aliases = aliasDraft
                            .split(',')
                            .map((a) => a.trim())
                            .filter(Boolean);
                        if (
                            aliases.join('|') !==
                            (entry.aliases ?? []).join('|')
                        )
                            saveEdit({ aliases });
                    }}
                />
            </div>
            {effect ? (
                <span style={{ color: effect.color }}>{effect.text}</span>
            ) : null}
            {entry.matchState === 'proposed' ? (
                <div className="flex flex-wrap gap-1">
                    <button
                        type="button"
                        className="xp-glass-chip cursor-pointer font-semibold"
                        disabled={decide.isPending}
                        onClick={() =>
                            decide.mutate({
                                id: sessionId,
                                name,
                                data: { decision: 'match' },
                            })
                        }
                    >
                        ✔ Match “{entry.proposedPersonaName}”
                    </button>
                    <button
                        type="button"
                        className="xp-glass-chip cursor-pointer"
                        disabled={decide.isPending}
                        onClick={() =>
                            decide.mutate({
                                id: sessionId,
                                name,
                                data: { decision: 'mint' },
                            })
                        }
                    >
                        ＋ Mint new persona
                    </button>
                </div>
            ) : null}
            {entry.matchState === 'unmatched' && entry.routing !== 'concept' ? (
                <div className="flex flex-wrap items-center gap-1">
                    <span style={{ color: '#555' }}>
                        at commit: mints a new persona (card comes from
                        Finalize) —
                    </span>
                    {pickerOpen ? (
                        <PersonaPicker
                            busy={decide.isPending}
                            onPick={(personaId) =>
                                decide.mutate({
                                    id: sessionId,
                                    name,
                                    data: { decision: 'match', personaId },
                                })
                            }
                            onCancel={() => setPickerOpen(false)}
                        />
                    ) : (
                        <button
                            type="button"
                            className="xp-glass-chip cursor-pointer"
                            title="The matcher misses quoted nicknames — pick the library persona yourself"
                            onClick={() => setPickerOpen(true)}
                        >
                            match to library persona…
                        </button>
                    )}
                </div>
            ) : null}
            {upsert.isError || decide.isError ? (
                <span style={{ color: '#c00' }}>
                    {((upsert.error ?? decide.error) as Error)?.message}
                </span>
            ) : null}
        </div>
    );
}

function PersonaPicker({
    busy,
    onPick,
    onCancel,
}: {
    busy: boolean;
    onPick: (personaId: string) => void;
    onCancel: () => void;
}) {
    const personas = useGetPersonaSuspense().data.data ?? [];
    const [personaId, setPersonaId] = useState('');
    return (
        <span className="flex items-center gap-1">
            <select
                value={personaId}
                onChange={(e) => setPersonaId(e.target.value)}
            >
                <option value="">choose persona…</option>
                {personas.map((persona) => (
                    <option key={persona.id} value={persona.id}>
                        {persona.name}
                    </option>
                ))}
            </select>
            <button
                type="button"
                className="xp-glass-chip cursor-pointer"
                disabled={!personaId || busy}
                onClick={() => personaId && onPick(personaId)}
            >
                Match
            </button>
            <button
                type="button"
                className="xp-glass-chip cursor-pointer"
                onClick={onCancel}
            >
                ✕
            </button>
        </span>
    );
}

function ConceptRow({
    sessionId,
    concept,
}: {
    sessionId: string;
    concept: ImportConcept;
}) {
    const queryClient = useQueryClient();
    const [aliasDraft, setAliasDraft] = useState(
        (concept.aliases ?? []).join(', '),
    );
    const update = usePutImportIdRegistryConceptsName({
        mutation: {
            onSuccess: () => invalidateImportSession(queryClient, sessionId),
        },
    });
    const name = concept.name ?? '';

    return (
        <div className="xp-glass-card flex flex-wrap items-center gap-1 p-2">
            <span className="font-semibold">{name}</span>
            <span className="xp-glass-chip" title="mentions across the draft">
                ×{num(concept.mentions)}
            </span>
            <label
                className="xp-glass-chip flex items-center gap-1"
                title="Confirmed concepts become canonical vocabulary in later runs; unconfirmed ones stay suggestions"
            >
                <input
                    type="checkbox"
                    checked={concept.confirmed === true}
                    disabled={update.isPending}
                    onChange={(e) =>
                        update.mutate({
                            id: sessionId,
                            name,
                            data: { confirmed: e.target.checked },
                        })
                    }
                />
                confirmed
            </label>
            <input
                type="text"
                className="min-w-0 flex-1"
                placeholder="aliases, comma-separated"
                value={aliasDraft}
                onChange={(e) => setAliasDraft(e.target.value)}
                onBlur={() => {
                    const aliases = aliasDraft
                        .split(',')
                        .map((a) => a.trim())
                        .filter(Boolean);
                    if (aliases.join('|') !== (concept.aliases ?? []).join('|'))
                        update.mutate({
                            id: sessionId,
                            name,
                            data: { aliases },
                        });
                }}
            />
            {update.isError ? (
                <span style={{ color: '#c00' }}>
                    {(update.error as Error).message}
                </span>
            ) : null}
        </div>
    );
}
