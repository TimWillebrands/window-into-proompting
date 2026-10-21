import { useQueryClient } from '@tanstack/react-query';
import { useId, useState } from 'react';
import {
    getGetWorldsPartyIdGatheringsQueryKey,
    useGetPartyIdChatGroups,
    useGetWorldsPartyIdGatherings,
    useGetWorldsPartyIdLivesSuspense,
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

const panel = {
    background: '#fff',
    border: '1px solid',
    borderColor: '#7f9db9 #fff #fff #7f9db9',
} as const;

/** Calendar: get-togethers planned in Rooms by personas and humans; RSVP as yourself. */
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
        <div className="app-surface flex h-full flex-col" style={{ gap: 6 }}>
            <PlanForm playerId={playerId} />
            <fieldset
                className="flex min-h-0 flex-1 flex-col"
                style={{ margin: '0 6px' }}
            >
                <legend>Coming up</legend>
                <div className="flex-1 overflow-y-auto" style={panel}>
                    {upcoming.length === 0 ? (
                        <p style={{ padding: 8, color: '#666' }}>
                            Nothing planned. Personas plan get-togethers on
                            their own — or plan one above.
                        </p>
                    ) : null}
                    {upcoming.map((g) => (
                        <Row
                            key={g.id}
                            g={g}
                            nameOf={nameOf}
                            playerId={playerId}
                            now={now}
                        />
                    ))}
                </div>
            </fieldset>
            <fieldset style={{ margin: '0 6px 6px' }}>
                <legend>Recently</legend>
                <div style={{ ...panel, maxHeight: 110, overflowY: 'auto' }}>
                    {past.length === 0 ? (
                        <p style={{ padding: 6, color: '#666' }}>—</p>
                    ) : null}
                    {past.map((g) => (
                        <div
                            key={g.id}
                            style={{ padding: '3px 8px', color: '#555' }}
                        >
                            {time(g.startsAt)} — “{g.title}” in #{g.roomName} (
                            {g.attendeeIds?.length ?? 0} came)
                        </div>
                    ))}
                </div>
            </fieldset>
        </div>
    );
}

function Row({
    g,
    nameOf,
    playerId,
    now,
}: {
    g: Gathering;
    nameOf: (id: string) => string;
    playerId: string | null;
    now: number;
}) {
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
    const mins = Math.round((Number(g.startsAt) - now) / 60000);
    return (
        <div
            className="flex items-center"
            style={{
                gap: 8,
                padding: '6px 8px',
                borderBottom: '1px solid #e4e8f0',
            }}
        >
            <div style={{ width: 92, textAlign: 'center', color: '#003399' }}>
                <div style={{ fontWeight: 700 }}>{time(g.startsAt)}</div>
                <div style={{ fontSize: 10, color: '#666' }}>
                    {mins < 60
                        ? `in ${Math.max(mins, 0)} min`
                        : `in ${Math.round(mins / 60)} h`}
                </div>
            </div>
            <div className="min-w-0 flex-1">
                <div style={{ fontWeight: 700 }}>“{g.title}”</div>
                <div style={{ color: '#555' }}>
                    #{g.roomName} · hosted by {g.hostName} ·{' '}
                    {(g.attendeeIds ?? []).map(nameOf).join(', ')}
                </div>
            </div>
            <button
                type="button"
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
            </button>
        </div>
    );
}

function PlanForm({ playerId }: { playerId: string | null }) {
    const queryClient = useQueryClient();
    const ids = useId();
    const rooms = (useGetPartyIdChatGroups(WORLD_ID).data?.data ?? []).filter(
        (r) => r.kind !== 'dm',
    );
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
            className="flex flex-wrap items-center"
            style={{ gap: 6, padding: '6px 8px 0' }}
            onSubmit={(e) => {
                e.preventDefault();
                if (!title.trim() || !room) return;
                plan.mutate({
                    partyId: WORLD_ID,
                    data: {
                        title: title.trim(),
                        roomId: room,
                        startsAt:
                            Date.now() +
                            Math.max(1, Number(minutes) || 30) * 60000,
                        hostId: playerId,
                    },
                });
            }}
        >
            <label htmlFor={`${ids}-t`}>Plan:</label>
            <input
                id={`${ids}-t`}
                value={title}
                maxLength={120}
                placeholder="Movie night, storm stories, …"
                onChange={(e) => setTitle(e.currentTarget.value)}
                style={{ flex: 1, minWidth: 140 }}
            />
            <label htmlFor={`${ids}-r`}>in</label>
            <select
                id={`${ids}-r`}
                value={room}
                onChange={(e) => setRoomId(e.currentTarget.value)}
            >
                {rooms.map((r) => (
                    <option key={r.id} value={r.id ?? ''}>
                        #{r.name}
                    </option>
                ))}
            </select>
            <label htmlFor={`${ids}-m`}>starts in</label>
            <input
                id={`${ids}-m`}
                type="number"
                min={1}
                value={minutes}
                onChange={(e) => setMinutes(e.currentTarget.value)}
                style={{ width: 52 }}
            />
            min
            <button
                type="submit"
                disabled={!title.trim() || !room || plan.isPending}
            >
                Plan it
            </button>
        </form>
    );
}
