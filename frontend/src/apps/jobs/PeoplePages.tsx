import {
    ComboBox,
    CommandButton,
    ListView,
    RadioButton,
    RadioGroup,
    Stack,
    TextBox,
} from '@partytown/ui';
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
import {
    Actions,
    Card,
    Field,
    Fields,
    Jump,
    Page,
    PageHead,
    Refusal,
} from './parts';
import { SkillsPanel } from './SkillsPanel';
import { WantsPanel } from './WantsPanel';
import { useRefreshWork, useWork } from './work-context';

export function PeoplePage() {
    const { people, workOf, go } = useWork();
    const working = people.filter((p) => p.title).length;
    return (
        <>
            <PageHead
                icon="users"
                title="Everyone"
                sub={`${working} of ${people.length} in work. Pick someone to see or change it.`}
            />
            <Page>
                <ListView
                    label="Everyone"
                    activateOn="single"
                    onActivate={(id) => go({ page: 'person', id })}
                    columns={[
                        { key: 'who', header: 'Who', width: 'max-content' },
                        {
                            key: 'does',
                            header: 'Does',
                            width: 'minmax(160px, 2fr)',
                            wrap: true,
                        },
                        { key: 'when', header: 'When' },
                        { key: 'today', header: 'Today', width: 'max-content' },
                    ]}
                    items={people.map((p) => ({
                        id: p.participantId,
                        label: p.name,
                        cells: {
                            does: <JobCell person={p} work={workOf(p)} />,
                            when: p.days ? `${p.days} ${p.hours}` : '',
                            today: shiftLabel(p),
                        },
                    }))}
                />
            </Page>
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
                icon="user"
                title={person.name}
                sub={[jobLine(person, work), where].filter(Boolean).join(' · ')}
                chip={shiftLabel(person) || undefined}
            />
            <Page>
                {work.kind === 'company' ? (
                    <p>
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
            </Page>
        </>
    );
}

type Mode = 'job' | 'company' | 'practice';

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
            <Stack
                as="form"
                gap="md"
                onSubmit={(e) => {
                    e.preventDefault();
                    save();
                }}
            >
                <RadioGroup
                    name={`work-${person.participantId}`}
                    aria-label="Kind of work"
                    orientation="horizontal"
                    value={mode}
                    onValueChange={(value) => setMode(value as Mode)}
                >
                    {modes.map(([value, label]) => (
                        <RadioButton
                            key={value}
                            value={value}
                            disabled={
                                value === 'company' && companies.length === 0
                            }
                        >
                            {label}
                        </RadioButton>
                    ))}
                </RadioGroup>
                <Fields>
                    <Field label="Title" grow>
                        <TextBox
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
                            <ComboBox
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
                            </ComboBox>
                        </Field>
                    ) : (
                        <Field label="Workplace" grow>
                            <TextBox
                                value={workplace}
                                placeholder="the Café, the office in Zuid"
                                onChange={(e) =>
                                    setWorkplace(e.currentTarget.value)
                                }
                            />
                        </Field>
                    )}
                    <Field label="Days">
                        <TextBox
                            className="w-[96px]"
                            value={days}
                            onChange={(e) => setDays(e.currentTarget.value)}
                        />
                    </Field>
                    <Field label="Hours">
                        <TextBox
                            className="w-[96px]"
                            value={hours}
                            onChange={(e) => setHours(e.currentTarget.value)}
                        />
                    </Field>
                </Fields>
                {mode === 'practice' ? (
                    <Fields>
                        <Field label="For" grow>
                            <TextBox
                                value={field}
                                placeholder="careers in logistics"
                                onChange={(e) =>
                                    setField(e.currentTarget.value)
                                }
                            />
                        </Field>
                        <Field label="How they work" grow>
                            <TextBox
                                value={approach}
                                placeholder="blunt, practical, sets homework"
                                onChange={(e) =>
                                    setApproach(e.currentTarget.value)
                                }
                            />
                        </Field>
                        <Field label="Fee">
                            <TextBox
                                className="w-[56px]"
                                value={fee}
                                onChange={(e) => setFee(e.currentTarget.value)}
                            />
                        </Field>
                    </Fields>
                ) : null}
                <Actions>
                    <CommandButton
                        type="submit"
                        isDefault
                        disabled={!ready || pending}
                    >
                        Save
                    </CommandButton>
                    <CommandButton
                        tone="caution"
                        disabled={!person.title || pending}
                        onClick={() => clear.mutate({ partyId, participantId })}
                    >
                        No job
                    </CommandButton>
                </Actions>
                <Refusal
                    error={
                        saveJob.error ??
                        clear.error ??
                        hire.error ??
                        setUp.error
                    }
                />
            </Stack>
        </Card>
    );
}
