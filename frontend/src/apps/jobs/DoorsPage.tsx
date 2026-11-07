import { useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';
import {
    getGetWorldsPartyIdPlacesQueryKey,
    useGetWorldsPartyIdPlacesSuspense,
    usePutWorldsPartyIdPlacesPlaceIdAccess,
} from '#api/party-zone';
import type { Place } from '../../api/model';
import { useIsAdmin, WORLD_ID } from '../../lib/world';
import { DOORS, doorOf, workplacesOf } from './jobs-lib';
import { Card, Empty, Field, Jump, PageHead, Refusal } from './parts';
import { useWork } from './work-context';

/** Workplace doors (ADR 0041): who may walk in besides the staff. */
export function DoorsPage() {
    const places =
        useGetWorldsPartyIdPlacesSuspense(WORLD_ID, {
            query: { refetchInterval: 30_000 },
        }).data.data.places ?? [];
    const workplaces = workplacesOf(places);
    return (
        <>
            <PageHead
                icon="🚪"
                title="Doors"
                sub="Who may walk into a workplace. Staff always can; anyone else knocks, unless they're on its list or expected for a session or get-together."
            />
            <div className="wk-page">
                {workplaces.length === 0 ? (
                    <Empty>Nobody works anywhere on the map yet.</Empty>
                ) : (
                    workplaces.map((p) => <DoorCard key={p.id} place={p} />)
                )}
            </div>
        </>
    );
}

function DoorCard({ place }: { place: Place }) {
    const { people, nameOf, go } = useWork();
    const isAdmin = useIsAdmin();
    const queryClient = useQueryClient();
    const save = usePutWorldsPartyIdPlacesPlaceIdAccess({
        mutation: {
            onSuccess: () =>
                queryClient.invalidateQueries({
                    queryKey: getGetWorldsPartyIdPlacesQueryKey(WORLD_ID),
                }),
        },
    });
    const [who, setWho] = useState('');
    const staff = place.staff ?? [];
    const allowed = place.allowed ?? [];
    const door = doorOf(place);
    const set = (access: string, list: string[]) =>
        place.id &&
        save.mutate({
            partyId: WORLD_ID,
            placeId: place.id,
            data: { access, allowed: list },
        });
    const free = people.filter(
        (p) =>
            !staff.includes(p.participantId) &&
            !allowed.includes(p.participantId),
    );
    const person = (id: string) => (
        <Jump onClick={() => go({ page: 'person', id })}>{nameOf(id)}</Jump>
    );

    return (
        <Card title={place.name ?? 'somewhere'}>
            <div className="wk-fields">
                <Field label="Door">
                    <select
                        value={door}
                        disabled={!isAdmin || save.isPending}
                        onChange={(e) => set(e.currentTarget.value, allowed)}
                    >
                        {DOORS.map((d) => (
                            <option key={d.value} value={d.value}>
                                {d.label}
                            </option>
                        ))}
                    </select>
                </Field>
            </div>
            <p>
                <span className="wk-muted">Staff: </span>
                {staff.map((id, i) => (
                    <span key={id}>
                        {i > 0 ? ', ' : null}
                        {person(id)}
                    </span>
                ))}
            </p>
            {allowed.length > 0 ? (
                <ul className="wk-rows">
                    {allowed.map((id) => (
                        <li key={id}>
                            {person(id)}
                            <span className="wk-muted">on the list</span>
                            {isAdmin ? (
                                <button
                                    type="button"
                                    disabled={save.isPending}
                                    onClick={() =>
                                        set(
                                            door,
                                            allowed.filter((a) => a !== id),
                                        )
                                    }
                                >
                                    Take off
                                </button>
                            ) : null}
                        </li>
                    ))}
                </ul>
            ) : (
                <p className="wk-muted">Nobody else is on the list.</p>
            )}
            {isAdmin && door !== 'open' ? (
                <form
                    className="wk-inline"
                    onSubmit={(e) => {
                        e.preventDefault();
                        set(door, [...allowed, who]);
                        setWho('');
                    }}
                >
                    <select
                        aria-label="Who else may come in"
                        value={who}
                        onChange={(e) => setWho(e.currentTarget.value)}
                    >
                        <option value="">Let someone else in…</option>
                        {free.map((p) => (
                            <option
                                key={p.participantId}
                                value={p.participantId}
                            >
                                {p.name}
                            </option>
                        ))}
                    </select>
                    <button type="submit" disabled={!who || save.isPending}>
                        Add
                    </button>
                </form>
            ) : null}
            <Refusal error={save.error} />
        </Card>
    );
}
