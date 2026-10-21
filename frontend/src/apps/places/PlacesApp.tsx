import { useQueryClient } from '@tanstack/react-query';
import { lazy, Suspense, useEffect, useMemo, useRef, useState } from 'react';
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
import { useRoomConversation } from '../../lib/use-room-conversation';
import { usePlayerId, useWorldEvents, WORLD_ID } from '../../lib/world';
import type { WorldEventData } from '../../lib/world-bus';
import PlaceCanvas from './iso/PlaceCanvas';
import { ArrangeTools, MakerTools, PoseBar } from './PlaceTools';

const PlaceScene = lazy(() => import('./scene/PlaceScene'));

/** three.js needs WebGL2; without it the 2D canvas draws the Place. */
const webgl2 = (() => {
    try {
        return !!document.createElement('canvas').getContext('webgl2');
    } catch {
        return false;
    }
})();

import {
    avatarColour,
    bodiesIn,
    bodySpotId,
    canHear,
    furniIcon,
    mayEnter,
    navigatorPlaces,
    placeLabel,
    postureIcon,
    scribble,
    volumeOf,
    walkingText,
} from './places-lib';

const panel = {
    background: '#fff',
    border: '1px solid',
    borderColor: '#7f9db9 #fff #fff #7f9db9',
} as const;

/** Places: where every Body is. Text-only until the iso renderer (spec phase 5). */
export default function PlacesApp() {
    const playerId = usePlayerId();
    const queryClient = useQueryClient();
    const snapshot = useGetWorldsPartyIdPlacesSuspense(WORLD_ID).data.data;
    const places = snapshot.places ?? [];
    const bodies = snapshot.bodies ?? [];
    const me = bodies.find((b) => b.participantId === playerId);
    const [selectedId, setSelectedId] = useState<string | null>(null);
    const now = useNow();

    const { connectPartyRealtime, disconnectPartyRealtime } =
        useRealtimeStoreActions();
    useEffect(() => {
        connectPartyRealtime(WORLD_ID);
        return () => disconnectPartyRealtime(WORLD_ID);
    }, [connectPartyRealtime, disconnectPartyRealtime]);

    useWorldEvents((envelope) => {
        if (!envelope.type.startsWith('world.')) return;
        const kind = (envelope.data as WorldEventData).kind;
        if (kind !== 'body' && kind !== 'place') return;
        const prefix = getGetWorldsPartyIdPlacesQueryKey(WORLD_ID)[0];
        queryClient.invalidateQueries({
            predicate: (q) =>
                typeof q.queryKey[0] === 'string' &&
                q.queryKey[0].startsWith(prefix),
        });
    });

    const selected =
        places.find((p) => p.id === selectedId) ??
        places.find((p) => p.id === (me?.destPlaceId ?? me?.placeId)) ??
        places[0];

    return (
        <div className="app-surface flex h-full" style={{ gap: 6, padding: 6 }}>
            <Navigator
                places={navigatorPlaces(places, playerId)}
                bodies={bodies}
                me={me}
                playerId={playerId}
                selectedId={selected?.id}
                onSelect={setSelectedId}
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
                    now={now}
                />
            ) : null}
        </div>
    );
}

const ALL_EARS_KEY = 'proompting.places.allEars';

/** Author switch: hear every line in a Place regardless of Earshot. Remembered per browser. */
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

function useNow() {
    const [now, setNow] = useState(() => Date.now());
    useEffect(() => {
        const t = setInterval(() => setNow(Date.now()), 1000);
        return () => clearInterval(t);
    }, []);
    return now;
}

function Navigator({
    places,
    bodies,
    me,
    playerId,
    selectedId,
    onSelect,
}: {
    places: Place[];
    bodies: Body[];
    me: Body | undefined;
    playerId: string | null;
    selectedId: string | undefined;
    onSelect: (id: string) => void;
}) {
    return (
        <fieldset className="flex flex-col" style={{ width: 230, margin: 0 }}>
            <legend>Navigator</legend>
            <div className="flex-1 overflow-y-auto" style={panel}>
                {places.map((p) => {
                    const here = bodiesIn(bodies, p.id);
                    const mine = me && (me.destPlaceId ?? me.placeId) === p.id;
                    return (
                        <button
                            key={p.id}
                            type="button"
                            onClick={() => p.id && onSelect(p.id)}
                            className="block w-full text-left"
                            style={{
                                padding: '4px 6px',
                                border: 'none',
                                borderBottom: '1px solid #eee',
                                boxShadow: 'none',
                                minHeight: 0,
                                background:
                                    p.id === selectedId
                                        ? '#dbe7fb'
                                        : 'transparent',
                            }}
                        >
                            <div
                                className="flex items-center"
                                style={{ gap: 4 }}
                            >
                                <span>
                                    {p.kind !== 'home'
                                        ? '🏢'
                                        : mayEnter(p, playerId)
                                          ? '🏠'
                                          : '🔒'}
                                </span>
                                <strong>{placeLabel(p, playerId)}</strong>
                                {mine ? (
                                    <span style={{ color: '#2a7' }}>• you</span>
                                ) : null}
                                <span
                                    style={{
                                        marginLeft: 'auto',
                                        color: '#666',
                                    }}
                                >
                                    {here.length}
                                </span>
                            </div>
                            <div style={{ color: '#666', fontSize: 11 }}>
                                {here.length === 0
                                    ? 'nobody'
                                    : here.map((b) => b.name).join(', ')}
                            </div>
                        </button>
                    );
                })}
            </div>
        </fieldset>
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
    now,
}: {
    place: Place;
    bodies: Body[];
    allBodies: Body[];
    places: Place[];
    furni: FurniView[];
    me: Body | undefined;
    playerId: string | null;
    now: number;
}) {
    const move = usePostWorldsPartyIdPlacesMove();
    const situation = useGetWorldsPartyIdPlacesSituationParticipantId(
        WORLD_ID,
        playerId ?? '',
        { query: { enabled: !!playerId } },
    ).data?.data.situation;
    const [allEars, setAllEars] = useAllEars();
    const ownHome = place.kind === 'home' && place.ownerId === playerId;
    const [laptopOpen, setLaptopOpen] = useState(() => drosteDepth() > 0);
    const [profileId, setProfileId] = useState<string | null>(null);
    const profiled = allBodies.find((b) => b.participantId === profileId);
    const amHere = !!me && me.placeId === place.id && !me.destPlaceId;
    const heading = !!me && me.destPlaceId === place.id;
    const canGo = !!me && mayEnter(place, playerId) && !amHere && !heading;
    const owner = allBodies.find((b) => b.participantId === place.ownerId);
    const canKnock =
        !!me &&
        place.kind === 'home' &&
        !mayEnter(place, playerId) &&
        !!owner &&
        owner.placeId === place.id &&
        !owner.destPlaceId &&
        !owner.asleep;
    const knockers =
        place.ownerId && place.ownerId === playerId
            ? Object.keys(place.knocks ?? {})
            : [];
    const knock = usePostWorldsPartyIdPlacesKnock();
    const letIn = usePostWorldsPartyIdPlacesLetIn();
    const arrangeThing = usePostWorldsPartyIdPlacesThingsArrange();
    const [arranging, setArranging] = useState(false);
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

    const goTo = (spotId?: string) =>
        place.id &&
        move.mutate({
            partyId: WORLD_ID,
            data: {
                placeId: place.id,
                spotId: spotId ?? null,
                participantId: playerId,
            },
        });

    return (
        <div
            className="flex min-w-0 flex-1 flex-col overflow-y-auto"
            style={{ gap: 6 }}
        >
            <div className="flex items-center" style={{ gap: 8 }}>
                <div className="min-w-0 flex-1">
                    <h3 style={{ margin: 0, fontSize: 14 }}>
                        {placeLabel(place, playerId)}
                    </h3>
                    <div style={{ color: '#666' }}>{place.description}</div>
                </div>
                <label
                    style={{ color: '#666', whiteSpace: 'nowrap' }}
                    title="Author view: hear Place talk beyond your Earshot"
                >
                    <input
                        type="checkbox"
                        checked={allEars}
                        onChange={(e) => setAllEars(e.currentTarget.checked)}
                    />{' '}
                    All ears
                </label>
                {canKnock ? (
                    <button
                        type="button"
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
                    </button>
                ) : null}
                {canGo ? (
                    <button
                        type="button"
                        onClick={() => goTo()}
                        disabled={move.isPending}
                    >
                        Go here
                    </button>
                ) : null}
                {mayArrange && !arranging ? (
                    <button type="button" onClick={() => setArranging(true)}>
                        Arrange
                    </button>
                ) : null}
            </div>
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
            {amHere && me && !arranging ? (
                <PoseBar place={place} me={me} bodies={allBodies} />
            ) : null}
            {situation ? (
                <div
                    style={{
                        ...panel,
                        padding: '4px 6px',
                        background: '#ffffe1',
                    }}
                >
                    {situation}
                </div>
            ) : !playerId ? (
                <div style={{ color: '#666' }}>
                    Sign in on the Welcome screen to have a body here.
                </div>
            ) : null}
            {knockers.map((id) => (
                <div
                    key={id}
                    className="flex items-center"
                    style={{
                        ...panel,
                        padding: '4px 6px',
                        gap: 6,
                        background: '#fff6d5',
                    }}
                >
                    🚪{' '}
                    {allBodies.find((b) => b.participantId === id)?.name ??
                        'Someone'}{' '}
                    is knocking at your door.
                    <button
                        type="button"
                        style={{ marginLeft: 'auto' }}
                        disabled={letIn.isPending}
                        onClick={() =>
                            letIn.mutate({
                                partyId: WORLD_ID,
                                data: { visitorId: id, ownerId: playerId },
                            })
                        }
                    >
                        Let in
                    </button>
                </div>
            ))}
            {amHere && ownHome && laptopOpen ? (
                <Droste onClose={() => setLaptopOpen(false)} />
            ) : null}
            {amHere && ownHome ? <DecorateForm playerId={playerId} /> : null}
            {profiled ? (
                <BodyProfile
                    body={profiled}
                    canEdit={profiled.participantId === playerId || allEars}
                    isMe={profiled.participantId === playerId}
                    onClose={() => setProfileId(null)}
                />
            ) : null}
            {webgl2 ? (
                <Suspense fallback={<div style={{ height: 300 }} />}>
                    <PlaceScene
                        places={places}
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
                        onSpotClick={
                            amHere || canGo
                                ? (spotId) => goTo(spotId)
                                : undefined
                        }
                    />
                </Suspense>
            ) : (
                <PlaceCanvas
                    place={place}
                    bodies={allBodies}
                    furni={furni}
                    playerId={playerId}
                    allEars={allEars}
                    onBodyClick={setProfileId}
                    onSpotClick={
                        amHere || canGo ? (spotId) => goTo(spotId) : undefined
                    }
                />
            )}
            <div
                className="grid"
                style={{
                    gridTemplateColumns:
                        'repeat(auto-fill, minmax(150px, 1fr))',
                    gap: 6,
                }}
            >
                {(place.spots ?? []).map((s) => {
                    const at = bodies.filter((b) => bodySpotId(b) === s.id);
                    const full = at.length >= Number(s.capacity ?? 1);
                    const mineSpot = at.some(
                        (b) => b.participantId === playerId,
                    );
                    const things = furni.filter((f) => f.spotId === s.id);
                    return (
                        <div
                            key={s.id}
                            style={{ ...panel, padding: 6, minHeight: 64 }}
                        >
                            <div
                                className="flex items-center"
                                style={{ gap: 4 }}
                            >
                                <strong>{s.name}</strong>
                                <span style={{ color: '#888' }}>
                                    {at.length}/{s.capacity}
                                </span>
                                {amHere && !mineSpot && !full ? (
                                    <button
                                        type="button"
                                        style={{
                                            marginLeft: 'auto',
                                            minWidth: 0,
                                            padding: '0 6px',
                                        }}
                                        disabled={move.isPending}
                                        onClick={() => goTo(s.id)}
                                    >
                                        Move here
                                    </button>
                                ) : null}
                            </div>
                            <div
                                className="flex flex-wrap"
                                style={{ gap: 4, marginTop: 4 }}
                            >
                                {at.map((b) => (
                                    <BodyChip
                                        key={b.participantId}
                                        body={b}
                                        now={now}
                                        isMe={b.participantId === playerId}
                                    />
                                ))}
                            </div>
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
                        </div>
                    );
                })}
            </div>
            {amHere && place.talkRoomId ? (
                <PlaceTalk
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
            ) : amHere ? (
                <div style={{ color: '#666' }}>Nobody to talk to here yet.</div>
            ) : null}
        </div>
    );
}

/** Who someone is at a glance: their look, what they're doing, their mood. */
function BodyProfile({
    body,
    isMe,
    canEdit,
    onClose,
}: {
    body: Body;
    isMe: boolean;
    /** Your own look, or anyone's in the author's All ears mode. */
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
        <div
            className="flex items-start"
            style={{
                ...panel,
                padding: '4px 6px',
                gap: 8,
                background: '#f3f7ff',
            }}
        >
            <span style={{ fontSize: 22 }}>{postureIcon(body)}</span>
            <div className="min-w-0 flex-1">
                <strong>
                    {body.name}
                    {isMe ? ' (you)' : ''}
                </strong>
                {body.isHuman ? (
                    <span style={{ color: '#888' }}> (human)</span>
                ) : null}
                {look ? <div style={{ color: '#555' }}>{look}</div> : null}
                <div>
                    {body.asleep
                        ? 'Asleep.'
                        : (body.activity ??
                          (body.destPlaceId
                              ? 'On the move.'
                              : 'Just hanging around.'))}
                </div>
                {self?.mood ? (
                    <div style={{ color: '#555' }}>
                        Mood: {self.mood}
                        {self.doing ? ` · ${self.doing}` : ''}
                    </div>
                ) : null}
            </div>
            <div className="flex flex-col" style={{ gap: 4 }}>
                <button
                    type="button"
                    style={{ minWidth: 0, padding: '0 6px' }}
                    onClick={onClose}
                >
                    ✕
                </button>
                {canEdit ? (
                    <button
                        type="button"
                        style={{ minWidth: 0, padding: '0 6px' }}
                        onClick={() => setEditing((e) => !e)}
                    >
                        {editing ? 'Done' : 'Change look'}
                    </button>
                ) : null}
            </div>
            {editing ? <LookEditor body={body} /> : null}
        </div>
    );
}

/** Pick a look from the palette; each change saves at once. */
function LookEditor({ body }: { body: Body }) {
    const fields =
        useGetWorldsPartyIdPlacesAppearanceOptions(WORLD_ID).data?.data;
    const save = usePutWorldsPartyIdPlacesAppearance();
    const restyle = usePostWorldsPartyIdPlacesRestyle();
    const [wish, setWish] = useState('');
    if (!fields) return null;
    const a = (body.appearance ?? {}) as Record<string, unknown>;
    return (
        <div className="flex flex-col" style={{ gap: 4 }}>
            <form
                className="flex"
                style={{ gap: 4 }}
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
                <input
                    value={wish}
                    placeholder="Say it: dye my hair pink, get a leather jacket…"
                    style={{ flex: 1 }}
                    onChange={(e) => setWish(e.currentTarget.value)}
                />
                <button type="submit" disabled={restyle.isPending}>
                    {restyle.isPending ? 'Changing…' : 'Change'}
                </button>
            </form>
            <div
                className="grid"
                style={{ gridTemplateColumns: 'auto auto', gap: '2px 6px' }}
            >
                {fields.map((f) => (
                    <label key={f.key} className="contents">
                        <span style={{ color: '#555' }}>{f.label}</span>
                        <select
                            value={String(a[f.key ?? ''] ?? '')}
                            disabled={save.isPending}
                            onChange={(e) =>
                                save.mutate({
                                    partyId: WORLD_ID,
                                    data: {
                                        picked: {
                                            [f.key ?? '']:
                                                e.currentTarget.value,
                                        },
                                        participantId:
                                            body.participantId ?? null,
                                    },
                                })
                            }
                        >
                            {(f.options ?? []).map((v) => (
                                <option key={v} value={v}>
                                    {v}
                                </option>
                            ))}
                        </select>
                    </label>
                ))}
            </div>
        </div>
    );
}

const DROSTE_MAX = 3;

/** How many laptops deep this desktop is (0 = the real one). */
const drosteDepth = () =>
    typeof window === 'undefined'
        ? 0
        : Number(
              new URLSearchParams(window.location.search).get('droste') ?? 0,
          );

/** Your laptop shows the desktop, which shows Places, which shows you at your laptop… */
function Droste({ onClose }: { onClose: () => void }) {
    const depth = drosteDepth();
    const src = useMemo(() => {
        const url = new URL(window.location.href);
        url.searchParams.set('droste', String(depth + 1));
        return url.toString();
    }, [depth]);
    return (
        <fieldset style={{ margin: 0 }}>
            <legend>
                💻 Your laptop{' '}
                <button
                    type="button"
                    style={{ minWidth: 0, padding: '0 6px' }}
                    onClick={onClose}
                >
                    Close
                </button>
            </legend>
            {depth >= DROSTE_MAX ? (
                <div style={{ color: '#666', padding: 8 }}>
                    It's laptops all the way down.
                </div>
            ) : (
                <div
                    style={{
                        width: 480,
                        height: 300,
                        overflow: 'hidden',
                        border: '2px solid #333',
                    }}
                >
                    <iframe
                        title="Your laptop"
                        src={src}
                        style={{
                            width: 960,
                            height: 600,
                            border: 0,
                            transform: 'scale(0.5)',
                            transformOrigin: '0 0',
                        }}
                    />
                </div>
            )}
        </fieldset>
    );
}

/** Put something up at home, in your own words (2 a day). */
function DecorateForm({ playerId }: { playerId: string | null }) {
    const decorate = usePostWorldsPartyIdPlacesDecorate();
    const [text, setText] = useState('');
    return (
        <form
            className="flex items-center"
            style={{ gap: 4 }}
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
            <span>🖼️</span>
            <input
                className="flex-1"
                value={text}
                maxLength={200}
                onChange={(e) => setText(e.currentTarget.value)}
                placeholder="Put something up… (a Bowie poster over the sofa)"
            />
            <button type="submit" disabled={!text.trim() || decorate.isPending}>
                Put up
            </button>
            {decorate.isError ? (
                <span style={{ color: '#a33' }}>Two new things a day.</span>
            ) : null}
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
    /** The laptop at home opens the desktop (the Droste easter egg). */
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
    return (
        <div
            className="flex items-center"
            style={{ gap: 4, marginTop: 4, fontSize: 11 }}
            title={furni.notable ?? undefined}
        >
            <span>{furniIcon(furni)}</span>
            <span style={{ color: furni.notable ? '#a33' : '#555' }}>
                {furni.notable ?? furni.name}
            </span>
            {canAct && onOpen && furni.kind === 'laptop' ? (
                <button
                    type="button"
                    style={{
                        marginLeft: 'auto',
                        minWidth: 0,
                        padding: '0 6px',
                    }}
                    onClick={onOpen}
                >
                    Open
                </button>
            ) : null}
            {canAct && furni.careLabel ? (
                <button
                    type="button"
                    style={{
                        marginLeft: 'auto',
                        minWidth: 0,
                        padding: '0 6px',
                    }}
                    disabled={act.isPending}
                    onClick={() => run(true)}
                >
                    {furni.careLabel.split(' ')[0] === 'Try'
                        ? 'Fix'
                        : furni.careLabel.split(' ')[0]}
                </button>
            ) : canAct && furni.useLabel ? (
                <button
                    type="button"
                    style={{
                        marginLeft: 'auto',
                        minWidth: 0,
                        padding: '0 6px',
                    }}
                    disabled={act.isPending}
                    onClick={() => run(false)}
                    title={furni.useLabel}
                >
                    Use
                </button>
            ) : null}
        </div>
    );
}

function BodyChip({
    body,
    now,
    isMe,
}: {
    body: Body;
    now: number;
    isMe: boolean;
}) {
    const walking = walkingText(body, now);
    return (
        <span
            className="inline-flex items-center"
            style={{
                gap: 3,
                padding: '1px 5px',
                borderRadius: 9,
                background: avatarColour(body.participantId),
                color: '#fff',
                fontSize: 11,
                outline: isMe ? '2px solid #245edb' : undefined,
                opacity: walking ? 0.6 : 1,
            }}
            title={
                [body.activity, walking].filter(Boolean).join(' · ') ||
                undefined
            }
        >
            {postureIcon(body)} {body.name}
            {isMe ? ' (you)' : ''}
            {body.activity ? ' 📱' : ''}
            {walking ? ` (${walking})` : ''}
        </span>
    );
}

/** Speech between the Bodies here. Everyone in the Place hears it (Earshot is phase 6). */
function PlaceTalk({
    roomId,
    playerId,
    nearby,
    allEars,
}: {
    roomId: string;
    playerId: string | null;
    nearby: Body[];
    allEars: boolean;
}) {
    const lives = useGetWorldsPartyIdLivesSuspense(WORLD_ID).data.data ?? [];
    const { messages, phaseByPersona } = useRoomConversation(WORLD_ID, roomId);
    const speaking = [...phaseByPersona.values()]
        .filter((p) => p.phase === 'typing' || p.phase === 'streaming')
        .map((p) => (p.personaName ?? '').split(' ')[0]);
    const shown = useMemo(
        () => messages.filter((m) => m.content?.trim()).slice(-40),
        [messages],
    );
    const prompt = usePostPartyIdPrompt();
    const voice = usePostWorldsPartyIdPlacesVoice();
    const [draft, setDraft] = useState('');
    const [volume, setVolume] = useState<'talk' | 'whisper' | 'shout'>('talk');
    const [to, setTo] = useState<string>('');
    const endRef = useRef<HTMLDivElement>(null);
    useEffect(() => {
        if (shown.length) endRef.current?.scrollIntoView({ block: 'end' });
    }, [shown.length]);

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
        <fieldset
            className="flex flex-col"
            style={{ margin: 0, minHeight: 180 }}
        >
            <legend>Said out loud here</legend>
            <div
                className="min-h-0 flex-1 overflow-y-auto"
                style={{ ...panel, padding: 6 }}
            >
                {shown.length === 0 ? (
                    <div style={{ color: '#888' }}>Quiet.</div>
                ) : null}
                {shown.map((m) => {
                    const heard = canHear(m, playerId, allEars);
                    const v = volumeOf(m);
                    return (
                        <div
                            key={m.messageId}
                            style={{
                                marginBottom: 3,
                                color: heard ? undefined : '#999',
                                fontStyle:
                                    heard && v !== 'whisper'
                                        ? undefined
                                        : 'italic',
                                fontWeight: v === 'shout' ? 'bold' : undefined,
                            }}
                        >
                            <strong style={{ color: avatarColour(m.senderId) }}>
                                {nameOf(m.senderId)}
                                {v === 'whisper'
                                    ? ' (whispers)'
                                    : v === 'shout'
                                      ? ' (calls out)'
                                      : ''}
                                :
                            </strong>{' '}
                            {heard ? m.content : scribble(m.content)}
                        </div>
                    );
                })}
                {speaking.length > 0 ? (
                    <div style={{ color: '#888', fontStyle: 'italic' }}>
                        💬 {speaking.join(', ')}{' '}
                        {speaking.length === 1 ? 'is' : 'are'} about to say
                        something…
                    </div>
                ) : null}
                <div ref={endRef} />
            </div>
            <form
                className="flex"
                style={{ gap: 4, marginTop: 4 }}
                onSubmit={(e) => {
                    e.preventDefault();
                    void say();
                }}
            >
                <select
                    value={volume}
                    onChange={(e) =>
                        setVolume(e.currentTarget.value as typeof volume)
                    }
                    disabled={!playerId}
                    title="Talk reaches your spot, a shout the whole Place, a whisper one person next to you"
                >
                    <option value="talk">Talk</option>
                    <option value="whisper" disabled={nearby.length === 0}>
                        Whisper
                    </option>
                    <option value="shout">Shout</option>
                </select>
                {volume === 'whisper' && nearby.length > 0 ? (
                    <select
                        value={to}
                        onChange={(e) => setTo(e.currentTarget.value)}
                    >
                        {nearby.map((b) => (
                            <option
                                key={b.participantId}
                                value={b.participantId}
                            >
                                to {b.name}
                            </option>
                        ))}
                    </select>
                ) : null}
                <input
                    className="flex-1"
                    value={draft}
                    onChange={(e) => setDraft(e.currentTarget.value)}
                    placeholder={
                        playerId ? 'Say something out loud…' : 'Sign in to talk'
                    }
                    disabled={!playerId}
                />
                <button
                    type="submit"
                    disabled={!playerId || !draft.trim() || prompt.isPending}
                >
                    Say
                </button>
            </form>
        </fieldset>
    );
}
