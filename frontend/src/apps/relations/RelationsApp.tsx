import { useQueryClient } from '@tanstack/react-query';
import { type FormEvent, Suspense, useId, useState } from 'react';
import {
    getGetWorldsPartyIdRelationsQueryKey,
    useDeleteWorldsPartyIdRelationsRelationId,
    useGetWorldsPartyIdLivesSuspense,
    useGetWorldsPartyIdRelationsSuspense,
    usePostWorldsPartyIdRelations,
    usePostWorldsPartyIdRelationsRelationIdEnd,
    usePostWorldsPartyIdRelationsRelationIdHomeNew,
    usePutWorldsPartyIdRelationsRelationIdHome,
} from '#api/party-zone';
import type { Relation } from '../../api/model';
import { RelationKind } from '../../api/model';
import { WORLD_ID } from '../../lib/world';
import { inOrder, isCurrent, KINDS, summary } from './relations-lib';

/** Relations (ADR 0035), author side: what people are to each other, and who lives together. */
export default function RelationsApp() {
    return (
        <div className="app-surface" style={{ padding: 8, overflow: 'auto' }}>
            <Suspense fallback={null}>
                <Book />
            </Suspense>
        </div>
    );
}

function useRelationActions() {
    const queryClient = useQueryClient();
    const mutation = {
        onSuccess: () =>
            queryClient.invalidateQueries({
                queryKey: getGetWorldsPartyIdRelationsQueryKey(WORLD_ID),
            }),
    };
    const declare = usePostWorldsPartyIdRelations({ mutation });
    const end = usePostWorldsPartyIdRelationsRelationIdEnd({ mutation });
    const home = usePutWorldsPartyIdRelationsRelationIdHome({ mutation });
    const forget = useDeleteWorldsPartyIdRelationsRelationId({ mutation });
    const build = usePostWorldsPartyIdRelationsRelationIdHomeNew({ mutation });
    const all = [declare, end, home, forget, build];
    return {
        declare,
        end,
        home,
        forget,
        build,
        isPending: all.some((m) => m.isPending),
        failed: all.some((m) => m.isError),
    };
}

function Book() {
    const lives = useGetWorldsPartyIdLivesSuspense(WORLD_ID).data.data ?? [];
    const relations =
        useGetWorldsPartyIdRelationsSuspense(WORLD_ID).data.data ?? [];
    const actions = useRelationActions();
    const nameOf = (id?: string) =>
        lives.find((l) => l.personaId === id)?.name ?? 'Someone who left';

    return (
        <>
            <NewRelation
                people={lives.map((l) => ({
                    id: l.personaId ?? '',
                    name: l.name ?? '',
                }))}
                actions={actions}
            />
            {actions.failed ? (
                <p style={{ color: '#c00' }}>That did not work.</p>
            ) : null}
            {relations.length === 0 ? (
                <p>Nobody is anything to anybody yet.</p>
            ) : (
                <table style={{ width: '100%' }}>
                    <thead>
                        <tr>
                            <th align="left">Who</th>
                            <th align="left">What</th>
                            <th align="left">Home</th>
                            <th />
                        </tr>
                    </thead>
                    <tbody>
                        {inOrder(relations).map((r) => (
                            <Row
                                key={r.id}
                                relation={r}
                                nameOf={nameOf}
                                actions={actions}
                            />
                        ))}
                    </tbody>
                </table>
            )}
        </>
    );
}

function Row({
    relation,
    nameOf,
    actions,
}: {
    relation: Relation;
    nameOf: (id?: string) => string;
    actions: ReturnType<typeof useRelationActions>;
}) {
    const relationId = relation.id ?? '';
    const first = (id?: string) => nameOf(id).split(' ')[0];
    const moveIn = (hostId: string | null) =>
        actions.home.mutate({
            partyId: WORLD_ID,
            relationId,
            data: { hostId },
        });
    const { isPending } = actions;
    const buildHome = () => {
        const description = prompt(
            `Describe their new home. ${first(relation.aId)} owns it, and the homes they have now are removed.`,
        )?.trim();
        if (description)
            actions.build.mutate({
                partyId: WORLD_ID,
                relationId,
                data: { hostId: relation.aId ?? '', description },
            });
    };

    return (
        <tr>
            <td>
                {nameOf(relation.aId)} &amp; {nameOf(relation.bId)}
            </td>
            <td>{summary(relation)}</td>
            <td>
                {!isCurrent(relation) ? null : relation.liveTogether ? (
                    <button
                        type="button"
                        disabled={isPending}
                        onClick={() => moveIn(null)}
                    >
                        Move apart
                    </button>
                ) : (
                    <>
                        <button
                            type="button"
                            disabled={isPending}
                            onClick={() => moveIn(relation.aId ?? '')}
                        >
                            At {first(relation.aId)}'s
                        </button>{' '}
                        <button
                            type="button"
                            disabled={isPending}
                            onClick={() => moveIn(relation.bId ?? '')}
                        >
                            At {first(relation.bId)}'s
                        </button>
                    </>
                )}
            </td>
            <td align="right">
                {isCurrent(relation) ? (
                    <button
                        type="button"
                        disabled={isPending}
                        onClick={buildHome}
                    >
                        New home
                    </button>
                ) : null}{' '}
                {isCurrent(relation) ? (
                    <button
                        type="button"
                        disabled={isPending}
                        onClick={() =>
                            actions.end.mutate({
                                partyId: WORLD_ID,
                                relationId,
                            })
                        }
                    >
                        End
                    </button>
                ) : null}{' '}
                <button
                    type="button"
                    disabled={isPending}
                    onClick={() => {
                        if (confirm('Strike this, as if it never was?'))
                            actions.forget.mutate({
                                partyId: WORLD_ID,
                                relationId,
                            });
                    }}
                >
                    Forget
                </button>
            </td>
        </tr>
    );
}

function NewRelation({
    people,
    actions,
}: {
    people: { id: string; name: string }[];
    actions: ReturnType<typeof useRelationActions>;
}) {
    const [aId, setAId] = useState('');
    const [bId, setBId] = useState('');
    const [kind, setKind] = useState<RelationKind>(RelationKind.Together);
    const [story, setStory] = useState('');
    const [past, setPast] = useState(false);
    const pastId = useId();
    const ready = !!aId && !!bId && aId !== bId;

    const submit = (e: FormEvent) => {
        e.preventDefault();
        if (!ready) return;
        actions.declare.mutate(
            {
                partyId: WORLD_ID,
                data: { aId, bId, kind, story: story.trim() || null, past },
            },
            {
                onSuccess: () => {
                    setStory('');
                    setPast(false);
                },
            },
        );
    };

    const pick = (value: string, set: (id: string) => void, label: string) => (
        <select
            aria-label={label}
            value={value}
            onChange={(e) => set(e.target.value)}
        >
            <option value="">{label}</option>
            {people.map((p) => (
                <option key={p.id} value={p.id}>
                    {p.name}
                </option>
            ))}
        </select>
    );

    return (
        <form onSubmit={submit}>
            <fieldset className="border border-[#9db2c8] p-2 m-0">
                <legend className="text-xp-legend font-semibold text-[11px] px-1">
                    What are they to each other?
                </legend>
                <div
                    className="flex items-center"
                    style={{ gap: 6, flexWrap: 'wrap' }}
                >
                    {pick(aId, setAId, 'Someone')}
                    {pick(bId, setBId, 'Someone else')}
                    <select
                        aria-label="Kind"
                        value={kind}
                        onChange={(e) =>
                            setKind(e.target.value as RelationKind)
                        }
                    >
                        {KINDS.map((k) => (
                            <option key={k.kind} value={k.kind}>
                                {k.label}
                            </option>
                        ))}
                    </select>
                    <input
                        type="text"
                        aria-label="Their story"
                        placeholder="you dated in high school"
                        value={story}
                        maxLength={200}
                        style={{ flex: 1, minWidth: 180 }}
                        onChange={(e) => setStory(e.target.value)}
                    />
                    <input
                        id={pastId}
                        type="checkbox"
                        checked={past}
                        onChange={(e) => setPast(e.target.checked)}
                    />
                    <label htmlFor={pastId}>In the past</label>
                    <button
                        type="submit"
                        disabled={!ready || actions.isPending}
                    >
                        Add
                    </button>
                </div>
            </fieldset>
        </form>
    );
}
