import { useState } from 'react';
import type { Body } from '../../api/model/body';
import type { FurniView } from '../../api/model/furniView';
import type { Place } from '../../api/model/place';
import {
    useDeleteWorldsPartyIdPlacesPlaceId,
    usePostWorldsPartyIdPlacesCreate,
    usePostWorldsPartyIdPlacesPose,
    usePostWorldsPartyIdPlacesStyle,
    usePostWorldsPartyIdPlacesThings,
    usePostWorldsPartyIdPlacesThingsArrange,
    usePostWorldsPartyIdPlacesThingsRemodel,
    usePostWorldsPartyIdPlacesThingsRemove,
} from '../../api/party-zone';
import { WORLD_ID } from '../../lib/world';

const panel: React.CSSProperties = {
    border: '1px solid #b7c4d7',
    borderRadius: 3,
    background: '#fff',
    padding: '4px 6px',
};

const POSE_LABEL: Record<string, string> = {
    lean: '🫱 Lean',
    lounge: '🛋 Lounge',
    perch: '🪑 Perch',
    dance: '💃 Dance',
    lie: '😴 Lie down',
    'cross-legged': '🧘 Cross-legged',
    think: '🤔 Think',
    wave: '👋 Wave',
    hug: '🤗 Hug',
    'slow-dance': '💞 Slow dance',
    'high-five': '✋ High five',
    'hold-hands': '🤝 Hold hands',
};

/** Hang-out poses at your spot (ADR 0025): the spot's own, think and wave, and pair ones with whoever's next to you. */
export function PoseBar({
    place,
    me,
    bodies,
}: {
    place: Place;
    me: Body;
    bodies: Body[];
}) {
    const pose = usePostWorldsPartyIdPlacesPose();
    const spot = (place.spots ?? []).find((s) => s.id === me.spotId);
    if (!spot) return null;
    const company = bodies.filter(
        (b) =>
            b.participantId !== me.participantId &&
            b.placeId === place.id &&
            !b.destPlaceId &&
            b.spotId === spot.id &&
            !b.asleep,
    );
    const solo = [...(spot.poses ?? []), 'think', 'wave'];
    const pairs = [
        'hug',
        'high-five',
        'hold-hands',
        ...((spot.poses ?? []).includes('dance') ? ['slow-dance'] : []),
    ];
    const strike = (p: string | null, withId?: string) =>
        pose.mutate({
            partyId: WORLD_ID,
            data: {
                pose: p,
                with: withId ?? null,
                participantId: me.participantId ?? null,
            },
        });
    return (
        <div
            className="flex flex-wrap items-center"
            style={{ ...panel, gap: 4 }}
        >
            <span style={{ color: '#555' }}>Pose:</span>
            {solo.map((p) => (
                <button
                    key={p}
                    type="button"
                    disabled={pose.isPending}
                    aria-pressed={me.pose === p}
                    onClick={() => strike(p)}
                >
                    {POSE_LABEL[p] ?? p}
                </button>
            ))}
            {company.flatMap((b) =>
                pairs.map((p) => (
                    <button
                        key={`${b.participantId}:${p}`}
                        type="button"
                        disabled={pose.isPending}
                        aria-pressed={
                            me.pose === p && me.poseWith === b.participantId
                        }
                        onClick={() => strike(p, b.participantId ?? undefined)}
                    >
                        {POSE_LABEL[p] ?? p} {(b.name ?? '').split(' ')[0]}
                    </button>
                )),
            )}
            {me.pose ? (
                <button type="button" onClick={() => strike(null)}>
                    Stop
                </button>
            ) : null}
        </div>
    );
}

const WALLS = [
    '#e9dcc4',
    '#d8e2dc',
    '#f1d6c4',
    '#cfd8e6',
    '#e8e1f0',
    '#f3e6b8',
    '#b8d8c8',
    '#f4c7c3',
    '#3d405b',
    '#2f3e46',
];
const FLOORS = [
    '#c8a173',
    '#a67b5b',
    '#d9c3a0',
    '#8d6e63',
    '#b0a090',
    '#5e6b73',
    '#e8e4dc',
    '#7a2f3d',
];

/** Your own Home (or anything, as the author): move and turn things, take decor down, pick wall and floor colours. */
export function ArrangeTools({
    place,
    furni,
    selected,
    rot,
    onRotate,
    onDone,
    participantId,
}: {
    place: Place;
    furni: FurniView[];
    selected: string | null;
    rot: number;
    onRotate: () => void;
    onDone: () => void;
    participantId: string | null;
}) {
    const arrange = usePostWorldsPartyIdPlacesThingsArrange();
    const remove = usePostWorldsPartyIdPlacesThingsRemove();
    const remodel = usePostWorldsPartyIdPlacesThingsRemodel();
    const style = usePostWorldsPartyIdPlacesStyle();
    const thing = furni.find((f) => f.furniId === selected);
    const setStyle = (wall: string | null, floor: string | null) =>
        place.id &&
        style.mutate({
            partyId: WORLD_ID,
            data: { placeId: place.id, wall, floor, participantId },
        });
    return (
        <div
            className="flex flex-col"
            style={{ ...panel, gap: 4, background: '#f3f8ff' }}
        >
            <div className="flex items-center" style={{ gap: 6 }}>
                <strong>Arranging</strong>
                <span style={{ color: '#555' }}>
                    {thing
                        ? `${thing.name}: click a tile to put it there.`
                        : 'Click a thing to pick it up.'}
                </span>
                <button
                    type="button"
                    style={{ marginLeft: 'auto' }}
                    onClick={onDone}
                >
                    Done
                </button>
            </div>
            {thing && place.id ? (
                <div className="flex flex-wrap" style={{ gap: 4 }}>
                    <button type="button" onClick={onRotate}>
                        ↻ Turn ({rot * 90}°)
                    </button>
                    <button
                        type="button"
                        onClick={() =>
                            arrange.mutate({
                                partyId: WORLD_ID,
                                data: {
                                    placeId: place.id ?? '',
                                    furniId: thing.furniId ?? '',
                                    x: null,
                                    z: null,
                                    rot: 0,
                                    participantId,
                                },
                            })
                        }
                    >
                        Put back
                    </button>
                    {thing.kind === 'decor' ? (
                        <>
                            <button
                                type="button"
                                onClick={() =>
                                    remove.mutate({
                                        partyId: WORLD_ID,
                                        data: {
                                            placeId: place.id ?? '',
                                            furniId: thing.furniId ?? '',
                                            participantId,
                                        },
                                    })
                                }
                            >
                                Take down
                            </button>
                            <button
                                type="button"
                                title="Have the model made again"
                                onClick={() =>
                                    remodel.mutate({
                                        partyId: WORLD_ID,
                                        data: {
                                            placeId: place.id ?? '',
                                            furniId: thing.furniId ?? '',
                                            participantId,
                                        },
                                    })
                                }
                            >
                                Remake
                            </button>
                        </>
                    ) : null}
                </div>
            ) : null}
            <Swatches
                label="Walls"
                colours={WALLS}
                current={place.wallColour}
                onPick={(c) => setStyle(c, place.floorColour ?? null)}
            />
            <Swatches
                label="Floor"
                colours={FLOORS}
                current={place.floorColour}
                onPick={(c) => setStyle(place.wallColour ?? null, c)}
            />
        </div>
    );
}

function Swatches({
    label,
    colours,
    current,
    onPick,
}: {
    label: string;
    colours: string[];
    current?: string | null;
    onPick: (c: string | null) => void;
}) {
    return (
        <div className="flex items-center" style={{ gap: 3 }}>
            <span style={{ color: '#555', width: 40 }}>{label}</span>
            {colours.map((c) => (
                <button
                    key={c}
                    type="button"
                    title={c}
                    aria-pressed={current === c}
                    onClick={() => onPick(c)}
                    style={{
                        width: 18,
                        height: 18,
                        minWidth: 0,
                        padding: 0,
                        background: c,
                        outline:
                            current === c ? '2px solid #245edb' : undefined,
                    }}
                />
            ))}
            <button
                type="button"
                onClick={() => onPick(null)}
                style={{ minWidth: 0 }}
            >
                Default
            </button>
        </div>
    );
}

/** Author: have a new Place designed, or a thing made here. */
export function MakerTools({
    place,
    playerId,
}: {
    place: Place;
    playerId: string | null;
}) {
    const create = usePostWorldsPartyIdPlacesCreate();
    const remove = useDeleteWorldsPartyIdPlacesPlaceId();
    const thing = usePostWorldsPartyIdPlacesThings();
    const [idea, setIdea] = useState('');
    const [outdoor, setOutdoor] = useState(false);
    const [what, setWhat] = useState('');
    return (
        <div
            className="flex flex-col"
            style={{ ...panel, gap: 4, background: '#fbf7ff' }}
        >
            <form
                className="flex items-center"
                style={{ gap: 4 }}
                onSubmit={(e) => {
                    e.preventDefault();
                    if (!idea.trim()) return;
                    create.mutate(
                        {
                            partyId: WORLD_ID,
                            data: { text: idea.trim(), outdoor, madeBy: null },
                        },
                        { onSuccess: () => setIdea('') },
                    );
                }}
            >
                <span style={{ color: '#555' }}>New Place:</span>
                <input
                    value={idea}
                    style={{ flex: 1 }}
                    placeholder="a rooftop bar with fairy lights, a record shop, a little park…"
                    onChange={(e) => setIdea(e.currentTarget.value)}
                />
                <label style={{ whiteSpace: 'nowrap' }}>
                    <input
                        type="checkbox"
                        checked={outdoor}
                        onChange={(e) => setOutdoor(e.currentTarget.checked)}
                    />{' '}
                    outdoors
                </label>
                <button type="submit" disabled={create.isPending}>
                    {create.isPending ? 'Designing…' : 'Build'}
                </button>
            </form>
            {create.isError ? (
                <span style={{ color: '#a00' }}>
                    Couldn't design that one. Try describing it differently.
                </span>
            ) : null}
            <form
                className="flex items-center"
                style={{ gap: 4 }}
                onSubmit={(e) => {
                    e.preventDefault();
                    if (!what.trim() || !place.id) return;
                    thing.mutate(
                        {
                            partyId: WORLD_ID,
                            data: {
                                placeId: place.id,
                                text: what.trim(),
                                ownerId: playerId,
                            },
                        },
                        { onSuccess: () => setWhat('') },
                    );
                }}
            >
                <span style={{ color: '#555' }}>Make a thing here:</span>
                <input
                    value={what}
                    style={{ flex: 1 }}
                    placeholder="a lava lamp shaped like a cat"
                    onChange={(e) => setWhat(e.currentTarget.value)}
                />
                <button type="submit" disabled={thing.isPending}>
                    Make
                </button>
            </form>
            {place.customScene && place.id ? (
                <button
                    type="button"
                    style={{ alignSelf: 'flex-start' }}
                    onClick={() => {
                        if (
                            place.id &&
                            window.confirm(
                                `Remove ${place.name}? Anyone there walks home.`,
                            )
                        )
                            remove.mutate({
                                partyId: WORLD_ID,
                                placeId: place.id,
                            });
                    }}
                >
                    Remove this Place
                </button>
            ) : null}
        </div>
    );
}
