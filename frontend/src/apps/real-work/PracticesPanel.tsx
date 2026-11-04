import { useQueryClient } from '@tanstack/react-query';
import { useId, useState } from 'react';
import {
    getGetWorldsPartyIdPracticesQueryKey,
    getGetWorldsPartyIdSessionsQueryKey,
    useGetWorldsPartyIdPracticesSuspense,
    useGetWorldsPartyIdSessionsSuspense,
    usePostWorldsPartyIdPracticesParticipantIdSessions,
    usePutWorldsPartyIdPracticesParticipantId,
} from '#api/party-zone';
import type { JobSummary, PracticeSummary } from '../../api/model';
import { WORLD_ID } from '../../lib/world';
import { homeworkLabel, refusal, sessionLabel } from './real-work-lib';

const cell = { padding: '2px 6px' } as const;
const failure = { color: '#a00', margin: '4px 0 0' } as const;

const when = (at: number | string) =>
    new Date(Number(at)).toLocaleString('en-GB', {
        weekday: 'short',
        hour: '2-digit',
        minute: '2-digit',
    });

/** Practices (ADR 0030): set someone up to take Clients, book anyone in, see how Sessions went. */
export function PracticesPanel({ people }: { people: JobSummary[] }) {
    const practices =
        useGetWorldsPartyIdPracticesSuspense(WORLD_ID).data.data ?? [];
    const sessions =
        useGetWorldsPartyIdSessionsSuspense(WORLD_ID, {
            query: { refetchInterval: 30_000 },
        }).data.data ?? [];

    return (
        <div className="flex flex-col" style={{ gap: 6 }}>
            {practices.length > 0 ? (
                <table
                    className="w-full"
                    style={{ borderCollapse: 'collapse' }}
                >
                    <thead>
                        <tr
                            style={{ background: '#ece9d8', textAlign: 'left' }}
                        >
                            <th style={cell}>Who</th>
                            <th style={cell}>Practice</th>
                            <th style={cell}>When</th>
                            <th style={cell}>Fee</th>
                        </tr>
                    </thead>
                    <tbody>
                        {practices.map((p) => (
                            <tr key={p.participantId}>
                                <td style={cell}>{p.name}</td>
                                <td style={cell} title={p.approach}>
                                    {p.title} ({p.field})
                                </td>
                                <td style={cell}>
                                    {p.days} {p.hours}
                                </td>
                                <td style={cell}>{p.fee}</td>
                            </tr>
                        ))}
                    </tbody>
                </table>
            ) : null}
            {practices.length > 0 ? (
                <BookForm practices={practices} people={people} />
            ) : null}
            {sessions.length > 0 ? (
                <fieldset style={{ margin: 0 }}>
                    <legend>Sessions</legend>
                    {sessions.map((s) => (
                        <div key={s.id} title={s.reason}>
                            {when(s.startsAt)} at {s.workplace} · {s.clientName}{' '}
                            with {s.practitionerName} · {sessionLabel(s)}
                            {s.homework ? ` · ${homeworkLabel(s)}` : ''}
                        </div>
                    ))}
                </fieldset>
            ) : null}
            <SetUpForm people={people} />
        </div>
    );
}

function BookForm({
    practices,
    people,
}: {
    practices: PracticeSummary[];
    people: JobSummary[];
}) {
    const queryClient = useQueryClient();
    const book = usePostWorldsPartyIdPracticesParticipantIdSessions({
        mutation: {
            onSuccess: () =>
                queryClient.invalidateQueries({
                    queryKey: getGetWorldsPartyIdSessionsQueryKey(WORLD_ID),
                }),
        },
    });
    const [practitioner, setPractitioner] = useState('');
    const [client, setClient] = useState('');
    const [reason, setReason] = useState('');

    return (
        <fieldset style={{ margin: 0 }}>
            <legend>Book a session</legend>
            <form
                className="flex flex-wrap items-center"
                style={{ gap: 6 }}
                onSubmit={(e) => {
                    e.preventDefault();
                    book.mutate({
                        partyId: WORLD_ID,
                        participantId: practitioner,
                        data: { clientId: client, reason },
                    });
                }}
            >
                <select
                    aria-label="Client"
                    value={client}
                    onChange={(e) => setClient(e.currentTarget.value)}
                >
                    <option value="">who…</option>
                    {people.map((p) => (
                        <option key={p.participantId} value={p.participantId}>
                            {p.name}
                        </option>
                    ))}
                </select>
                <select
                    aria-label="Practitioner"
                    value={practitioner}
                    onChange={(e) => setPractitioner(e.currentTarget.value)}
                >
                    <option value="">sees…</option>
                    {practices.map((p) => (
                        <option key={p.participantId} value={p.participantId}>
                            {p.name}
                        </option>
                    ))}
                </select>
                <input
                    aria-label="Reason"
                    placeholder="why they're coming"
                    value={reason}
                    onChange={(e) => setReason(e.currentTarget.value)}
                    style={{ flex: 1, minWidth: 160 }}
                />
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
            </form>
            {book.error ? <p style={failure}>{refusal(book.error)}</p> : null}
        </fieldset>
    );
}

function SetUpForm({ people }: { people: JobSummary[] }) {
    const ids = useId();
    const queryClient = useQueryClient();
    const setUp = usePutWorldsPartyIdPracticesParticipantId({
        mutation: {
            onSuccess: () =>
                queryClient.invalidateQueries({
                    queryKey: getGetWorldsPartyIdPracticesQueryKey(WORLD_ID),
                }),
        },
    });
    const [who, setWho] = useState('');
    const [title, setTitle] = useState('');
    const [field, setField] = useState('');
    const [approach, setApproach] = useState('');
    const [workplace, setWorkplace] = useState('');
    const [fee, setFee] = useState('40');
    const [days, setDays] = useState('Mon-Fri');
    const [hours, setHours] = useState('09:00-17:00');

    return (
        <fieldset style={{ margin: 0 }}>
            <legend>Set up a practice</legend>
            <form
                className="flex flex-wrap items-center"
                style={{ gap: 6 }}
                onSubmit={(e) => {
                    e.preventDefault();
                    setUp.mutate({
                        partyId: WORLD_ID,
                        participantId: who,
                        data: {
                            title,
                            workplace,
                            field,
                            approach: approach || null,
                            fee: Number(fee),
                            days,
                            hours,
                        },
                    });
                }}
            >
                <select
                    aria-label="Who"
                    value={who}
                    onChange={(e) => setWho(e.currentTarget.value)}
                >
                    <option value="">who…</option>
                    {people.map((p) => (
                        <option key={p.participantId} value={p.participantId}>
                            {p.name}
                        </option>
                    ))}
                </select>
                <label htmlFor={`${ids}-t`}>starts as</label>
                <input
                    id={`${ids}-t`}
                    placeholder="career coach"
                    value={title}
                    onChange={(e) => setTitle(e.currentTarget.value)}
                    style={{ width: 110 }}
                />
                <label htmlFor={`${ids}-f`}>for</label>
                <input
                    id={`${ids}-f`}
                    placeholder="careers in logistics"
                    value={field}
                    onChange={(e) => setField(e.currentTarget.value)}
                    style={{ flex: 1, minWidth: 130 }}
                />
                <input
                    aria-label="Workplace"
                    placeholder="the practice on the corner"
                    value={workplace}
                    onChange={(e) => setWorkplace(e.currentTarget.value)}
                    style={{ flex: 1, minWidth: 130 }}
                />
                <input
                    aria-label="How they work"
                    placeholder="how they work"
                    value={approach}
                    onChange={(e) => setApproach(e.currentTarget.value)}
                    style={{ flex: 2, minWidth: 160 }}
                />
                <input
                    aria-label="Fee in credits"
                    value={fee}
                    onChange={(e) => setFee(e.currentTarget.value)}
                    style={{ width: 40 }}
                />
                <input
                    aria-label="Days"
                    value={days}
                    onChange={(e) => setDays(e.currentTarget.value)}
                    style={{ width: 70 }}
                />
                <input
                    aria-label="Hours"
                    value={hours}
                    onChange={(e) => setHours(e.currentTarget.value)}
                    style={{ width: 90 }}
                />
                <button
                    type="submit"
                    disabled={
                        !who ||
                        !title.trim() ||
                        !field.trim() ||
                        !workplace.trim() ||
                        setUp.isPending
                    }
                >
                    Start
                </button>
            </form>
            {setUp.error ? <p style={failure}>{refusal(setUp.error)}</p> : null}
        </fieldset>
    );
}
