import {
    ComboBox,
    CommandButton,
    GroupBox,
    ListView,
    type ListViewColumn,
    TextBox,
} from '@partytown/ui';
import { useQueryClient } from '@tanstack/react-query';
import { useId, useState } from 'react';
import {
    getGetWorldsPartyIdGatheringsQueryKey,
    useGetPartyIdChatGroups,
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
    g.placeId ? `at ${g.roomName}` : `in #${g.roomName}`;

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
    { key: 'outcome', header: 'How it went', width: 'minmax(140px, 2fr)' },
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
        <div className="flex h-full flex-col gap-2 p-2">
            <PlanForm playerId={playerId} />
            <GroupBox
                title="Coming up"
                className="min-h-0 flex-1 grid-rows-[minmax(0,1fr)] content-stretch"
            >
                <ListView
                    label="Coming up"
                    className="min-h-0"
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
                        cells: {
                            when: time(g.startsAt),
                            where: whereOf(g),
                            outcome:
                                g.status === 'Missed'
                                    ? 'missed, the world was asleep'
                                    : `${g.attendeeIds?.length ?? 0} said they'd come`,
                        },
                    }))}
                />
            </GroupBox>
        </div>
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
    const rooms = (useGetPartyIdChatGroups(WORLD_ID).data?.data ?? []).filter(
        (r) => r.kind !== 'dm' && r.kind !== 'place',
    );
    const places = (
        useGetWorldsPartyIdPlaces(WORLD_ID).data?.data.places ?? []
    ).filter((p) => p.kind === 'public');
    const [title, setTitle] = useState('');
    const [roomId, setRoomId] = useState('');
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
    const room = roomId || rooms[0]?.id || '';
    return (
        <form
            className="flex flex-wrap items-center gap-1.5"
            onSubmit={(e) => {
                e.preventDefault();
                if (!title.trim() || !room) return;
                const placeId = room.startsWith('place:')
                    ? room.slice(6)
                    : null;
                plan.mutate({
                    partyId: WORLD_ID,
                    data: {
                        title: title.trim(),
                        roomId: placeId
                            ? '00000000-0000-0000-0000-000000000000'
                            : room,
                        placeId,
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
            <label htmlFor={`${ids}-r`}>in</label>
            <ComboBox
                id={`${ids}-r`}
                value={room}
                onChange={(e) => setRoomId(e.currentTarget.value)}
            >
                {rooms.length > 0 ? (
                    <optgroup label="Rooms">
                        {rooms.map((r) => (
                            <option key={r.id} value={r.id ?? ''}>
                                #{r.name}
                            </option>
                        ))}
                    </optgroup>
                ) : null}
                {places.length > 0 ? (
                    <optgroup label="Places">
                        {places.map((p) => (
                            <option key={p.id} value={`place:${p.id}`}>
                                {p.name}
                            </option>
                        ))}
                    </optgroup>
                ) : null}
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
                disabled={!title.trim() || !room || plan.isPending}
            >
                Plan it
            </CommandButton>
        </form>
    );
}
