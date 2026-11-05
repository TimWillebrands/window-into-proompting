import { useQueryClient } from '@tanstack/react-query';
import { createContext, useContext, useMemo } from 'react';
import {
    getGetWorldsPartyIdCompaniesQueryKey,
    getGetWorldsPartyIdJobsQueryKey,
    getGetWorldsPartyIdPracticesQueryKey,
    getGetWorldsPartyIdSessionsQueryKey,
    useGetWorldsPartyIdCompaniesSuspense,
    useGetWorldsPartyIdJobsSuspense,
    useGetWorldsPartyIdPracticesSuspense,
    useGetWorldsPartyIdSessionsSuspense,
} from '#api/party-zone';
import type {
    CompanySummary,
    JobSummary,
    PracticeSummary,
    SessionSummary,
} from '../../api/model';
import { WORLD_ID } from '../../lib/world';
import { type Employment, employmentOf, type WorkPlace } from './jobs-lib';

interface Work {
    people: JobSummary[];
    companies: CompanySummary[];
    practices: PracticeSummary[];
    sessions: SessionSummary[];
    go: (place: WorkPlace) => void;
    workOf: (person: JobSummary) => Employment;
    nameOf: (id: string) => string;
}

const WorkContext = createContext<Work | null>(null);

const live = { query: { refetchInterval: 30_000 } };

/** One read of the labour market for the whole window, so every page agrees. */
export function WorkProvider({
    go,
    children,
}: {
    go: (place: WorkPlace) => void;
    children: React.ReactNode;
}) {
    const people = useGetWorldsPartyIdJobsSuspense(WORLD_ID, live).data.data;
    const companies = useGetWorldsPartyIdCompaniesSuspense(WORLD_ID, live).data
        .data;
    const practices = useGetWorldsPartyIdPracticesSuspense(WORLD_ID, live).data
        .data;
    const sessions = useGetWorldsPartyIdSessionsSuspense(WORLD_ID, live).data
        .data;

    const work = useMemo<Work>(
        () => ({
            people: people ?? [],
            companies: companies ?? [],
            practices: practices ?? [],
            sessions: sessions ?? [],
            go,
            workOf: (person) =>
                employmentOf(person, companies ?? [], practices ?? []),
            nameOf: (id) =>
                people?.find((p) => p.participantId === id)?.name ?? 'someone',
        }),
        [people, companies, practices, sessions, go],
    );
    return <WorkContext.Provider value={work}>{children}</WorkContext.Provider>;
}

export function useWork(): Work {
    const work = useContext(WorkContext);
    if (!work) throw new Error('useWork must be called inside a WorkProvider');
    return work;
}

/** Mutation options that re-read everything: a hire changes a person, a Company and the front page. */
export function useRefreshWork() {
    const queryClient = useQueryClient();
    return {
        mutation: {
            onSuccess: () =>
                Promise.all(
                    [
                        getGetWorldsPartyIdJobsQueryKey(WORLD_ID),
                        getGetWorldsPartyIdCompaniesQueryKey(WORLD_ID),
                        getGetWorldsPartyIdPracticesQueryKey(WORLD_ID),
                        getGetWorldsPartyIdSessionsQueryKey(WORLD_ID),
                    ].map((queryKey) =>
                        queryClient.invalidateQueries({ queryKey }),
                    ),
                ),
        },
    };
}
