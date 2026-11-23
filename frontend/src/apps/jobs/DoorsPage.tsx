import { ComboBox, CommandButton } from '@partytown/ui';
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
import {
    Card,
    Empty,
    Field,
    Fields,
    Jump,
    Page,
    PageHead,
    Refusal,
    Row,
    Rows,
} from './parts';
import { useWork } from './work-context';

export function DoorsPage() {
    const places =
        useGetWorldsPartyIdPlacesSuspense(WORLD_ID, {
            query: { refetchInterval: 30_000 },
        }).data.data.places ?? [];
    const workplaces = workplacesOf(places);
    return (
        <>
            <PageHead
                icon="door"
                title="Doors"
                sub="Who may walk into a workplace. Staff always can; anyone else knocks, unless they're on its list or expected for a session or get-together."
            />
            <Page>
                {workplaces.length === 0 ? (
                    <Empty>Nobody works anywhere on the map yet.</Empty>
                ) : (
                    workplaces.map((p) => <DoorCard key={p.id} place={p} />)
                )}
            </Page>
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
            <Fields>
                <Field label="Door">
                    <ComboBox
                        value={door}
                        disabled={!isAdmin || save.isPending}
                        onChange={(e) => set(e.currentTarget.value, allowed)}
                    >
                        {DOORS.map((d) => (
                            <option key={d.value} value={d.value}>
                                {d.label}
                            </option>
                        ))}
                    </ComboBox>
                </Field>
            </Fields>
            <p>
                Staff:{' '}
                {staff.map((id, i) => (
                    <span key={id}>
                        {i > 0 ? ', ' : null}
                        {person(id)}
                    </span>
                ))}
            </p>
            {allowed.length > 0 ? (
                <Rows>
                    {allowed.map((id) => (
                        <Row key={id}>
                            {person(id)}
                            <span className="flex-1">on the list</span>
                            {isAdmin ? (
                                <CommandButton
                                    size="small"
                                    disabled={save.isPending}
                                    onClick={() =>
                                        set(
                                            door,
                                            allowed.filter((a) => a !== id),
                                        )
                                    }
                                >
                                    Take off
                                </CommandButton>
                            ) : null}
                        </Row>
                    ))}
                </Rows>
            ) : (
                <p>Nobody else is on the list.</p>
            )}
            {isAdmin && door !== 'open' ? (
                <form
                    className="flex items-center gap-1.5"
                    onSubmit={(e) => {
                        e.preventDefault();
                        set(door, [...allowed, who]);
                        setWho('');
                    }}
                >
                    <ComboBox
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
                    </ComboBox>
                    <CommandButton
                        type="submit"
                        isDefault
                        disabled={!who || save.isPending}
                    >
                        Add
                    </CommandButton>
                </form>
            ) : null}
            <Refusal error={save.error} />
        </Card>
    );
}
