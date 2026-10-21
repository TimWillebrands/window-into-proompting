import { useQueryClient } from '@tanstack/react-query';
import { useEffect, useMemo, useRef, useState } from 'react';
import {
    getGetWorldsPartyIdPlacesQueryKey,
    useGetWorldsPartyIdLivesSuspense,
    useGetWorldsPartyIdPlacesSituationParticipantId,
    useGetWorldsPartyIdPlacesSuspense,
    usePostPartyIdPrompt,
    usePostWorldsPartyIdPlacesMove,
} from '#api/party-zone';
import {
    useChatGroupMessages,
    useRealtimeStoreActions,
} from '#lib/realtime-store';
import type { Body } from '../../api/model/body';
import type { Place } from '../../api/model/place';
import { usePlayerId, useWorldEvents, WORLD_ID } from '../../lib/world';
import type { WorldEventData } from '../../lib/world-bus';
import {
    avatarColour,
    bodiesIn,
    bodySpotId,
    mayEnter,
    navigatorPlaces,
    placeLabel,
    postureIcon,
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
        if ((envelope.data as WorldEventData).kind !== 'body') return;
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
                    me={me}
                    playerId={playerId}
                    now={now}
                />
            ) : null}
        </div>
    );
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
    me,
    playerId,
    now,
}: {
    place: Place;
    bodies: Body[];
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
        <div className="flex min-w-0 flex-1 flex-col" style={{ gap: 6 }}>
            <div className="flex items-center" style={{ gap: 8 }}>
                <div className="min-w-0 flex-1">
                    <h3 style={{ margin: 0, fontSize: 14 }}>
                        {placeLabel(place, playerId)}
                    </h3>
                    <div style={{ color: '#666' }}>{place.description}</div>
                </div>
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
                    return (
                        <button
                            key={s.id}
                            type="button"
                            disabled={
                                !amHere || mineSpot || full || move.isPending
                            }
                            onClick={() => goTo(s.id)}
                            className="text-left"
                            style={{
                                padding: 6,
                                minHeight: 64,
                                alignItems: 'flex-start',
                            }}
                            title={amHere ? `Move to ${s.name}` : undefined}
                        >
                            <div>
                                <strong>{s.name}</strong>{' '}
                                <span style={{ color: '#888' }}>
                                    {at.length}/{s.capacity}
                                </span>
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
                        </button>
                    );
                })}
            </div>
            {amHere && place.talkRoomId ? (
                <PlaceTalk roomId={place.talkRoomId} playerId={playerId} />
            ) : amHere ? (
                <div style={{ color: '#666' }}>Nobody to talk to here yet.</div>
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
}: {
    roomId: string;
    playerId: string | null;
}) {
    const lives = useGetWorldsPartyIdLivesSuspense(WORLD_ID).data.data ?? [];
    const messages = useChatGroupMessages(roomId);
    const shown = useMemo(
        () => messages.filter((m) => m.content?.trim()).slice(-40),
        [messages],
    );
    const prompt = usePostPartyIdPrompt();
    const [draft, setDraft] = useState('');
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
        await prompt.mutateAsync({
            id: WORLD_ID,
            data: { chatGroupId: roomId, prompt: text, senderId: playerId },
        });
    };

    return (
        <fieldset
            className="flex min-h-0 flex-1 flex-col"
            style={{ margin: 0 }}
        >
            <legend>Said out loud here</legend>
            <div
                className="min-h-0 flex-1 overflow-y-auto"
                style={{ ...panel, padding: 6 }}
            >
                {shown.length === 0 ? (
                    <div style={{ color: '#888' }}>Quiet.</div>
                ) : null}
                {shown.map((m) => (
                    <div key={m.messageId} style={{ marginBottom: 3 }}>
                        <strong style={{ color: avatarColour(m.senderId) }}>
                            {nameOf(m.senderId)}:
                        </strong>{' '}
                        {m.content}
                    </div>
                ))}
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
