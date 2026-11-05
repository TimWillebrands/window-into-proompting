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
import { Card, Empty, Field, Jump, PageHead, Refusal } from './parts';
import { useRefreshWork, useWork } from './work-context';

export function PracticesPage() {
    const { practices, sessions, go } = useWork();
    return (
        <>
            <PageHead
                icon="🛋️"
                title="Practices"
                sub="People who sell their time by the session, paid wallet to wallet."
            />
            <div className="wk-page">
                {practices.length === 0 ? (
                    <Empty>
                        Nobody takes clients yet. Pick someone under People and
                        give them their own practice.
                    </Empty>
                ) : (
                    <div className="wk-tiles">
                        {practices.map((p) => (
                            <button
                                key={p.participantId}
                                type="button"
                                className="xp-bare wk-tile"
                                title={p.approach}
                                onClick={() =>
                                    go({ page: 'person', id: p.participantId })
                                }
                            >
                                <strong>{p.name}</strong>
                                <span>
                                    {p.title}, {p.field}
                                </span>
                                <span>
                                    {p.days} {p.hours} · {p.fee} credits
                                </span>
                            </button>
                        ))}
                    </div>
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
            </div>
        </>
    );
}

export function SessionList({ sessions }: { sessions: SessionSummary[] }) {
    const { go } = useWork();
    return (
        <ul className="wk-log">
            {sessionsInOrder(sessions).map((s) => (
                <li key={s.id} title={s.reason}>
                    <time>{whenLabel(s.startsAt)}</time>
                    <span>
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
                </li>
            ))}
        </ul>
    );
}

/** Books the next free slot; on a person's page the Practitioner is already known. */
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
        <form
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
            <div className="wk-fields">
                <Field label="Who">
                    <select
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
                    </select>
                </Field>
                {practitionerId ? null : (
                    <Field label="Sees">
                        <select
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
                        </select>
                    </Field>
                )}
                <Field label="Why they're coming" grow>
                    <input
                        value={reason}
                        onChange={(e) => setReason(e.currentTarget.value)}
                    />
                </Field>
            </div>
            <div className="wk-actions">
                <button
                    type="submit"
                    disabled={
                        !client ||
                        !practitioner ||
                        !reason.trim() ||
                        book.isPending
                    }
                >
                    Book
                </button>
            </div>
            <Refusal error={book.error} />
        </form>
    );
}
