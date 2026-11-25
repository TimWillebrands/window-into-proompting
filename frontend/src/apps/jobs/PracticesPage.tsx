import {
    ComboBox,
    CommandButton,
    Icon,
    ListView,
    Stack,
    TextBox,
} from '@partytown/ui';
import { useState } from 'react';
import { usePostWorldsPartyIdPracticesParticipantIdSessions } from '#api/party-zone';
import type { SessionSummary } from '../../api/model';
import { WORLD_ID } from '../../lib/world';
import {
    homeworkLabel,
    sessionLabel,
    sessionsInOrder,
    whenLabel,
} from './jobs-lib';
import {
    Actions,
    Card,
    Empty,
    Field,
    Fields,
    Jump,
    Page,
    PageHead,
    Refusal,
} from './parts';
import { useRefreshWork, useWork } from './work-context';

export function PracticesPage() {
    const { practices, sessions, go } = useWork();
    return (
        <>
            <PageHead
                icon="sofa"
                title="Practices"
                sub="People who sell their time by the session, paid wallet to wallet."
            />
            <Page>
                {practices.length === 0 ? (
                    <Empty>
                        Nobody takes clients yet. Pick someone under People and
                        give them their own practice.
                    </Empty>
                ) : (
                    <ListView
                        view="tiles"
                        label="Practices"
                        activateOn="single"
                        onActivate={(id) => go({ page: 'person', id })}
                        items={practices.map((p) => ({
                            id: p.participantId,
                            label: <span title={p.approach}>{p.name}</span>,
                            largeIcon: <Icon name="sofa" size={48} />,
                            details: [
                                `${p.title}, ${p.field}`,
                                `${p.days} ${p.hours} · ${p.fee} credits`,
                            ],
                        }))}
                    />
                )}
                {practices.length > 0 ? (
                    <Card title="Book a session">
                        <BookForm />
                    </Card>
                ) : null}
                {sessions.length > 0 ? (
                    <Card title="Sessions">
                        <SessionList sessions={sessions} />
                    </Card>
                ) : null}
            </Page>
        </>
    );
}

export function SessionList({ sessions }: { sessions: SessionSummary[] }) {
    const { go } = useWork();
    return (
        <ListView
            label="Sessions"
            selectionMode="none"
            columns={[
                { key: 'when', header: 'When', width: 'max-content' },
                {
                    key: 'session',
                    header: 'Session',
                    width: 'minmax(160px, 1fr)',
                    wrap: true,
                },
            ]}
            items={sessionsInOrder(sessions).map((s) => ({
                id: s.id,
                label: whenLabel(s.startsAt),
                cells: {
                    session: (
                        <span title={s.reason}>
                            <Jump
                                onClick={() =>
                                    go({ page: 'person', id: s.clientId })
                                }
                            >
                                {s.clientName}
                            </Jump>{' '}
                            with{' '}
                            <Jump
                                onClick={() =>
                                    go({ page: 'person', id: s.practitionerId })
                                }
                            >
                                {s.practitionerName}
                            </Jump>{' '}
                            at {s.workplace} · {sessionLabel(s)}
                            {s.homework ? ` · ${homeworkLabel(s)}` : ''}
                        </span>
                    ),
                },
            }))}
        />
    );
}

export function BookForm({ practitionerId }: { practitionerId?: string }) {
    const { practices, people } = useWork();
    const book = usePostWorldsPartyIdPracticesParticipantIdSessions(
        useRefreshWork(),
    );
    const [practitioner, setPractitioner] = useState(practitionerId ?? '');
    const [client, setClient] = useState('');
    const [reason, setReason] = useState('');
    const clients = people.filter((p) => p.participantId !== practitioner);

    return (
        <Stack
            as="form"
            gap="md"
            onSubmit={(e) => {
                e.preventDefault();
                book.mutate(
                    {
                        partyId: WORLD_ID,
                        participantId: practitioner,
                        data: { clientId: client, reason },
                    },
                    { onSuccess: () => setReason('') },
                );
            }}
        >
            <Fields>
                <Field label="Who">
                    <ComboBox
                        value={client}
                        onChange={(e) => setClient(e.currentTarget.value)}
                    >
                        <option value="">Pick one…</option>
                        {clients.map((p) => (
                            <option
                                key={p.participantId}
                                value={p.participantId}
                            >
                                {p.name}
                            </option>
                        ))}
                    </ComboBox>
                </Field>
                {practitionerId ? null : (
                    <Field label="Sees">
                        <ComboBox
                            value={practitioner}
                            onChange={(e) =>
                                setPractitioner(e.currentTarget.value)
                            }
                        >
                            <option value="">Pick one…</option>
                            {practices.map((p) => (
                                <option
                                    key={p.participantId}
                                    value={p.participantId}
                                >
                                    {p.name}
                                </option>
                            ))}
                        </ComboBox>
                    </Field>
                )}
                <Field label="Why they're coming" grow>
                    <TextBox
                        value={reason}
                        onChange={(e) => setReason(e.currentTarget.value)}
                    />
                </Field>
            </Fields>
            <Actions>
                <CommandButton
                    type="submit"
                    isDefault
                    disabled={
                        !client ||
                        !practitioner ||
                        !reason.trim() ||
                        book.isPending
                    }
                >
                    Book
                </CommandButton>
            </Actions>
            <Refusal error={book.error} />
        </Stack>
    );
}
