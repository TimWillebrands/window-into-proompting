import {
    ComboBox,
    CommandButton,
    GroupBox,
    ListView,
    type ListViewColumn,
    Stack,
    TextBox,
    WindowLayout,
} from '@partytown/ui';
import { useQueryClient } from '@tanstack/react-query';
import { useId, useState } from 'react';
import {
    getGetWorldsPartyIdGatheringsQueryKey,
    useGetWorldsPartyIdGatherings,
    useGetWorldsPartyIdLivesSuspense,
    useGetWorldsPartyIdPlaces,
    usePostWorldsPartyIdGatherings,
    usePostWorldsPartyIdGatheringsGatheringIdRsvp,
} from '#api/party-zone';
import type { Gathering } from '../../api/model/gathering';
import { usePlayerId, WORLD_ID } from '../../lib/world';

const time = (ts: number | string | undefined) =>
    ts == null
        ? ''
        : new Date(Number(ts)).toLocaleString([], {
              weekday: 'short',
              hour: '2-digit',
              minute: '2-digit',
          });

const whereOf = (g: Gathering) =>
    g.placeId ? (g.where ?? `at ${g.roomName}`) : '—';

const outcomeOf = (g: Gathering) => {
    if (g.status === 'Cancelled')
        return `Cancelled — ${g.cancelledBecause ?? 'called off'}`;
    if (g.status === 'Missed') return 'missed, the world was asleep';
    return `${g.attendeeIds?.length ?? 0} said they'd come`;
};

const upcomingColumns: ListViewColumn[] = [
    { key: 'title', header: 'Gathering', width: 'minmax(140px, 2fr)' },
    { key: 'when', header: 'When', width: 'minmax(96px, auto)' },
    { key: 'where', header: 'Where', width: 'minmax(96px, 1fr)' },
    { key: 'host', header: 'Host', width: 'minmax(80px, 1fr)' },
    { key: 'who', header: "Who's coming", width: 'minmax(120px, 2fr)' },
    { key: 'rsvp', header: '', width: 'max-content', align: 'end' },
];

const pastColumns: ListViewColumn[] = [
    { key: 'title', header: 'Gathering', width: 'minmax(140px, 2fr)' },
    { key: 'when', header: 'When', width: 'minmax(96px, auto)' },
    { key: 'where', header: 'Where', width: 'minmax(96px, 1fr)' },
    {
        key: 'outcome',
        header: 'How it went',
        width: 'minmax(140px, 2fr)',
        wrap: true,
    },
];

const startsIn = (g: Gathering, now: number) => {
    const mins = Math.round((Number(g.startsAt) - now) / 60000);
    return mins < 60
        ? `in ${Math.max(mins, 0)} min`
        : `in ${Math.round(mins / 60)} h`;
};

export default function CalendarApp() {
    const playerId = usePlayerId();
    const lives = useGetWorldsPartyIdLivesSuspense(WORLD_ID).data.data ?? [];
    const nameOf = (id: string) =>
        lives.find((l) => l.personaId === id)?.name ?? 'someone';
    const gatherings = useGetWorldsPartyIdGatherings(WORLD_ID).data?.data ?? [];
    const now = Date.now();
    const upcoming = gatherings.filter((g) => g.status === 'Planned');
    const past = gatherings
        .filter((g) => g.status !== 'Planned')
        .sort((a, b) => Number(b.startsAt) - Number(a.startsAt))
        .slice(0, 10);

    return (
        <WindowLayout scroll="none">
            <Stack gap="md" pad="md">
                <PlanForm playerId={playerId} />
                <GroupBox title="Coming up" fill>
                    <ListView
                        label="Coming up"
                        columns={upcomingColumns}
                        selectionMode="none"
                        empty="Nothing planned. Personas plan get-togethers on their own — or plan one above."
                        items={upcoming.map((g) => ({
                            id: g.id ?? '',
                            label: `“${g.title}”`,
                            cells: {
                                when: `${time(g.startsAt)}, ${startsIn(g, now)}`,
                                where: whereOf(g),
                                host: g.hostName,
                                who: (
                                    <span
                                        title={(g.attendeeIds ?? [])
                                            .map(nameOf)
                                            .join(', ')}
                                    >
                                        {(g.attendeeIds ?? [])
                                            .map(nameOf)
                                            .join(', ')}
                                    </span>
                                ),
                                rsvp: <Rsvp g={g} playerId={playerId} />,
                            },
                        }))}
                    />
                </GroupBox>
                <GroupBox title="Recently">
                    <ListView
                        label="Recently"
                        className="max-h-[110px]"
                        columns={pastColumns}
                        selectionMode="none"
                        empty="—"
                        items={past.map((g) => ({
                            id: g.id ?? '',
                            label: `“${g.title}”`,
                            tone:
                                g.status === 'Cancelled'
                                    ? ('caution' as const)
                                    : undefined,
                            cells: {
                                when: time(g.startsAt),
                                where: whereOf(g),
                                outcome: outcomeOf(g),
                            },
                        }))}
                    />
                </GroupBox>
            </Stack>
        </WindowLayout>
    );
}

function Rsvp({ g, playerId }: { g: Gathering; playerId: string | null }) {
    const queryClient = useQueryClient();
    const rsvp = usePostWorldsPartyIdGatheringsGatheringIdRsvp({
        mutation: {
            onSuccess: () =>
                queryClient.invalidateQueries({
                    queryKey: getGetWorldsPartyIdGatheringsQueryKey(WORLD_ID),
                }),
        },
    });
    const going = !!playerId && (g.attendeeIds ?? []).includes(playerId);
    return (
        <CommandButton
            size="small"
            tone={going ? 'caution' : 'go'}
            disabled={!playerId || rsvp.isPending}
            onClick={() =>
                rsvp.mutate({
                    partyId: WORLD_ID,
                    gatheringId: g.id ?? '',
                    data: { personaId: playerId, going: !going },
                })
            }
        >
            {going ? "Can't make it" : "I'll be there"}
        </CommandButton>
    );
}

function PlanForm({ playerId }: { playerId: string | null }) {
    const queryClient = useQueryClient();
    const ids = useId();
    const places = (
        useGetWorldsPartyIdPlaces(WORLD_ID).data?.data.places ?? []
    ).filter((p) => p.kind === 'public');
    const [title, setTitle] = useState('');
    const [placeId, setPlaceId] = useState('');
    const [minutes, setMinutes] = useState('30');
    const plan = usePostWorldsPartyIdGatherings({
        mutation: {
            onSuccess: () => {
                setTitle('');
                queryClient.invalidateQueries({
                    queryKey: getGetWorldsPartyIdGatheringsQueryKey(WORLD_ID),
                });
            },
        },
    });
    const place = placeId || places[0]?.id || '';
    return (
        <Stack
            as="form"
            direction="row"
            wrap
            align="center"
            gap="sm"
            onSubmit={(e) => {
                e.preventDefault();
                if (!title.trim() || !place) return;
                plan.mutate({
                    partyId: WORLD_ID,
                    data: {
                        title: title.trim(),
                        placeId: place,
                        startsAt:
                            Date.now() +
                            Math.max(1, Number(minutes) || 30) * 60000,
                        hostId: playerId,
                    },
                });
            }}
        >
            <label htmlFor={`${ids}-t`}>Plan:</label>
            <TextBox
                id={`${ids}-t`}
                className="min-w-[140px] flex-1"
                value={title}
                maxLength={120}
                placeholder="Movie night, storm stories, …"
                onChange={(e) => setTitle(e.currentTarget.value)}
            />
            <label htmlFor={`${ids}-r`}>at</label>
            <ComboBox
                id={`${ids}-r`}
                value={place}
                disabled={places.length === 0}
                onChange={(e) => setPlaceId(e.currentTarget.value)}
            >
                {places.length > 0 ? (
                    places.map((p) => (
                        <option key={p.id} value={p.id ?? ''}>
                            {p.name}
                        </option>
                    ))
                ) : (
                    <option value="">No public Places yet</option>
                )}
            </ComboBox>
            <label htmlFor={`${ids}-m`}>starts in</label>
            <TextBox
                id={`${ids}-m`}
                className="w-[64px]"
                type="number"
                min={1}
                value={minutes}
                onChange={(e) => setMinutes(e.currentTarget.value)}
            />
            min
            <CommandButton
                type="submit"
                isDefault
                disabled={!title.trim() || !place || plan.isPending}
            >
                Plan it
            </CommandButton>
        </Stack>
    );
}
