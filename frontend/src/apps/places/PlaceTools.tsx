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

const POSE_ICON: Record<string, [string, string]> = {
    lean: ['🫱', 'Lean'],
    lounge: ['🛋️', 'Lounge'],
    perch: ['🪑', 'Perch'],
    dance: ['💃', 'Dance'],
    lie: ['😴', 'Lie down'],
    'cross-legged': ['🧘', 'Sit'],
    think: ['🤔', 'Think'],
    wave: ['👋', 'Wave'],
    hug: ['🤗', 'Hug'],
    'slow-dance': ['💞', 'Slow dance'],
    'high-five': ['✋', 'High five'],
    'hold-hands': ['🤝', 'Hold hands'],
};

/** Hang-out poses at your spot (ADR 0025), as an IMVU-style dock over the view: the spot's own, think and wave, and pair ones with whoever's next to you. */
export function PoseBar({
    place,
    me,
    bodies,
    bottom,
}: {
    place: Place;
    me: Body;
    bodies: Body[];
    bottom?: number;
}) {
    const pose = usePostWorldsPartyIdPlacesPose();
    // Mid-walk across the room, the dock is already for where you're going.
    const spot = (place.spots ?? []).find(
        (s) => s.id === (me.destSpotId ?? me.spotId),
    );
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
    const button = (p: string, withBody?: Body) => {
        const [icon, label] = POSE_ICON[p] ?? ['✨', p];
        const on =
            me.pose === p &&
            (!withBody || me.poseWith === withBody.participantId);
        return (
            <button
                key={`${withBody?.participantId ?? ''}:${p}`}
                type="button"
                className={`pl-pose${withBody ? ' pl-pose--pair' : ''}`}
                aria-pressed={on}
                disabled={pose.isPending}
                title={withBody ? `${label} ${withBody.name}` : label}
                onClick={() =>
                    on
                        ? strike(null)
                        : strike(p, withBody?.participantId ?? undefined)
                }
            >
                <b>{icon}</b>
                {withBody
                    ? `${label.split(' ')[0]} ${(withBody.name ?? '').split(' ')[0]}`
                    : label}
            </button>
        );
    };
    return (
        <div
            className="pl-dock"
            style={
                bottom === undefined
                    ? {
                          position: 'static',
                          transform: 'none',
                          alignSelf: 'center',
                      }
                    : { bottom }
            }
        >
            {solo.map((p) => button(p))}
            {company.length > 0 ? <span className="pl-dock-sep" /> : null}
            {company.slice(0, 3).flatMap((b) => pairs.map((p) => button(p, b)))}
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
        <div className="pl-card pl-card--tool flex flex-col" style={{ gap: 5 }}>
            <div className="flex items-center" style={{ gap: 6 }}>
                <strong className="pl-card-title">🪑 Arranging</strong>
                <span style={{ color: '#5b6b8c' }}>
                    {thing
                        ? `${thing.name}: click a tile to put it there.`
                        : 'Click a thing to pick it up.'}
                </span>
                <button
                    type="button"
                    className="pl-btn pl-btn--go pl-btn--small"
                    style={{ marginLeft: 'auto' }}
                    onClick={onDone}
                >
                    Done
                </button>
            </div>
            {thing && place.id ? (
                <div className="flex flex-wrap" style={{ gap: 4 }}>
                    <button
                        type="button"
                        className="pl-btn pl-btn--small"
                        onClick={onRotate}
                    >
                        ↻ Turn ({rot * 90}°)
                    </button>
                    <button
                        type="button"
                        className="pl-btn pl-btn--small"
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
                                className="pl-btn pl-btn--warm pl-btn--small"
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
                                className="pl-btn pl-btn--small"
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
            <span style={{ color: '#5b6b8c', width: 40 }}>{label}</span>
            {colours.map((c) => (
                <button
                    key={c}
                    type="button"
                    className="pl-swatch"
                    title={c}
                    aria-pressed={current === c}
                    onClick={() => onPick(c)}
                    style={{ background: c }}
                />
            ))}
            <button
                type="button"
                className="pl-btn pl-btn--ghost pl-btn--small"
                style={{ color: '#245edb', borderColor: '#9fb3df' }}
                onClick={() => onPick(null)}
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
        <div className="pl-card pl-card--tool flex flex-col" style={{ gap: 5 }}>
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
                <strong className="pl-card-title">✨ New Place:</strong>
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
                <button
                    type="submit"
                    className="pl-btn pl-btn--go"
                    disabled={create.isPending}
                >
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
                <strong className="pl-card-title">🎁 Make a thing:</strong>
                <input
                    value={what}
                    style={{ flex: 1 }}
                    placeholder="a lava lamp shaped like a cat"
                    onChange={(e) => setWhat(e.currentTarget.value)}
                />
                <button
                    type="submit"
                    className="pl-btn pl-btn--go"
                    disabled={thing.isPending}
                >
                    Make
                </button>
            </form>
            {place.customScene && place.id ? (
                <button
                    type="button"
                    className="pl-btn pl-btn--warm pl-btn--small"
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
