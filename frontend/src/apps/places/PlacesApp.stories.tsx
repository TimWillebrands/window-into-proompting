import type { Meta, StoryObj } from '@storybook/react-vite';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { type ReactNode, useState } from 'react';
import {
    getGetWorldsPartyIdGrowthQueryKey,
    getGetWorldsPartyIdLivesQueryKey,
    getGetWorldsPartyIdPlacesAppearanceOptionsQueryKey,
    getGetWorldsPartyIdPlacesQueryKey,
    getGetWorldsPartyIdPlacesSituationParticipantIdQueryKey,
    getGetWorldsPartyIdSelvesQueryKey,
} from '#api/party-zone';
import type { Body } from '../../api/model/body';
import type { FurniView } from '../../api/model/furniView';
import type { Place } from '../../api/model/place';
import StoryWindow from '../../components/desktop/StoryWindow';
import { usePlayerSession, WORLD_ID } from '../../lib/world';
import PlacesApp from './PlacesApp';

const ME = 'player-1';
const NOW = Date.parse('2026-11-22T14:00:00Z');

const cafe: Place = {
    id: 'cafe',
    name: 'the Corner Café',
    kind: 'public',
    scene: 'cafe',
    talkRoomId: 'cg-1',
    description:
        'A narrow café with a long counter, a booth by the radiator and a jukebox that only plays Bowie.',
    spots: [
        { id: 'counter', name: 'Counter', capacity: 3, poses: ['lean'] },
        { id: 'booth', name: 'Booth', capacity: 4, poses: ['lounge'] },
        { id: 'window-table', name: 'Window table', capacity: 2 },
        { id: 'jukebox', name: 'Jukebox', capacity: 2, poses: ['dance'] },
    ],
};

const lobby: Place = {
    id: 'lobby',
    name: 'the Lobby',
    kind: 'public',
    scene: 'lobby',
    description: 'Marble floors, a reception desk, two sofas.',
    spots: [{ id: 'sofas', name: 'Sofas', capacity: 4 }],
};

const park: Place = {
    id: 'park',
    name: 'Rooftop bar',
    kind: 'public',
    customScene: '{}',
    description: 'Fairy lights over the city.',
    spots: [{ id: 'bar', name: 'Bar', capacity: 6 }],
};

const home: Place = {
    id: 'home',
    name: "Ana's flat",
    kind: 'home',
    scene: 'home',
    ownerId: ME,
    description: 'A small flat above the bakery.',
    spots: [{ id: 'sofa', name: 'Sofa', capacity: 2 }],
};

const theirs: Place = {
    id: 'theirs',
    name: "Bo's place",
    kind: 'home',
    scene: 'home',
    ownerId: 'bo',
    spots: [{ id: 'sofa', name: 'Sofa', capacity: 2 }],
};

const body = (
    participantId: string,
    name: string,
    spotId: string,
    extra: Partial<Body> = {},
): Body => ({
    participantId,
    name,
    placeId: 'cafe',
    spotId,
    posture: 'Sitting',
    appearance: {
        skin: 'fair',
        hair: 'brown',
        hairStyle: 'short',
        top: 'teal',
    } as Body['appearance'],
    ...extra,
});

const bodies: Body[] = [
    body(ME, 'Ana Lopez', 'counter', { isHuman: true }),
    body('bo', 'Bo Berg', 'counter', { activity: 'on the phone' }),
    body('cy', 'Cy Dorn', 'booth', { asleep: true }),
    body('dee', 'Dee Ek', 'lobby-sofas', { placeId: 'lobby' }),
];

const furni: FurniView[] = [
    {
        placeId: 'cafe',
        furniId: 'jukebox',
        kind: 'jukebox',
        name: 'Jukebox',
        spotId: 'jukebox',
        useLabel: 'Put a record on',
        on: true,
    },
    {
        placeId: 'cafe',
        furniId: 'espresso',
        kind: 'coffee',
        name: 'Espresso machine',
        spotId: 'counter',
        condition: 0.2,
        careLabel: 'Try descaling it',
        notable: 'The espresso machine is spluttering',
    },
    {
        placeId: 'cafe',
        furniId: 'guestbook',
        kind: 'decor',
        name: 'Guestbook',
        spotId: 'booth',
        useLabel: 'Sign the guestbook',
        gives: 'a memory',
        leaves: true,
    },
];

const ok = <T,>(data: T) => ({ data, status: 200, headers: new Headers() });

type Scenario = {
    situation?: string;
    allEars?: boolean;
    bodies?: Body[];
    places?: Place[];
    width?: number;
};

function seed(client: QueryClient, scenario: Scenario) {
    client.setQueryData(
        getGetWorldsPartyIdPlacesQueryKey(WORLD_ID),
        ok({
            places: scenario.places ?? [cafe, lobby, park, home, theirs],
            bodies: scenario.bodies ?? bodies,
            furni,
            now: NOW,
        }),
    );
    client.setQueryData(
        getGetWorldsPartyIdLivesQueryKey(WORLD_ID),
        ok(
            bodies.map((b) => ({
                personaId: b.participantId,
                name: b.name,
            })),
        ),
    );
    client.setQueryData(
        getGetWorldsPartyIdSelvesQueryKey(WORLD_ID),
        ok([
            {
                personaId: 'bo',
                name: 'Bo Berg',
                mood: 'restless',
                doing: 'Waiting for a call back about the flat on Canal Street.',
            },
        ]),
    );
    client.setQueryData(
        getGetWorldsPartyIdPlacesSituationParticipantIdQueryKey(WORLD_ID, ME),
        ok({ situation: scenario.situation ?? null }),
    );
    client.setQueryData(
        getGetWorldsPartyIdPlacesAppearanceOptionsQueryKey(WORLD_ID),
        ok([
            { key: 'hair', label: 'Hair', options: ['brown', 'pink'] },
            { key: 'top', label: 'Top', options: ['teal', 'navy'] },
        ]),
    );
    client.setQueryData(
        getGetWorldsPartyIdGrowthQueryKey(WORLD_ID),
        ok({
            auto: true,
            openAt: 10,
            rows: [
                {
                    localeId: 'l1',
                    name: 'Night market',
                    district: 'Zuid',
                    footfall: 6,
                    people: 3,
                    days: 2,
                    missing: 'a regular',
                    contributors: ['Bo', 'Cy'],
                },
            ],
            opened: [{ placeId: 'park', name: 'Rooftop bar', district: null }],
        }),
    );
    client.setQueryData(['world-player', WORLD_ID, 'user-1'], {
        playerId: ME,
        name: 'Ana Lopez',
    });
}

function Seeded({
    scenario,
    children,
}: {
    scenario: Scenario;
    children: ReactNode;
}) {
    const [client] = useState(() => {
        const fresh = storyClient();
        seed(fresh, scenario);
        return fresh;
    });
    return (
        <QueryClientProvider client={client}>{children}</QueryClientProvider>
    );
}

const signIn = () => {
    const before = usePlayerSession.getState();
    usePlayerSession.setState({ userId: 'user-1', hydrated: true });
    return () =>
        usePlayerSession.setState({
            userId: before.userId,
            hydrated: before.hydrated,
        });
};

const storyClient = () =>
    new QueryClient({
        defaultOptions: {
            queries: {
                retry: false,
                staleTime: Number.POSITIVE_INFINITY,
                gcTime: Number.POSITIVE_INFINITY,
            },
            mutations: { retry: false },
        },
    });

const meta = {
    title: 'Apps/PlacesApp',
    component: PlacesApp,
    parameters: { layout: 'fullscreen' },
    args: {},
    beforeEach: ({ parameters }) => {
        const scenario = (parameters.scenario ?? {}) as Scenario;
        window.localStorage.setItem(
            'proompting.places.allEars',
            scenario.allEars ? '1' : '0',
        );
        return signIn();
    },
    decorators: [
        (Story, context) => {
            const scenario = (context.parameters.scenario ?? {}) as Scenario;
            return (
                <Seeded scenario={scenario}>
                    <div
                        className="h-[720px] p-6"
                        style={{ width: (scenario.width ?? 1040) + 40 }}
                    >
                        {/* biome-ignore lint/correctness/useUniqueElementIds: stories render a single instance */}
                        <StoryWindow
                            id="places"
                            title="Places"
                            icon="/img/icons/places.ico"
                        >
                            <Story />
                        </StoryWindow>
                    </div>
                </Seeded>
            );
        },
    ],
} satisfies Meta<typeof PlacesApp>;

export default meta;

type Story = StoryObj<typeof meta>;

export const AtTheCafe: Story = {
    parameters: {
        scenario: {
            situation:
                'You are leaning on the counter next to Bo, who keeps checking his phone.',
        },
    },
};

export const AllEars: Story = { parameters: { scenario: { allEars: true } } };

export const Narrow: Story = {
    parameters: { scenario: { width: 760 } },
};

export const Compact: Story = {
    parameters: { scenario: { width: 480 } },
};

export const Travelling: Story = {
    parameters: {
        scenario: {
            bodies: [
                body(ME, 'Ana Lopez', 'counter', {
                    destPlaceId: 'lobby',
                    destSpotId: 'sofas',
                    departedAt: NOW - 20_000,
                    arriveAt: NOW + 40_000,
                }),
                ...bodies.slice(1),
            ],
        },
    },
};

export const AtHome: Story = {
    parameters: {
        scenario: {
            places: [
                cafe,
                lobby,
                { ...home, knocks: { bo: NOW } as Place['knocks'] },
            ],
            bodies: [
                body(ME, 'Ana Lopez', 'sofa', { placeId: 'home' }),
                ...bodies.slice(1),
            ],
        },
    },
};

export const Profile: Story = {
    play: async ({ canvasElement }) => {
        await new Promise((r) => setTimeout(r, 500));
        canvasElement
            .querySelector<HTMLButtonElement>('button[title="on the phone"]')
            ?.click();
    },
};

export const Arranging: Story = {
    ...AtHome,
    play: async ({ canvasElement }) => {
        await new Promise((r) => setTimeout(r, 500));
        const arrange = Array.from(
            canvasElement.querySelectorAll<HTMLButtonElement>('button'),
        ).find((b) => b.textContent?.includes('Arrange'));
        arrange?.click();
    },
};
