import { Suspense, useState } from 'react';
import {
    useDeleteWorldsPartyIdJobsParticipantId,
    usePutWorldsPartyIdCompaniesCompanyIdStaffParticipantId,
    usePutWorldsPartyIdJobsParticipantId,
    usePutWorldsPartyIdPracticesParticipantId,
} from '#api/party-zone';
import type { JobSummary } from '../../api/model';
import { WORLD_ID } from '../../lib/world';
import { type Employment, jobLine, shiftLabel } from './jobs-lib';
import { BookForm, SessionList } from './PracticesPage';
import { Card, Field, Jump, PageHead, Refusal } from './parts';
import { SkillsPanel } from './SkillsPanel';
import { WantsPanel } from './WantsPanel';
import { useRefreshWork, useWork } from './work-context';

/** The front page: everyone, what they do, and how today went. */
export function PeoplePage() {
    const { people, workOf, go } = useWork();
    const working = people.filter((p) => p.title).length;
    return (
        <>
            <PageHead
                icon="👥"
                title="Everyone"
                sub={`${working} of ${people.length} in work. Pick someone to see or change it.`}
            />
            <div className="wk-page">
                <table className="wk-table">
                    <thead>
                        <tr>
                            <th>Who</th>
                            <th>Does</th>
                            <th>When</th>
                            <th>Today</th>
                        </tr>
                    </thead>
                    <tbody>
                        {people.map((p) => {
                            const work = workOf(p);
                            return (
                                <tr
                                    key={p.participantId}
                                    onClick={() =>
                                        go({
                                            page: 'person',
                                            id: p.participantId,
                                        })
                                    }
                                >
                                    <td className="wk-tight">{p.name}</td>
                                    <td>
                                        <JobCell person={p} work={work} />
                                    </td>
                                    <td>
                                        {p.days ? `${p.days} ${p.hours}` : ''}
                                    </td>
                                    <td className="wk-tight">
                                        {shiftLabel(p)}
                                    </td>
                                </tr>
                            );
                        })}
                    </tbody>
                </table>
            </div>
        </>
    );
}

function JobCell({ person, work }: { person: JobSummary; work: Employment }) {
    const { go } = useWork();
    if (work.kind !== 'company') return jobLine(person, work);
    return (
        <>
            {person.title} at{' '}
            <Jump onClick={() => go({ page: 'company', id: work.company.id })}>
                {work.company.name}
            </Jump>
        </>
    );
}

export function PersonPage({ person }: { person: JobSummary }) {
    const { workOf, go, sessions, nameOf } = useWork();
    const work = workOf(person);
    const id = person.participantId;
    const theirs = sessions.filter(
        (s) => s.clientId === id || s.practitionerId === id,
    );
    const where = person.days ? `${person.days} ${person.hours}` : '';
    return (
        <>
            <PageHead
                icon="🧑"
                title={person.name}
                sub={[jobLine(person, work), where].filter(Boolean).join(' · ')}
                chip={shiftLabel(person) || undefined}
            />
            <div className="wk-page">
                {work.kind === 'company' ? (
                    <p className="wk-note">
                        Works at{' '}
                        <Jump
                            onClick={() =>
                                go({ page: 'company', id: work.company.id })
                            }
                        >
                            {work.company.name}
                        </Jump>
                        .
                    </p>
                ) : null}
                <WorkForm
                    key={`${id}-${work.kind}`}
                    person={person}
                    work={work}
                />
                {work.kind === 'practice' ? (
                    <Card title={`Book a session with ${person.name}`}>
                        <BookForm practitionerId={id} />
                    </Card>
                ) : null}
                {theirs.length > 0 ? (
                    <Card title="Sessions">
                        <SessionList sessions={theirs} />
                    </Card>
                ) : null}
                <Suspense fallback={null}>
                    <WantsPanel
                        key={`w-${id}`}
                        participantId={id}
                        name={person.name}
                        nameOf={nameOf}
                    />
                </Suspense>
                <Suspense fallback={null}>
                    <SkillsPanel
                        key={`s-${id}`}
                        participantId={id}
                        name={person.name}
                    />
                </Suspense>
            </div>
        </>
    );
}

type Mode = 'job' | 'company' | 'practice';

/** One form for what someone does: a job, a place at a Company, or their own Practice. */
function WorkForm({ person, work }: { person: JobSummary; work: Employment }) {
    const { companies } = useWork();
    const refresh = useRefreshWork();
    const saveJob = usePutWorldsPartyIdJobsParticipantId(refresh);
    const clear = useDeleteWorldsPartyIdJobsParticipantId(refresh);
    const hire =
        usePutWorldsPartyIdCompaniesCompanyIdStaffParticipantId(refresh);
    const setUp = usePutWorldsPartyIdPracticesParticipantId(refresh);

    const practice = work.kind === 'practice' ? work.practice : null;
    const [mode, setMode] = useState<Mode>(
        work.kind === 'none' ? 'job' : work.kind,
    );
    const [title, setTitle] = useState(person.title ?? '');
    const [workplace, setWorkplace] = useState(person.workplace ?? '');
    const [days, setDays] = useState(person.days ?? 'Mon-Fri');
    const [hours, setHours] = useState(person.hours ?? '09:00-17:00');
    const [companyId, setCompanyId] = useState(
        work.kind === 'company' ? work.company.id : '',
    );
    const [field, setField] = useState(practice?.field ?? '');
    const [approach, setApproach] = useState(practice?.approach ?? '');
    const [fee, setFee] = useState(String(practice?.fee ?? 40));

    const partyId = WORLD_ID;
    const participantId = person.participantId;
    const pending =
        saveJob.isPending ||
        clear.isPending ||
        hire.isPending ||
        setUp.isPending;
    const ready =
        title.trim() &&
        (mode === 'company'
            ? companyId
            : workplace.trim() && (mode === 'job' || field.trim()));

    const save = () => {
        if (mode === 'company') {
            hire.mutate({
                partyId,
                companyId,
                participantId,
                data: { title, days, hours },
            });
        } else if (mode === 'practice') {
            setUp.mutate({
                partyId,
                participantId,
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
        } else {
            const job = { title, workplace, days, hours };
            // A plain edit keeps the Company or Practice, so leaving one means clearing it first.
            const leaving = work.kind === 'company' || work.kind === 'practice';
            const clearing = leaving
                ? clear.mutateAsync({ partyId, participantId })
                : Promise.resolve();
            clearing
                .then(() =>
                    saveJob.mutate({ partyId, participantId, data: job }),
                )
                .catch(() => undefined);
        }
    };

    const modes: [Mode, string][] = [
        ['job', 'A job'],
        ['company', 'At a company'],
        ['practice', 'Own practice'],
    ];

    return (
        <Card title="Work">
            <form
                onSubmit={(e) => {
                    e.preventDefault();
                    save();
                }}
            >
                <div className="wk-modes">
                    {modes.map(([value, label]) => (
                        <button
                            key={value}
                            type="button"
                            className="xp-bare wk-mode"
                            aria-pressed={mode === value}
                            disabled={
                                value === 'company' && companies.length === 0
                            }
                            onClick={() => setMode(value)}
                        >
                            {label}
                        </button>
                    ))}
                </div>
                <div className="wk-fields">
                    <Field label="Title" grow>
                        <input
                            value={title}
                            placeholder={
                                mode === 'practice'
                                    ? 'career coach'
                                    : mode === 'company'
                                      ? 'trader'
                                      : 'barista'
                            }
                            onChange={(e) => setTitle(e.currentTarget.value)}
                        />
                    </Field>
                    {mode === 'company' ? (
                        <Field label="Company" grow>
                            <select
                                value={companyId}
                                onChange={(e) =>
                                    setCompanyId(e.currentTarget.value)
                                }
                            >
                                <option value="">Pick one…</option>
                                {companies.map((c) => (
                                    <option key={c.id} value={c.id}>
                                        {c.name}
                                    </option>
                                ))}
                            </select>
                        </Field>
                    ) : (
                        <Field label="Workplace" grow>
                            <input
                                value={workplace}
                                placeholder="the Café, the office in Zuid"
                                onChange={(e) =>
                                    setWorkplace(e.currentTarget.value)
                                }
                            />
                        </Field>
                    )}
                    <Field label="Days">
                        <input
                            className="wk-short"
                            value={days}
                            onChange={(e) => setDays(e.currentTarget.value)}
                        />
                    </Field>
                    <Field label="Hours">
                        <input
                            className="wk-short"
                            value={hours}
                            onChange={(e) => setHours(e.currentTarget.value)}
                        />
                    </Field>
                </div>
                {mode === 'practice' ? (
                    <div className="wk-fields">
                        <Field label="For" grow>
                            <input
                                value={field}
                                placeholder="careers in logistics"
                                onChange={(e) =>
                                    setField(e.currentTarget.value)
                                }
                            />
                        </Field>
                        <Field label="How they work" grow>
                            <input
                                value={approach}
                                placeholder="blunt, practical, sets homework"
                                onChange={(e) =>
                                    setApproach(e.currentTarget.value)
                                }
                            />
                        </Field>
                        <Field label="Fee">
                            <input
                                className="wk-tiny"
                                value={fee}
                                onChange={(e) => setFee(e.currentTarget.value)}
                            />
                        </Field>
                    </div>
                ) : null}
                <div className="wk-actions">
                    <button type="submit" disabled={!ready || pending}>
                        Save
                    </button>
                    <button
                        type="button"
                        disabled={!person.title || pending}
                        onClick={() => clear.mutate({ partyId, participantId })}
                    >
                        No job
                    </button>
                </div>
                <Refusal
                    error={
                        saveJob.error ??
                        clear.error ??
                        hire.error ??
                        setUp.error
                    }
                />
            </form>
        </Card>
    );
}
