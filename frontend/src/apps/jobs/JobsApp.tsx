import { useQueryClient } from '@tanstack/react-query';
import { Suspense, useId, useState } from 'react';
import {
    getGetWorldsPartyIdJobsQueryKey,
    useDeleteWorldsPartyIdJobsParticipantId,
    useGetWorldsPartyIdJobsSuspense,
    usePutWorldsPartyIdJobsParticipantId,
} from '#api/party-zone';
import type { JobSummary } from '../../api/model';
import { WORLD_ID } from '../../lib/world';
import { shiftLabel } from './jobs-lib';
import { WantsPanel } from './WantsPanel';

const panel = {
    background: '#fff',
    border: '1px solid',
    borderColor: '#7f9db9 #fff #fff #7f9db9',
} as const;

/** Work & Wants (ADR 0026): who works where and when, how today's shift went, and what each person wants. */
export default function JobsApp() {
    const jobs = useGetWorldsPartyIdJobsSuspense(WORLD_ID).data.data ?? [];
    const [picked, setPicked] = useState<string | null>(null);
    const selected = jobs.find((j) => j.participantId === picked) ?? null;

    return (
        <div className="app-surface flex h-full flex-col" style={{ gap: 6 }}>
            <div
                className="min-h-0 flex-1 overflow-y-auto"
                style={{ ...panel, margin: '6px 6px 0' }}
            >
                <table
                    className="w-full"
                    style={{ borderCollapse: 'collapse' }}
                >
                    <thead>
                        <tr
                            style={{ background: '#ece9d8', textAlign: 'left' }}
                        >
                            <th style={{ padding: '2px 6px' }}>Who</th>
                            <th style={{ padding: '2px 6px' }}>Job</th>
                            <th style={{ padding: '2px 6px' }}>Where</th>
                            <th style={{ padding: '2px 6px' }}>When</th>
                            <th style={{ padding: '2px 6px' }}>Today</th>
                        </tr>
                    </thead>
                    <tbody>
                        {jobs.map((j) => (
                            <tr
                                key={j.participantId}
                                onClick={() => setPicked(j.participantId)}
                                aria-selected={j.participantId === picked}
                                style={{
                                    cursor: 'pointer',
                                    background:
                                        j.participantId === picked
                                            ? '#316ac5'
                                            : undefined,
                                    color:
                                        j.participantId === picked
                                            ? '#fff'
                                            : undefined,
                                }}
                            >
                                <td style={{ padding: '2px 6px' }}>{j.name}</td>
                                <td style={{ padding: '2px 6px' }}>
                                    {j.title ??
                                        (j.seeded ? '—' : 'working it out…')}
                                </td>
                                <td style={{ padding: '2px 6px' }}>
                                    {j.workplace ? (
                                        <span
                                            title={
                                                j.onscreen
                                                    ? 'A Place in this world'
                                                    : 'Offscreen'
                                            }
                                        >
                                            {j.onscreen ? '📍' : '🏢'}{' '}
                                            {j.workplace}
                                        </span>
                                    ) : null}
                                </td>
                                <td style={{ padding: '2px 6px' }}>
                                    {j.days ? `${j.days} ${j.hours}` : null}
                                </td>
                                <td style={{ padding: '2px 6px' }}>
                                    {shiftLabel(j)}
                                </td>
                            </tr>
                        ))}
                    </tbody>
                </table>
            </div>
            {selected ? (
                <>
                    <JobForm key={selected.participantId} job={selected} />
                    <Suspense fallback={null}>
                        <WantsPanel
                            key={`w-${selected.participantId}`}
                            participantId={selected.participantId}
                            name={selected.name}
                            nameOf={(id) =>
                                jobs.find((j) => j.participantId === id)
                                    ?.name ?? 'someone'
                            }
                        />
                    </Suspense>
                </>
            ) : (
                <p style={{ margin: '0 8px 8px', color: '#666' }}>
                    Personas read their job and what they want off who they are.
                    Pick someone to see or change it.
                </p>
            )}
        </div>
    );
}

function JobForm({ job }: { job: JobSummary }) {
    const ids = useId();
    const queryClient = useQueryClient();
    const [title, setTitle] = useState(job.title ?? '');
    const [workplace, setWorkplace] = useState(job.workplace ?? '');
    const [days, setDays] = useState(job.days ?? 'Mon-Fri');
    const [hours, setHours] = useState(job.hours ?? '09:00-17:00');
    const refresh = {
        mutation: {
            onSuccess: () =>
                queryClient.invalidateQueries({
                    queryKey: getGetWorldsPartyIdJobsQueryKey(WORLD_ID),
                }),
        },
    };
    const save = usePutWorldsPartyIdJobsParticipantId(refresh);
    const clear = useDeleteWorldsPartyIdJobsParticipantId(refresh);
    const error = save.error ?? clear.error;

    return (
        <fieldset style={{ margin: '0 6px 6px' }}>
            <legend>{job.name}'s job</legend>
            <form
                className="flex flex-wrap items-center"
                style={{ gap: 6 }}
                onSubmit={(e) => {
                    e.preventDefault();
                    save.mutate({
                        partyId: WORLD_ID,
                        participantId: job.participantId,
                        data: { title, workplace, days, hours },
                    });
                }}
            >
                <label htmlFor={`${ids}-t`}>Job</label>
                <input
                    id={`${ids}-t`}
                    value={title}
                    placeholder="barista"
                    onChange={(e) => setTitle(e.currentTarget.value)}
                    style={{ width: 130 }}
                />
                <label htmlFor={`${ids}-w`}>at</label>
                <input
                    id={`${ids}-w`}
                    value={workplace}
                    placeholder="the Café, the office in Zuid"
                    onChange={(e) => setWorkplace(e.currentTarget.value)}
                    style={{ flex: 1, minWidth: 140 }}
                />
                <label htmlFor={`${ids}-d`}>on</label>
                <input
                    id={`${ids}-d`}
                    value={days}
                    onChange={(e) => setDays(e.currentTarget.value)}
                    style={{ width: 80 }}
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
                        !title.trim() || !workplace.trim() || save.isPending
                    }
                >
                    Save
                </button>
                <button
                    type="button"
                    disabled={!job.title || clear.isPending}
                    onClick={() =>
                        clear.mutate({
                            partyId: WORLD_ID,
                            participantId: job.participantId,
                        })
                    }
                >
                    No job
                </button>
            </form>
            {error ? (
                <p style={{ color: '#a00', margin: '4px 0 0' }}>
                    Couldn't save that. Hours look like 09:00-17:00, within one
                    day.
                </p>
            ) : null}
        </fieldset>
    );
}
