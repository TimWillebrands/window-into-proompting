import {
    ChatLog,
    ChatMessage,
    CheckBox,
    ComboBox,
    CommandButton,
    FieldRow,
    GroupBox,
    HudPanel,
    Icon,
    ListView,
    ProgressBar,
    TaskPane,
    TaskPaneSection,
    Text,
    TextBox,
    type Tone,
    Toolbar,
    ToolbarButton,
} from '@partytown/ui';
import { useQueryClient } from '@tanstack/react-query';
import {
    lazy,
    type ReactNode,
    Suspense,
    useEffect,
    useLayoutEffect,
    useMemo,
    useRef,
    useState,
} from 'react';
import {
    getGetWorldsPartyIdPlacesQueryKey,
    useGetWorldsPartyIdLivesSuspense,
    useGetWorldsPartyIdPlacesAppearanceOptions,
    useGetWorldsPartyIdPlacesSituationParticipantId,
    useGetWorldsPartyIdPlacesSuspense,
    useGetWorldsPartyIdSelves,
    usePostPartyIdPrompt,
    usePostWorldsPartyIdPlacesDecorate,
    usePostWorldsPartyIdPlacesFurni,
    usePostWorldsPartyIdPlacesJoin,
    usePostWorldsPartyIdPlacesKnock,
    usePostWorldsPartyIdPlacesLetIn,
    usePostWorldsPartyIdPlacesMove,
    usePostWorldsPartyIdPlacesRestyle,
    usePostWorldsPartyIdPlacesThingsArrange,
    usePostWorldsPartyIdPlacesVoice,
    usePutWorldsPartyIdPlacesAppearance,
} from '#api/party-zone';
import { useRealtimeStoreActions } from '#lib/realtime-store';
import type { Body } from '../../api/model/body';
import type { FurniView } from '../../api/model/furniView';
import type { Place } from '../../api/model/place';
import type { PlacesSnapshot } from '../../api/model/placesSnapshot';
import { useRoomConversation } from '../../lib/use-room-conversation';
import { usePlayerId, useWorldEvents, WORLD_ID } from '../../lib/world';
import type { WorldEventData } from '../../lib/world-bus';
import GrowingPane from './GrowingPane';
import { lookOf } from './iso/appearance';
import FaceGlyph from './iso/FaceGlyph';
import PlaceCanvas from './iso/PlaceCanvas';
import { ArrangeTools, MakerTools, PoseBar } from './PlaceTools';

const PlaceScene = lazy(() => import('./scene/PlaceScene'));

const webgl2 = (() => {
    try {
        return !!document.createElement('canvas').getContext('webgl2');
    } catch {
        return false;
    }
})();

import {
    avatarColour,
    awayLines,
    bodiesIn,
    bodySpotId,
    earshotRuns,
    furniIcon,
    hosts,
    isLocale,
    livesIn,
    mayEnter,
    navigatorPlaces,
    occupancy,
    placeIcon,
    placeLabel,
    postureIcon,
    scribble,
    serverNow,
    setServerClock,
    toMs,
    volumeOf,
    walkingText,
    workplaceDoor,
} from './places-lib';

export default function PlacesApp() {
    const playerId = usePlayerId();
    const queryClient = useQueryClient();
    const placesQuery = useGetWorldsPartyIdPlacesSuspense(WORLD_ID);
    const snapshot = placesQuery.data.data;
    setServerClock(toMs(snapshot.now), placesQuery.dataUpdatedAt);
    const allPlaces = snapshot.places ?? [];
    const places = allPlaces.filter((p) => !isLocale(p));
    const bodies = snapshot.bodies ?? [];
    const me = bodies.find((b) => b.participantId === playerId);
    const [selectedId, setSelectedId] = useState<string | null>(null);
    const [allEars, setAllEars] = useAllEars();

    const { connectPartyRealtime, disconnectPartyRealtime } =
        useRealtimeStoreActions();
    useEffect(() => {
        connectPartyRealtime(WORLD_ID);
        return () => disconnectPartyRealtime(WORLD_ID);
    }, [connectPartyRealtime, disconnectPartyRealtime]);

    const cooling = useRef<ReturnType<typeof setTimeout> | null>(null);
    const again = useRef(false);
    useEffect(
        () => () => {
            clearTimeout(cooling.current ?? undefined);
            cooling.current = null;
            again.current = false;
        },
        [],
    );
    const patchBody = usePatchBody();
    useWorldEvents((envelope) => {
        if (!envelope.type.startsWith('world.')) return;
        const event = envelope.data as WorldEventData<Body>;
        const kind = event.kind;
        if (kind !== 'body' && kind !== 'place') return;
        if (kind === 'body') void patchBody(event.payload);
        if (cooling.current) {
            again.current = true;
            return;
        }
        const refresh = () => {
            const prefix = getGetWorldsPartyIdPlacesQueryKey(WORLD_ID)[0];
            queryClient.invalidateQueries({
                predicate: (q) =>
                    typeof q.queryKey[0] === 'string' &&
                    q.queryKey[0].startsWith(prefix) &&
                    !q.queryKey[0].includes('/appearance'),
            });
        };
        const cool = () => {
            cooling.current = setTimeout(() => {
                cooling.current = null;
                if (!again.current) return;
                again.current = false;
                refresh();
                cool();
            }, 150);
        };
        refresh();
        cool();
    });

    const selected =
        places.find((p) => p.id === selectedId) ??
        places.find((p) => p.id === (me?.destPlaceId ?? me?.placeId)) ??
        places[0];

    return (
        <div className="flex h-full min-h-0">
            <Navigator
                place={selected}
                places={navigatorPlaces(places, playerId)}
                away={awayLines(bodies, allPlaces)}
                bodies={bodies}
                me={me}
                playerId={playerId}
                selectedId={selected?.id}
                onSelect={setSelectedId}
                allEars={allEars}
                onAllEars={setAllEars}
            />
            {selected ? (
                <PlaceView
                    place={selected}
                    bodies={bodiesIn(bodies, selected.id)}
                    allBodies={bodies}
                    places={places}
                    furni={(snapshot.furni ?? []).filter(
                        (f) => f.placeId === selected.id,
                    )}
                    me={me}
                    playerId={playerId}
                    allEars={allEars}
                />
            ) : null}
        </div>
    );
}

const OCCUPANCY_TONE = {
    calm: 'neutral',
    busy: 'caution',
    full: 'danger',
} as const satisfies Record<'calm' | 'busy' | 'full', Tone | undefined>;

const ALL_EARS_KEY = 'proompting.places.allEars';

function useAllEars(): [boolean, (on: boolean) => void] {
    const [on, setOn] = useState(false);
    useEffect(
        () => setOn(window.localStorage.getItem(ALL_EARS_KEY) === '1'),
        [],
    );
    return [
        on,
        (next) => {
            window.localStorage.setItem(ALL_EARS_KEY, next ? '1' : '0');
            setOn(next);
        },
    ];
}

function useTicker(on: boolean) {
    const [now, setNow] = useState(serverNow);
    useEffect(() => {
        if (!on) return;
        setNow(serverNow());
        const t = setInterval(() => setNow(serverNow()), 1000);
        return () => clearInterval(t);
    }, [on]);
    return now;
}

function usePatchBody() {
    const queryClient = useQueryClient();
    return async (body: Body | null | undefined) => {
        if (!body?.participantId) return;
        const queryKey = getGetWorldsPartyIdPlacesQueryKey(WORLD_ID);
        await queryClient.cancelQueries({ queryKey });
        queryClient.setQueryData(
            queryKey,
            (old: { data: PlacesSnapshot } | undefined) =>
                old && {
                    ...old,
                    data: {
                        ...old.data,
                        bodies: (old.data.bodies ?? []).map((b) =>
                            b.participantId === body.participantId ? body : b,
                        ),
                    },
                },
            { updatedAt: queryClient.getQueryState(queryKey)?.dataUpdatedAt },
        );
    };
}

function Navigator({
    place,
    places,
    away,
    bodies,
    me,
    playerId,
    selectedId,
    onSelect,
    allEars,
    onAllEars,
}: {
    place: Place | undefined;
    places: Place[];
    away: { participantId: string; text: string }[];
    bodies: Body[];
    me: Body | undefined;
    playerId: string | null;
    selectedId: string | undefined;
    onSelect: (id: string) => void;
    allEars: boolean;
    onAllEars: (on: boolean) => void;
}) {
    const visit = usePostWorldsPartyIdPlacesJoin();
    const patchBody = usePatchBody();
    const sections: [string, Place[]][] = [
        [
            'Out and about',
            places.filter(
                (p) => p.kind !== 'home' && !p.customScene && !p.dormant,
            ),
        ],
        [
            'Made by the World',
            places.filter(
                (p) => p.kind !== 'home' && !!p.customScene && !p.dormant,
            ),
        ],
        ['Homes', places.filter((p) => p.kind === 'home' && !p.vacated)],
        ['Quiet lately', places.filter((p) => p.kind !== 'home' && p.dormant)],
    ];
    const here = place ? bodiesIn(bodies, place.id).length : 0;
    const fullness = place
        ? occupancy(place, here)
        : { share: 0, level: 'calm' as const };
    return (
        <TaskPane
            role="navigation"
            aria-label="Places"
            className="w-[272px] shrink-0"
        >
            {place ? (
                <TaskPaneSection
                    special
                    collapsible={false}
                    title={placeLabel(place, playerId)}
                    icon={<Icon name={placeIcon(place, playerId)} size={32} />}
                >
                    {place.description ? (
                        <p title={place.description}>{place.description}</p>
                    ) : null}
                    <p>{here === 0 ? 'Nobody here.' : `${here} here.`}</p>
                    <ProgressBar
                        value={fullness.share * 100}
                        tone={OCCUPANCY_TONE[fullness.level]}
                        label="How full it is"
                        valueText={fullness.level}
                    />
                </TaskPaneSection>
            ) : null}
            {sections
                .filter(([, list]) => list.length > 0)
                .map(([title, list]) => (
                    <TaskPaneSection
                        key={title}
                        title={title}
                        aside={`${list.reduce(
                            (n, p) => n + bodiesIn(bodies, p.id).length,
                            0,
                        )} here`}
                    >
                        <ListView
                            view="tiles"
                            tileSize="compact"
                            label={title}
                            selected={selectedId ? [selectedId] : []}
                            onSelectionChange={(ids) =>
                                ids[0] && onSelect(ids[0])
                            }
                            items={list
                                .filter((p) => !!p.id)
                                .map((p) => {
                                    const inside = bodiesIn(bodies, p.id);
                                    const going =
                                        !!me &&
                                        me.destPlaceId === p.id &&
                                        me.placeId !== p.id;
                                    const mine =
                                        !!me &&
                                        !going &&
                                        (me.destPlaceId ?? me.placeId) === p.id;
                                    const occ = occupancy(p, inside.length);
                                    return {
                                        id: p.id ?? '',
                                        label: placeLabel(p, playerId),
                                        icon: (
                                            <Icon
                                                name={placeIcon(p, playerId)}
                                                size={32}
                                            />
                                        ),
                                        details: [
                                            `${mine ? "You're here · " : going ? 'On your way · ' : ''}${inside.length} here${occ.level === 'calm' ? '' : ` · ${occ.level}`}`,
                                            inside.length === 0
                                                ? 'nobody around'
                                                : inside
                                                      .map(
                                                          (b) =>
                                                              (
                                                                  b.name ?? ''
                                                              ).split(' ')[0],
                                                      )
                                                      .join(', '),
                                        ],
                                    };
                                })}
                        />
                    </TaskPaneSection>
                ))}
            {away.length > 0 ? (
                <TaskPaneSection
                    title="Away"
                    aside={`${away.length} offscreen`}
                >
                    {away.map((line) => (
                        <div
                            key={line.participantId}
                            className="flex items-center justify-between gap-2"
                        >
                            <span className="min-w-0">{line.text}</span>
                            {playerId ? (
                                <CommandButton
                                    size="small"
                                    disabled={visit.isPending}
                                    onClick={() =>
                                        visit.mutate(
                                            {
                                                partyId: WORLD_ID,
                                                data: {
                                                    otherId: line.participantId,
                                                    participantId: null,
                                                },
                                            },
                                            {
                                                onSuccess: (res) =>
                                                    patchBody(
                                                        (res as { data?: Body })
                                                            .data,
                                                    ),
                                            },
                                        )
                                    }
                                >
                                    Visit
                                </CommandButton>
                            ) : null}
                        </div>
                    ))}
                </TaskPaneSection>
            ) : null}
            {allEars ? <GrowingPane /> : null}
            <TaskPaneSection title="Author" collapsible={false}>
                <CheckBox
                    title="Author view: hear Place talk beyond your Earshot, and build things"
                    checked={allEars}
                    onChange={(e) => onAllEars(e.currentTarget.checked)}
                >
                    All ears (author)
                </CheckBox>
            </TaskPaneSection>
        </TaskPane>
    );
}

function PlaceView({
    place,
    bodies,
    allBodies,
    places,
    furni,
    me,
    playerId,
    allEars,
}: {
    place: Place;
    bodies: Body[];
    allBodies: Body[];
    places: Place[];
    furni: FurniView[];
    me: Body | undefined;
    playerId: string | null;
    allEars: boolean;
}) {
    const move = usePostWorldsPartyIdPlacesMove();
    const patchBody = usePatchBody();
    const queryClient = useQueryClient();
    const situation = useGetWorldsPartyIdPlacesSituationParticipantId(
        WORLD_ID,
        playerId ?? '',
        { query: { enabled: !!playerId } },
    ).data?.data.situation;
    const ownHome = livesIn(place, playerId);
    const [laptopOpen, setLaptopOpen] = useState(() => drosteDepth() > 0);
    const [profileId, setProfileId] = useState<string | null>(null);
    const profiled = allBodies.find((b) => b.participantId === profileId);
    const amHere =
        !!me &&
        me.placeId === place.id &&
        (!me.destPlaceId || me.destPlaceId === place.id);
    const heading =
        !!me && me.destPlaceId === place.id && me.placeId !== place.id;
    const travelling =
        !!me && !!me.destPlaceId && me.destPlaceId !== me.placeId;
    const canGo = !!me && mayEnter(place, playerId) && !amHere && !heading;
    const canKnock =
        !!me &&
        !mayEnter(place, playerId) &&
        allBodies.some(
            (b) =>
                hosts(place, b.participantId ?? null) &&
                b.placeId === place.id &&
                !b.destPlaceId &&
                !b.asleep,
        );
    const answersDoor = ownHome || (amHere && hosts(place, playerId));
    const knockers = answersDoor ? Object.keys(place.knocks ?? {}) : [];
    const knock = usePostWorldsPartyIdPlacesKnock();
    const letIn = usePostWorldsPartyIdPlacesLetIn();
    const arrangeThing = usePostWorldsPartyIdPlacesThingsArrange();
    const [arranging, setArranging] = useState(false);
    const [tab, setTab] = useState<'talk' | 'spots'>('talk');
    const [picked, setPicked] = useState<string | null>(null);
    const [rot, setRot] = useState(0);
    const mayArrange = (ownHome && amHere) || allEars;
    const arrangeAs = ownHome
        ? playerId
        : allEars
          ? '00000000-0000-0000-0000-000000000000'
          : playerId;
    const put = (x: number, z: number, r: number) =>
        picked &&
        place.id &&
        arrangeThing.mutate({
            partyId: WORLD_ID,
            data: {
                placeId: place.id,
                furniId: picked,
                x,
                z,
                rot: r,
                participantId: arrangeAs,
            },
        });

    const walk = async (placeId: string, spotId: string | null) => {
        if (me && placeId === me.placeId && spotId && !travelling)
            await patchBody({
                ...me,
                destPlaceId: placeId,
                destSpotId: spotId,
                posture: 'Walking',
                pose: null,
                poseWith: null,
                asleep: false,
            });
        move.mutate(
            {
                partyId: WORLD_ID,
                data: { placeId, spotId, participantId: playerId },
            },
            {
                onSuccess: (res) => patchBody((res as { data?: Body }).data),
                onError: () =>
                    queryClient.invalidateQueries({
                        queryKey: getGetWorldsPartyIdPlacesQueryKey(WORLD_ID),
                    }),
            },
        );
    };
    const goTo = (spotId?: string) => {
        if (place.id) void walk(place.id, spotId ?? null);
    };
    const turnBack = () => {
        if (me?.placeId) void walk(me.placeId, me.spotId ?? null);
    };

    const onSpot =
        amHere || canGo ? (spotId: string) => goTo(spotId) : undefined;
    const here = bodies.length;
    const pops =
        knockers.length > 0 || profiled ? (
            <div className="flex max-h-[calc(100%-110px)] w-[min(420px,calc(100%-60px))] flex-col gap-1.5 overflow-y-auto">
                {knockers.map((id) => (
                    <HudPanel
                        key={id}
                        role="status"
                        icon={<Icon name="door" />}
                        title={
                            allBodies.find((b) => b.participantId === id)
                                ?.name ?? 'Someone'
                        }
                    >
                        <div className="flex items-center gap-2">
                            <span className="min-w-0 flex-1">
                                {workplaceDoor(place)
                                    ? 'is at the door.'
                                    : 'is knocking at your door.'}
                            </span>
                            <CommandButton
                                size="small"
                                tone="go"
                                disabled={letIn.isPending}
                                onClick={() =>
                                    letIn.mutate({
                                        partyId: WORLD_ID,
                                        data: {
                                            visitorId: id,
                                            ownerId: playerId,
                                        },
                                    })
                                }
                            >
                                Let in
                            </CommandButton>
                        </div>
                    </HudPanel>
                ))}
                {profiled ? (
                    <BodyProfile
                        body={profiled}
                        canEdit={profiled.participantId === playerId || allEars}
                        isMe={profiled.participantId === playerId}
                        onClose={() => setProfileId(null)}
                    />
                ) : null}
            </div>
        ) : null;
    const hud = (
        <>
            <div className="pointer-events-none absolute inset-x-0 top-0 flex flex-col items-start gap-2 p-2 pr-[56px]">
                <HudPanel
                    icon={<Icon name={placeIcon(place, playerId)} />}
                    title={`${placeLabel(place, playerId)} · ${here === 0 ? 'empty' : `${here} here`}`}
                />
                {pops}
            </div>
            {me && travelling ? (
                <Travel
                    me={me}
                    places={places}
                    playerId={playerId}
                    onTurnBack={turnBack}
                />
            ) : null}
            <div className="pointer-events-none absolute inset-x-0 bottom-0 flex flex-col items-center gap-2 p-2">
                {amHere && me && !arranging ? (
                    <PoseBar
                        place={place}
                        me={me}
                        bodies={allBodies}
                        floating
                    />
                ) : null}
                {situation && !travelling ? (
                    <HudPanel className="self-stretch">
                        <span
                            className="min-w-0 truncate hover:whitespace-normal"
                            title={situation}
                        >
                            {situation}
                        </span>
                    </HudPanel>
                ) : null}
            </div>
        </>
    );

    return (
        <main className="@container flex min-w-0 flex-1 flex-col gap-2 overflow-y-auto p-2 *:shrink-0">
            {canKnock || canGo || (mayArrange && !arranging) ? (
                <Toolbar aria-label="Place tasks">
                    {canKnock ? (
                        <ToolbarButton
                            icon={<Icon name="door" />}
                            disabled={knock.isPending || knock.isSuccess}
                            onClick={() =>
                                place.id &&
                                knock.mutate({
                                    partyId: WORLD_ID,
                                    data: {
                                        homeId: place.id,
                                        participantId: playerId,
                                    },
                                })
                            }
                        >
                            {knock.isSuccess ? 'Knocked…' : 'Knock'}
                        </ToolbarButton>
                    ) : null}
                    {canGo ? (
                        <ToolbarButton
                            icon={<Icon name="forward" />}
                            onClick={() => goTo()}
                            disabled={move.isPending}
                        >
                            Go here
                        </ToolbarButton>
                    ) : null}
                    {mayArrange && !arranging ? (
                        <ToolbarButton
                            icon={<Icon name="chair" />}
                            onClick={() => setArranging(true)}
                        >
                            Arrange
                        </ToolbarButton>
                    ) : null}
                </Toolbar>
            ) : null}
            {!playerId ? (
                <p className="flex items-center gap-2">
                    <Icon name="info" />
                    Sign in on the Welcome screen to have a body here.
                </p>
            ) : null}
            {allEars ? <MakerTools place={place} playerId={playerId} /> : null}
            {arranging && mayArrange ? (
                <ArrangeTools
                    place={place}
                    furni={furni}
                    selected={picked}
                    rot={rot}
                    participantId={arrangeAs}
                    onRotate={() => {
                        const next = (rot + 1) % 4;
                        setRot(next);
                        const f = furni.find((x) => x.furniId === picked);
                        if (f?.tileX != null && f.tileZ != null)
                            put(Number(f.tileX), Number(f.tileZ), next);
                    }}
                    onDone={() => {
                        setArranging(false);
                        setPicked(null);
                    }}
                />
            ) : null}
            {amHere && ownHome && laptopOpen ? (
                <Droste onClose={() => setLaptopOpen(false)} />
            ) : null}
            {webgl2 ? (
                <Suspense
                    fallback={
                        <div className="flex min-h-[240px] flex-[1_0_240px] items-center justify-center">
                            <ProgressBar marquee label="Loading the Place" />
                        </div>
                    }
                >
                    <PlaceScene
                        places={places}
                        hud={hud}
                        arrange={
                            arranging && mayArrange
                                ? {
                                      selected: picked,
                                      onPick: (id) => {
                                          setPicked(id);
                                          setRot(
                                              Number(
                                                  furni.find(
                                                      (f) => f.furniId === id,
                                                  )?.rot ?? 0,
                                              ),
                                          );
                                      },
                                      onTile: (t) => put(t.x, t.y, rot),
                                  }
                                : undefined
                        }
                        place={place}
                        bodies={allBodies}
                        furni={furni}
                        playerId={playerId}
                        allEars={allEars}
                        onBodyClick={setProfileId}
                        onSpotClick={onSpot}
                    />
                </Suspense>
            ) : (
                <>
                    {situation ? (
                        <p className="flex items-start gap-2">
                            <Icon name="info" />
                            {situation}
                        </p>
                    ) : null}
                    {amHere && me ? (
                        <PoseBar place={place} me={me} bodies={allBodies} />
                    ) : null}
                    <div className="relative">
                        <PlaceCanvas
                            place={place}
                            bodies={allBodies}
                            furni={furni}
                            playerId={playerId}
                            allEars={allEars}
                            onBodyClick={setProfileId}
                            onSpotClick={onSpot}
                        />
                        {pops ? (
                            <div className="pointer-events-none absolute inset-x-0 top-0 p-2">
                                {pops}
                            </div>
                        ) : null}
                    </div>
                </>
            )}
            <div className="flex flex-col gap-1">
                <Toolbar aria-label="Show" className="@min-[641px]:hidden">
                    <ToolbarButton
                        icon={<Icon name="chat" />}
                        pressed={tab === 'talk'}
                        onClick={() => setTab('talk')}
                    >
                        Talk
                    </ToolbarButton>
                    <ToolbarButton
                        icon={<Icon name="chair" />}
                        pressed={tab === 'spots'}
                        onClick={() => setTab('spots')}
                    >
                        Spots · {here}
                    </ToolbarButton>
                </Toolbar>
                <div className="grid h-[236px] grid-cols-[minmax(0,1fr)_minmax(260px,0.9fr)] gap-2 *:min-h-0 @max-[640px]:h-[200px] @max-[640px]:grid-cols-1">
                    <div
                        className={`grid grid-cols-[repeat(auto-fill,minmax(150px,1fr))] content-start gap-1.5 overflow-y-auto${tab === 'talk' ? ' @max-[640px]:hidden' : ''}`}
                    >
                        {amHere && ownHome ? (
                            <DecorateForm playerId={playerId} />
                        ) : null}
                        {(place.spots ?? []).map((s) => {
                            const at = bodies.filter(
                                (b) => bodySpotId(b) === s.id,
                            );
                            const capacity = Number(s.capacity ?? 1);
                            const full = at.length >= capacity;
                            const mineSpot = at.some(
                                (b) => b.participantId === playerId,
                            );
                            const things = furni.filter(
                                (f) => f.spotId === s.id,
                            );
                            return (
                                <GroupBox
                                    key={s.id}
                                    title={`${s.name ?? ''} · ${at.length}/${capacity}`}
                                    current={mineSpot}
                                    icon={
                                        mineSpot ? (
                                            <Icon name="location" />
                                        ) : undefined
                                    }
                                    className="content-start gap-1"
                                >
                                    {at.length > 0 ? (
                                        <div className="flex flex-wrap gap-1">
                                            {at.map((b) => (
                                                <BodyChip
                                                    key={b.participantId}
                                                    body={b}
                                                    isMe={
                                                        b.participantId ===
                                                        playerId
                                                    }
                                                    onClick={() =>
                                                        setProfileId(
                                                            b.participantId ??
                                                                null,
                                                        )
                                                    }
                                                />
                                            ))}
                                        </div>
                                    ) : (
                                        <span>nobody</span>
                                    )}
                                    {amHere && !mineSpot && !full ? (
                                        <CommandButton
                                            size="small"
                                            className="justify-self-start"
                                            disabled={move.isPending}
                                            onClick={() => goTo(s.id)}
                                        >
                                            Go
                                        </CommandButton>
                                    ) : null}
                                    {things.map((f) => (
                                        <FurniRow
                                            key={f.furniId}
                                            furni={f}
                                            canAct={amHere}
                                            playerId={playerId}
                                            onOpen={
                                                ownHome
                                                    ? () => setLaptopOpen(true)
                                                    : undefined
                                            }
                                        />
                                    ))}
                                </GroupBox>
                            );
                        })}
                    </div>
                    {amHere && place.talkRoomId ? (
                        <PlaceTalk
                            shown={tab === 'talk'}
                            tucked={tab === 'spots'}
                            roomId={place.talkRoomId}
                            playerId={playerId}
                            allEars={allEars}
                            nearby={bodies.filter(
                                (b) =>
                                    b.participantId !== playerId &&
                                    !b.destPlaceId &&
                                    b.spotId === me?.spotId,
                            )}
                        />
                    ) : (
                        <GroupBox
                            title="Talk here"
                            icon={<Icon name="chat" />}
                            className={`flex min-h-0 flex-col${tab === 'spots' ? ' @max-[640px]:hidden' : ''}`}
                        >
                            <ChatLog
                                aria-label="Talk here"
                                className="min-h-0 flex-1"
                            >
                                <TalkNote>
                                    {amHere
                                        ? 'Nobody to talk to here yet.'
                                        : 'Go here to hear what people are saying.'}
                                </TalkNote>
                            </ChatLog>
                        </GroupBox>
                    )}
                </div>
            </div>
        </main>
    );
}

function Travel({
    me,
    places,
    playerId,
    onTurnBack,
}: {
    me: Body;
    places: Place[];
    playerId: string | null;
    onTurnBack: () => void;
}) {
    const now = useTicker(true);
    const start = toMs(me.departedAt);
    const end = toMs(me.arriveAt);
    const left = Math.max(0, Math.ceil((end - now) / 1000));
    const share = end > start ? Math.min(1, (now - start) / (end - start)) : 1;
    const to = places.find((p) => p.id === me.destPlaceId);
    const from = places.find((p) => p.id === me.placeId);
    const remaining =
        left > 0
            ? `${Math.floor(left / 60)}:${String(left % 60).padStart(2, '0')}`
            : 'arriving…';
    return (
        <HudPanel
            anchor="top"
            role="status"
            icon={<Icon name="walk" />}
            title={`${from ? `${placeLabel(from, playerId)} → ` : ''}${placeLabel(to, playerId)}`}
            className="w-[min(380px,calc(100%-260px))] min-w-[220px]"
        >
            <div className="flex items-center gap-2">
                <ProgressBar
                    className="min-w-0 flex-1"
                    value={share * 100}
                    label="On the way"
                    valueText={remaining}
                />
                <span>{remaining}</span>
                {from && left > 0 ? (
                    <CommandButton
                        size="small"
                        icon={<Icon name="undo" />}
                        title={`Go back to ${placeLabel(from, playerId)}`}
                        onClick={onTurnBack}
                    >
                        Back
                    </CommandButton>
                ) : null}
            </div>
        </HudPanel>
    );
}

function BodyProfile({
    body,
    isMe,
    canEdit,
    onClose,
}: {
    body: Body;
    isMe: boolean;
    canEdit: boolean;
    onClose: () => void;
}) {
    const [editing, setEditing] = useState(false);
    const selves = useGetWorldsPartyIdSelves(WORLD_ID).data?.data ?? [];
    const self = selves.find((s) => s.personaId === body.participantId);
    const a = body.appearance;
    const look = a
        ? [
              `${a.hair} ${a.hairStyle === 'bald' ? '' : `${a.hairStyle} `}hair`.replace(
                  '  ',
                  ' ',
              ),
              `${a.top} top`,
              a.accessory && a.accessory !== 'none' ? a.accessory : null,
          ]
              .filter(Boolean)
              .join(', ')
        : null;
    return (
        <HudPanel
            icon={<Icon name={postureIcon(body)} size={32} />}
            title={`${body.name}${isMe ? ' (you)' : ''}${body.isHuman ? ' (human)' : ''}`}
            onClose={onClose}
        >
            {look ? <span>{look}</span> : null}
            <span>
                {body.asleep
                    ? 'Asleep.'
                    : (body.activity ??
                      (body.destPlaceId
                          ? 'On the move.'
                          : 'Just hanging around.'))}
            </span>
            {self?.mood ? <span>Mood: {self.mood}</span> : null}
            {self?.doing ? (
                <span className="line-clamp-2" title={self.doing}>
                    {self.doing}
                </span>
            ) : null}
            {canEdit ? (
                <CommandButton
                    size="small"
                    className="justify-self-start"
                    pressed={editing}
                    onClick={() => setEditing((e) => !e)}
                >
                    {editing ? 'Done' : 'Change look'}
                </CommandButton>
            ) : null}
            {editing ? <LookEditor body={body} /> : null}
        </HudPanel>
    );
}

function LookEditor({ body }: { body: Body }) {
    const fields =
        useGetWorldsPartyIdPlacesAppearanceOptions(WORLD_ID).data?.data;
    const save = usePutWorldsPartyIdPlacesAppearance();
    const restyle = usePostWorldsPartyIdPlacesRestyle();
    const [wish, setWish] = useState('');
    if (!fields) return null;
    const a = (body.appearance ?? {}) as Record<string, unknown>;
    return (
        <div className="flex flex-col gap-1">
            <form
                className="flex gap-1"
                onSubmit={(e) => {
                    e.preventDefault();
                    if (!wish.trim()) return;
                    restyle.mutate(
                        {
                            partyId: WORLD_ID,
                            data: {
                                text: wish.trim(),
                                participantId: body.participantId ?? null,
                            },
                        },
                        { onSuccess: () => setWish('') },
                    );
                }}
            >
                <TextBox
                    value={wish}
                    aria-label="Change the look"
                    placeholder="Say it: dye my hair pink, get a leather jacket…"
                    className="min-w-0 flex-1"
                    onChange={(e) => setWish(e.currentTarget.value)}
                />
                <CommandButton type="submit" disabled={restyle.isPending}>
                    {restyle.isPending ? 'Changing…' : 'Change'}
                </CommandButton>
            </form>
            {fields.map((f) => (
                <FieldRow key={f.key} label={f.label}>
                    <ComboBox
                        value={String(a[f.key ?? ''] ?? '')}
                        disabled={save.isPending}
                        options={(f.options ?? []).map((v) => ({
                            value: v,
                            label: v,
                        }))}
                        onChange={(e) =>
                            save.mutate({
                                partyId: WORLD_ID,
                                data: {
                                    picked: {
                                        [f.key ?? '']: e.currentTarget.value,
                                    },
                                    participantId: body.participantId ?? null,
                                },
                            })
                        }
                    />
                </FieldRow>
            ))}
        </div>
    );
}

const DROSTE_MAX = 3;

const drosteDepth = () =>
    typeof window === 'undefined'
        ? 0
        : Number(
              new URLSearchParams(window.location.search).get('droste') ?? 0,
          );

function Droste({ onClose }: { onClose: () => void }) {
    const depth = drosteDepth();
    const src = useMemo(() => {
        const url = new URL(window.location.href);
        url.searchParams.set('droste', String(depth + 1));
        return url.toString();
    }, [depth]);
    return (
        <GroupBox
            title={
                <span className="flex items-center gap-2">
                    Your laptop
                    <CommandButton size="small" onClick={onClose}>
                        Close
                    </CommandButton>
                </span>
            }
            icon={<Icon name="laptop" />}
        >
            {depth >= DROSTE_MAX ? (
                <p className="p-2">It's laptops all the way down.</p>
            ) : (
                <div className="h-[300px] w-[480px] overflow-hidden">
                    <iframe
                        title="Your laptop"
                        src={src}
                        className="border-0"
                        style={{
                            width: 960,
                            height: 600,
                            transform: 'scale(0.5)',
                            transformOrigin: '0 0',
                        }}
                    />
                </div>
            )}
        </GroupBox>
    );
}

function DecorateForm({ playerId }: { playerId: string | null }) {
    const decorate = usePostWorldsPartyIdPlacesDecorate();
    const [text, setText] = useState('');
    return (
        <form
            className="col-span-full flex items-center gap-1"
            onSubmit={(e) => {
                e.preventDefault();
                if (!text.trim()) return;
                decorate.mutate(
                    {
                        partyId: WORLD_ID,
                        data: { text: text.trim(), participantId: playerId },
                    },
                    { onSuccess: () => setText('') },
                );
            }}
        >
            <Icon name="picture" />
            <TextBox
                value={text}
                maxLength={200}
                aria-label="Put something up"
                className="min-w-0 flex-1"
                onChange={(e) => setText(e.currentTarget.value)}
                placeholder={
                    decorate.isError
                        ? 'Two new things a day — try tomorrow.'
                        : 'Put something up… (a Bowie poster over the sofa)'
                }
            />
            <CommandButton
                type="submit"
                size="small"
                disabled={!text.trim() || decorate.isPending}
            >
                Put up
            </CommandButton>
        </form>
    );
}

function FurniRow({
    furni,
    canAct,
    playerId,
    onOpen,
}: {
    furni: FurniView;
    canAct: boolean;
    playerId: string | null;
    onOpen?: () => void;
}) {
    const act = usePostWorldsPartyIdPlacesFurni();
    const run = (care: boolean) =>
        furni.placeId &&
        furni.furniId &&
        act.mutate({
            partyId: WORLD_ID,
            data: {
                placeId: furni.placeId,
                furniId: furni.furniId,
                care,
                text: null,
                participantId: playerId,
            },
        });
    const careWord = furni.careLabel?.split(' ')[0];
    const thing = furni;
    const doable = thing.gives != null && !!furni.useLabel;
    const left = thing.leaves ? thing.takes?.at(-1) : undefined;
    const doIt = () => {
        const text = thing.leaves
            ? window.prompt(
                  `${furni.useLabel}: your own words to leave (empty lets the world write it)`,
              )
            : null;
        if (thing.leaves && text === null) return;
        if (furni.placeId && furni.furniId)
            act.mutate({
                partyId: WORLD_ID,
                data: {
                    placeId: furni.placeId,
                    furniId: furni.furniId,
                    care: false,
                    text: text?.trim() || null,
                    participantId: playerId,
                },
            });
    };
    const gave = (
        act.data as unknown as { data?: { done?: string } } | undefined
    )?.data?.done;
    if (doable)
        return (
            <div
                className="flex min-w-0 items-center gap-1"
                title={
                    left
                        ? `${left.byName?.split(' ')[0]} left: “${left.text}”`
                        : (furni.name ?? undefined)
                }
            >
                <Icon name="hand" />
                <span className="min-w-0 flex-1 truncate">
                    {gave ?? furni.useLabel}
                </span>
                {canAct && !gave ? (
                    <CommandButton
                        size="small"
                        disabled={act.isPending}
                        onClick={doIt}
                    >
                        {act.isPending ? '…' : 'Do'}
                    </CommandButton>
                ) : null}
            </div>
        );
    return (
        <div
            className="flex min-w-0 items-center gap-1"
            title={furni.notable ?? furni.name ?? undefined}
        >
            <span>{furniIcon(furni)}</span>
            <span className="min-w-0 flex-1 truncate">
                {furni.notable ?? furni.name}
            </span>
            {canAct && onOpen && furni.kind === 'laptop' ? (
                <CommandButton size="small" onClick={onOpen}>
                    Open
                </CommandButton>
            ) : null}
            {canAct && furni.careLabel ? (
                <CommandButton
                    size="small"
                    tone="caution"
                    disabled={act.isPending}
                    onClick={() => run(true)}
                >
                    {careWord === 'Try' ? 'Fix' : careWord}
                </CommandButton>
            ) : canAct && furni.useLabel ? (
                <CommandButton
                    size="small"
                    disabled={act.isPending}
                    onClick={() => run(false)}
                    title={furni.useLabel}
                >
                    Use
                </CommandButton>
            ) : null}
        </div>
    );
}

function BodyChip({
    body,
    isMe,
    onClick,
}: {
    body: Body;
    isMe: boolean;
    onClick?: () => void;
}) {
    const now = useTicker(!!body.destPlaceId);
    const walking = walkingText(body, now);
    const look = lookOf(body.appearance, avatarColour(body.participantId));
    return (
        <CommandButton
            size="small"
            icon={<FaceGlyph look={look} />}
            onClick={onClick}
            title={
                [body.activity, walking].filter(Boolean).join(' · ') ||
                undefined
            }
        >
            {(body.name ?? '').split(' ')[0]}
            {isMe ? ' ★' : ''}
            {walking ? <Icon name="walk" label="walking" /> : null}
            {body.activity?.includes('phone') ? (
                <Icon name="phone" label="on the phone" />
            ) : null}
        </CommandButton>
    );
}

function PlaceTalk({
    shown: visible,
    tucked,
    roomId,
    playerId,
    nearby,
    allEars,
}: {
    shown: boolean;
    tucked: boolean;
    roomId: string;
    playerId: string | null;
    nearby: Body[];
    allEars: boolean;
}) {
    const lives = useGetWorldsPartyIdLivesSuspense(WORLD_ID).data.data ?? [];
    const { messages, phaseByPersona } = useRoomConversation(WORLD_ID, roomId);
    const speaking = [...phaseByPersona.values()]
        .filter((p) => p.phase === 'typing' || p.phase === 'streaming')
        .map((p) => p.personaName ?? '')
        .map(
            (id) =>
                (lives.find((l) => l.personaId === id)?.name ?? id).split(
                    ' ',
                )[0],
        );
    const shown = useMemo(
        () => messages.filter((m) => m.content?.trim()).slice(-40),
        [messages],
    );
    const prompt = usePostPartyIdPrompt();
    const voice = usePostWorldsPartyIdPlacesVoice();
    const [draft, setDraft] = useState('');
    const [volume, setVolume] = useState<'talk' | 'whisper' | 'shout'>('talk');
    const [to, setTo] = useState<string>('');
    const logRef = useRef<HTMLDivElement>(null);
    const atEnd = useRef(true);
    // biome-ignore lint/correctness/useExhaustiveDependencies: re-run as lines and typing arrive, and when the tab comes back
    useLayoutEffect(() => {
        const log = logRef.current;
        if (log && atEnd.current) log.scrollTop = log.scrollHeight;
    }, [shown, speaking.length, visible]);

    const nameOf = (id: string | undefined) => {
        const name = lives.find((l) => l.personaId === id)?.name ?? 'Someone';
        return id === playerId ? `${name} (you)` : name;
    };

    const say = async () => {
        const text = draft.trim();
        if (!text || !playerId) return;
        setDraft('');
        if (volume !== 'talk')
            await voice.mutateAsync({
                partyId: WORLD_ID,
                data: {
                    volume,
                    to:
                        volume === 'whisper'
                            ? to || nearby[0]?.participantId || null
                            : null,
                    participantId: playerId,
                },
            });
        await prompt.mutateAsync({
            id: WORLD_ID,
            data: { chatGroupId: roomId, prompt: text, senderId: playerId },
        });
    };

    return (
        <GroupBox
            title="Said out loud here"
            icon={<Icon name="chat" />}
            className={`flex min-h-0 flex-col${tucked ? ' @max-[640px]:hidden' : ''}`}
        >
            <ChatLog
                ref={logRef}
                aria-label="Said out loud here"
                className="min-h-0 flex-1"
                onScroll={(e) => {
                    const el = e.currentTarget;
                    atEnd.current =
                        el.scrollHeight - el.scrollTop - el.clientHeight < 24;
                }}
            >
                {shown.length === 0 ? <TalkNote>Quiet.</TalkNote> : null}
                {earshotRuns(shown, playerId, allEars).map((run) => {
                    if (!run.heard && run.lines.length > 1) {
                        const who = [
                            ...new Set(
                                run.lines.map(
                                    (m) => nameOf(m.senderId).split(' ')[0],
                                ),
                            ),
                        ];
                        return (
                            <ChatMessage
                                key={run.lines[0].messageId}
                                layout="transcript"
                                kind="emote"
                                manner="faint"
                                sender={
                                    <>
                                        <Icon name="muted" /> {who.join(', ')}
                                    </>
                                }
                            >
                                talking out of earshot · {run.lines.length}{' '}
                                lines
                            </ChatMessage>
                        );
                    }
                    const m = run.heard ? run.line : run.lines[0];
                    const heard = run.heard;
                    const v = volumeOf(m);
                    return (
                        <ChatMessage
                            key={m.messageId}
                            layout="transcript"
                            sender={
                                <>
                                    {nameOf(m.senderId)}
                                    {v === 'whisper' ? (
                                        <>
                                            {' '}
                                            <Icon
                                                name="whisper"
                                                label="whispers"
                                            />
                                        </>
                                    ) : v === 'shout' ? (
                                        <>
                                            {' '}
                                            <Icon
                                                name="megaphone"
                                                label="shouts"
                                            />
                                        </>
                                    ) : null}
                                </>
                            }
                            colour={avatarColour(m.senderId)}
                            manner={
                                !heard
                                    ? 'faint'
                                    : v === 'whisper'
                                      ? 'quiet'
                                      : v === 'shout'
                                        ? 'loud'
                                        : 'plain'
                            }
                        >
                            {heard ? m.content : scribble(m.content)}
                        </ChatMessage>
                    );
                })}
                {speaking.length > 0 ? (
                    <TalkNote>
                        {speaking.join(', ')}{' '}
                        {speaking.length === 1 ? 'is' : 'are'} about to say
                        something…
                    </TalkNote>
                ) : null}
            </ChatLog>
            <form
                className="flex items-center gap-1"
                onSubmit={(e) => {
                    e.preventDefault();
                    void say();
                }}
            >
                <Toolbar
                    variant="bare"
                    labels="none"
                    aria-label="Volume"
                    title="Talk reaches your spot, a shout the whole Place, a whisper one person next to you"
                >
                    {(
                        [
                            ['whisper', 'whisper'],
                            ['talk', 'chat'],
                            ['shout', 'megaphone'],
                        ] as const
                    ).map(([v, icon]) => (
                        <ToolbarButton
                            key={v}
                            title={v}
                            icon={<Icon name={icon} />}
                            pressed={volume === v}
                            disabled={
                                !playerId ||
                                (v === 'whisper' && nearby.length === 0)
                            }
                            onClick={() => setVolume(v)}
                        >
                            {v}
                        </ToolbarButton>
                    ))}
                </Toolbar>
                {volume === 'whisper' && nearby.length > 0 ? (
                    <ComboBox
                        aria-label="Whisper to"
                        value={to}
                        onChange={(e) => setTo(e.currentTarget.value)}
                        options={nearby.map((b) => ({
                            value: b.participantId ?? '',
                            label: `to ${(b.name ?? '').split(' ')[0]}`,
                        }))}
                    />
                ) : null}
                <TextBox
                    value={draft}
                    aria-label="Say something"
                    className="min-w-0 flex-1"
                    onChange={(e) => setDraft(e.currentTarget.value)}
                    placeholder={
                        playerId
                            ? volume === 'shout'
                                ? 'Call out…'
                                : volume === 'whisper'
                                  ? 'Whisper…'
                                  : 'Say something out loud…'
                            : 'Sign in to talk'
                    }
                    disabled={!playerId}
                />
                <CommandButton
                    type="submit"
                    tone="go"
                    disabled={!playerId || !draft.trim() || prompt.isPending}
                >
                    Say
                </CommandButton>
            </form>
        </GroupBox>
    );
}

function TalkNote({ children }: { children: ReactNode }) {
    return (
        <Text as="p" tone="muted" italic>
            {children}
        </Text>
    );
}
