import {
    CheckBox,
    ComboBox,
    CommandButton,
    ControlGrid,
    GroupBox,
    ListView,
    Stack,
    StatusText,
    Text,
    TextBox,
    WindowLayout,
} from '@partytown/ui';
import { useQueryClient } from '@tanstack/react-query';
import { type FormEvent, Suspense, useState } from 'react';
import {
    getGetWorldsPartyIdRelationsQueryKey,
    useDeleteWorldsPartyIdRelationsRelationId,
    useGetWorldsPartyIdLivesSuspense,
    useGetWorldsPartyIdRelationsDoorsSuspense,
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

export default function RelationsApp() {
    return (
        <WindowLayout contentLabel="Relations" scroll="both">
            <Stack gap="md" pad="md">
                <Suspense fallback={null}>
                    <Book />
                    <Doors />
                </Suspense>
            </Stack>
        </WindowLayout>
    );
}

function Doors() {
    const doors =
        useGetWorldsPartyIdRelationsDoorsSuspense(WORLD_ID).data.data ?? [];
    if (doors.length === 0) return null;
    return (
        <GroupBox title="At the door">
            <ListView
                view="details"
                label="At the door"
                selectionMode="none"
                columns={[
                    { key: 'label', header: 'Place' },
                    { key: 'who', header: 'Who' },
                    { key: 'week', header: 'This week' },
                    { key: 'wants', header: 'Wants' },
                ]}
                items={doors.map((d) => ({
                    id: `${d.place}:${d.visitor}`,
                    label: d.place,
                    cells: {
                        who: d.visitor,
                        week: d.hasKey ? 'has a key' : d.doorsteps,
                        wants: [d.visitorWant, d.hostWant]
                            .filter(Boolean)
                            .join(' / '),
                    },
                }))}
            />
        </GroupBox>
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
                <StatusText tone="danger">That did not work.</StatusText>
            ) : null}
            {relations.length === 0 ? (
                <Text as="p" tone="muted">
                    Nobody is anything to anybody yet.
                </Text>
            ) : (
                <ControlGrid
                    label="Relations"
                    columns={[
                        { key: 'who', header: 'Who' },
                        { key: 'what', header: 'What' },
                        { key: 'home', header: 'Home' },
                        { key: 'actions', header: '', align: 'end' },
                    ]}
                    rows={inOrder(relations).map((r) => ({
                        id: r.id ?? '',
                        cells: rowCells(r, nameOf, actions),
                    }))}
                />
            )}
        </>
    );
}

function rowCells(
    relation: Relation,
    nameOf: (id?: string) => string,
    actions: ReturnType<typeof useRelationActions>,
) {
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

    return {
        who: (
            <>
                {nameOf(relation.aId)} &amp; {nameOf(relation.bId)}
            </>
        ),
        what: summary(relation),
        home: !isCurrent(relation) ? null : relation.liveTogether ? (
            <CommandButton
                size="small"
                disabled={isPending}
                onClick={() => moveIn(null)}
            >
                Move apart
            </CommandButton>
        ) : (
            <Stack as="span" direction="row" wrap gap="xs">
                <CommandButton
                    size="small"
                    disabled={isPending}
                    onClick={() => moveIn(relation.aId ?? '')}
                >
                    At {first(relation.aId)}'s
                </CommandButton>
                <CommandButton
                    size="small"
                    disabled={isPending}
                    onClick={() => moveIn(relation.bId ?? '')}
                >
                    At {first(relation.bId)}'s
                </CommandButton>
            </Stack>
        ),
        actions: (
            <Stack as="span" direction="row" wrap justify="end" gap="xs">
                {isCurrent(relation) ? (
                    <CommandButton
                        size="small"
                        disabled={isPending}
                        onClick={buildHome}
                    >
                        New home
                    </CommandButton>
                ) : null}
                {isCurrent(relation) ? (
                    <CommandButton
                        size="small"
                        tone="caution"
                        disabled={isPending}
                        onClick={() =>
                            actions.end.mutate({
                                partyId: WORLD_ID,
                                relationId,
                            })
                        }
                    >
                        End
                    </CommandButton>
                ) : null}
                <CommandButton
                    size="small"
                    tone="danger"
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
                </CommandButton>
            </Stack>
        ),
    };
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
        <ComboBox
            aria-label={label}
            value={value}
            onChange={(e) => set(e.target.value)}
            options={[
                { value: '', label },
                ...people.map((p) => ({ value: p.id, label: p.name })),
            ]}
        />
    );

    return (
        <form onSubmit={submit}>
            <GroupBox title="What are they to each other?">
                <Stack direction="row" wrap align="center" gap="sm">
                    {pick(aId, setAId, 'Someone')}
                    {pick(bId, setBId, 'Someone else')}
                    <ComboBox
                        aria-label="Kind"
                        value={kind}
                        onChange={(e) =>
                            setKind(e.target.value as RelationKind)
                        }
                        options={KINDS.map((k) => ({
                            value: k.kind,
                            label: k.label,
                        }))}
                    />
                    <TextBox
                        aria-label="Their story"
                        placeholder="you dated in high school"
                        value={story}
                        maxLength={200}
                        className="min-w-[180px] flex-1"
                        onChange={(e) => setStory(e.target.value)}
                    />
                    <CheckBox
                        checked={past}
                        onChange={(e) => setPast(e.target.checked)}
                    >
                        In the past
                    </CheckBox>
                    <CommandButton
                        isDefault
                        disabled={!ready || actions.isPending}
                    >
                        Add
                    </CommandButton>
                </Stack>
            </GroupBox>
        </form>
    );
}
