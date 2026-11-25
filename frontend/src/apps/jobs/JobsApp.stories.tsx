import type { Meta, StoryObj } from '@storybook/react-vite';
import {
    getGetWorldsPartyIdCompaniesQueryKey,
    getGetWorldsPartyIdDesiresQueryKey,
    getGetWorldsPartyIdJobsQueryKey,
    getGetWorldsPartyIdPracticesQueryKey,
    getGetWorldsPartyIdSessionsQueryKey,
    getGetWorldsPartyIdSkillsQueryKey,
} from '#api/party-zone';
import type {
    CompanySummary,
    JobSummary,
    PracticeSummary,
    SessionSummary,
} from '../../api/model';
import { seeded } from '../../components/chat/story-seed';
import StoryWindow from '../../components/desktop/StoryWindow';
import { WindowInstanceProvider } from '../../components/desktop/window-instance-context';
import { WORLD_ID } from '../../lib/world';
import JobsApp from './JobsApp';

const now = Date.now();

const person = (
    participantId: string,
    name: string,
    job: Partial<JobSummary> = {},
): JobSummary => ({
    participantId,
    name,
    seeded: true,
    title: null,
    workplace: null,
    onscreen: true,
    days: null,
    hours: null,
    shiftDay: null,
    shiftStatus: null,
    lateMinutes: 0,
    paid: 0,
    ...job,
});

const people: JobSummary[] = [
    person('persona-ada', 'Ada', {
        title: 'Analyst',
        workplace: 'place-office',
        days: 'Mon–Fri',
        hours: '09:00–17:00',
        shiftDay: 'today',
        shiftStatus: 'OnShift',
        paid: 240,
    }),
    person('persona-linus', 'Linus', {
        title: 'Counsellor',
        workplace: 'place-sofa',
        days: 'Tue–Thu',
        hours: '10:00–16:00',
    }),
    person('persona-hedy', 'Hedy', {
        title: 'Barista',
        workplace: 'place-cafe',
        days: 'Sat–Sun',
        hours: '08:00–14:00',
    }),
    person('player-grace', 'Grace'),
];

const companies: CompanySummary[] = [
    {
        id: 'company-1',
        name: 'Kettle Capital',
        kind: 'Fund',
        mandate: 'Back the World’s best guesses.',
        market: 'forecasts',
        focus: 'weather',
        workplace: 'place-office',
        capital: 1000,
        cash: 620,
        value: 1084,
        brier: 0.21,
        markedAt: now - 3_600_000,
        staff: [{ participantId: 'persona-ada', name: 'Ada', brier: 0.19 }],
        positions: [
            {
                instrumentId: 'rain-saturday',
                title: 'Rain on Saturday',
                units: 40,
                cost: 0.4,
                mark: 0.55,
                value: 22,
                fresh: true,
            },
        ],
        watchlist: [
            {
                instrumentId: 'picnic-happens',
                title: 'The picnic happens',
                bid: 0.7,
                ask: 0.74,
                previous: 0.68,
                open: true,
            },
        ],
        trades: [
            {
                id: 'trade-1',
                at: now - 7_200_000,
                traderId: 'persona-ada',
                traderName: 'Ada',
                instrumentId: 'rain-saturday',
                title: 'Rain on Saturday',
                side: 'Buy',
                units: 40,
                price: 0.4,
                fee: 0.2,
                realized: null,
                thesis: 'The swallows are flying low.',
                forecast: 0.55,
                brier: null,
            },
        ],
    },
];

const practices: PracticeSummary[] = [
    {
        participantId: 'persona-linus',
        name: 'Linus',
        title: 'Counsellor',
        workplace: 'place-sofa',
        days: 'Tue–Thu',
        hours: '10:00–16:00',
        field: 'grief and kettles',
        approach: 'Listens first, then asks one good question.',
        fee: 12,
    },
];

const sessions: SessionSummary[] = [
    {
        id: 'session-1',
        practitionerId: 'persona-linus',
        practitionerName: 'Linus',
        clientId: 'persona-hedy',
        clientName: 'Hedy',
        workplace: 'place-sofa',
        reason: 'Borrowed the kettle and feels bad about it.',
        startsAt: now + 3_600_000,
        startedAt: null,
        endedAt: null,
        status: 'Booked',
        fee: 12,
        paid: false,
        homework: null,
        homeworkKept: null,
    },
];

const meta = {
    title: 'Apps/JobsApp',
    component: JobsApp,
    parameters: { layout: 'fullscreen' },
    decorators: [
        (Story, { parameters }) => (
            <div
                className={
                    parameters.narrow
                        ? 'h-[600px] w-[480px] p-6'
                        : 'h-[600px] w-[840px] p-6'
                }
            >
                {/* biome-ignore lint/correctness/useUniqueElementIds: stories render single instance */}
                <StoryWindow id="jobs" title="Work" icon="jobs">
                    <WindowInstanceProvider windowId="jobs">
                        <Story />
                    </WindowInstanceProvider>
                </StoryWindow>
            </div>
        ),
        seeded([
            [getGetWorldsPartyIdJobsQueryKey(WORLD_ID), people],
            [getGetWorldsPartyIdCompaniesQueryKey(WORLD_ID), companies],
            [getGetWorldsPartyIdPracticesQueryKey(WORLD_ID), practices],
            [getGetWorldsPartyIdSessionsQueryKey(WORLD_ID), sessions],
            [getGetWorldsPartyIdSkillsQueryKey(WORLD_ID), []],
            [getGetWorldsPartyIdDesiresQueryKey(WORLD_ID), []],
        ]),
    ],
} satisfies Meta<typeof JobsApp>;

export default meta;
type Story = StoryObj<typeof meta>;

export const Everyone: Story = {};

export const Person: Story = { args: { page: 'person', id: 'persona-ada' } };

export const Company: Story = { args: { page: 'company', id: 'company-1' } };

export const Practices: Story = { args: { page: 'practices' } };

export const Doors: Story = { args: { page: 'doors' } };

export const Narrow: Story = {
    ...Person,
    parameters: { narrow: true },
};
