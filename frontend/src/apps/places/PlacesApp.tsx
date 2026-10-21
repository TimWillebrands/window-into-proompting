import { useQueryClient } from '@tanstack/react-query';
import { useEffect, useMemo, useRef, useState } from 'react';
import {
    getGetWorldsPartyIdPlacesQueryKey,
    useGetWorldsPartyIdLivesSuspense,
    useGetWorldsPartyIdPlacesSituationParticipantId,
    useGetWorldsPartyIdPlacesSuspense,
    usePostPartyIdPrompt,
    usePostWorldsPartyIdPlacesFurni,
    usePostWorldsPartyIdPlacesMove,
    usePostWorldsPartyIdPlacesVoice,
} from '#api/party-zone';
import {
    useChatGroupMessages,
    useRealtimeStoreActions,
} from '#lib/realtime-store';
import type { Body } from '../../api/model/body';
import type { FurniView } from '../../api/model/furniView';
import type { Place } from '../../api/model/place';
import { usePlayerId, useWorldEvents, WORLD_ID } from '../../lib/world';
import type { WorldEventData } from '../../lib/world-bus';
import PlaceCanvas from './iso/PlaceCanvas';
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
                                <span>{p.kind === 'home' ? '🏠' : '🏢'}</span>
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
    furni,
    me,
    playerId,
    now,
}: {
    place: Place;
    bodies: Body[];
    allBodies: Body[];
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
    const amHere = !!me && me.placeId === place.id && !me.destPlaceId;
    const heading = !!me && me.destPlaceId === place.id;
    const canGo = !!me && mayEnter(place, playerId) && !amHere && !heading;

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
                {canGo ? (
                    <button
                        type="button"
                        onClick={() => goTo()}
                        disabled={move.isPending}
                    >
                        Go here
                    </button>
                ) : null}
            </div>
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
            <PlaceCanvas
                place={place}
                bodies={allBodies}
                furni={furni}
                playerId={playerId}
                allEars={allEars}
                onSpotClick={
                    amHere || canGo ? (spotId) => goTo(spotId) : undefined
                }
            />
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

function FurniRow({
    furni,
    canAct,
    playerId,
}: {
    furni: FurniView;
    canAct: boolean;
    playerId: string | null;
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
            {postureIcon(body)} {isMe ? 'You' : body.name}
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
    const messages = useChatGroupMessages(roomId);
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

    const nameOf = (id: string | undefined) =>
        id === playerId
            ? 'You'
            : (lives.find((l) => l.personaId === id)?.name ?? 'Someone');

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
