import {
    CheckBox,
    CommandButton,
    FieldRow,
    GroupBox,
    HudPanel,
    Icon,
    type IconName,
    SwatchPicker,
    TextArea,
    TextBox,
    Toolbar,
    ToolbarButton,
    ToolbarSeparator,
} from '@partytown/ui';
import { useState } from 'react';
import type { Body } from '../../api/model/body';
import type { FurniView } from '../../api/model/furniView';
import type { Place } from '../../api/model/place';
import {
    useDeleteWorldsPartyIdPlacesPlaceId,
    usePostWorldsPartyIdPlacesCreate,
    usePostWorldsPartyIdPlacesPlaceIdRedesign,
    usePostWorldsPartyIdPlacesPose,
    usePostWorldsPartyIdPlacesStyle,
    usePostWorldsPartyIdPlacesThings,
    usePostWorldsPartyIdPlacesThingsArrange,
    usePostWorldsPartyIdPlacesThingsRemodel,
    usePostWorldsPartyIdPlacesThingsRemove,
} from '../../api/party-zone';
import { WORLD_ID } from '../../lib/world';
import { FLOOR_COLOURS, WALL_COLOURS } from './scene/surfaces';

const POSE_ICON: Record<string, [IconName, string]> = {
    lean: ['lean', 'Lean'],
    lounge: ['sofa', 'Lounge'],
    perch: ['chair', 'Perch'],
    dance: ['dance', 'Dance'],
    lie: ['lie-down', 'Lie down'],
    'cross-legged': ['meditate', 'Sit'],
    think: ['think', 'Think'],
    wave: ['wave', 'Wave'],
    hug: ['hug', 'Hug'],
    'slow-dance': ['hearts', 'Slow dance'],
    'high-five': ['hand', 'High five'],
    'hold-hands': ['handshake', 'Hold hands'],
};

export function PoseBar({
    place,
    me,
    bodies,
    floating = false,
}: {
    place: Place;
    me: Body;
    bodies: Body[];
    floating?: boolean;
}) {
    const pose = usePostWorldsPartyIdPlacesPose();
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
        const [icon, label] = POSE_ICON[p] ?? ['sparkle', p];
        const on =
            me.pose === p &&
            (!withBody || me.poseWith === withBody.participantId);
        return (
            <ToolbarButton
                key={`${withBody?.participantId ?? ''}:${p}`}
                icon={<Icon name={icon} />}
                pressed={on}
                disabled={pose.isPending}
                title={withBody ? `${label} ${withBody.name}` : label}
                onClick={() =>
                    on
                        ? strike(null)
                        : strike(p, withBody?.participantId ?? undefined)
                }
            >
                {withBody
                    ? `${label.split(' ')[0]} ${(withBody.name ?? '').split(' ')[0]}`
                    : label}
            </ToolbarButton>
        );
    };
    const bar = (
        <Toolbar
            labels="below"
            aria-label="Poses"
            className="justify-center self-center"
        >
            {solo.map((p) => button(p))}
            {company.length > 0 ? <ToolbarSeparator /> : null}
            {company.slice(0, 3).flatMap((b) => pairs.map((p) => button(p, b)))}
        </Toolbar>
    );
    return floating ? (
        <HudPanel className="max-w-[calc(100%-80px)]">{bar}</HudPanel>
    ) : (
        bar
    );
}

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
        <GroupBox title="Arranging" icon={<Icon name="chair" />}>
            <div className="flex items-center gap-2">
                <span className="min-w-0 flex-1">
                    {thing
                        ? `${thing.name}: click a tile to put it there.`
                        : 'Click a thing to pick it up.'}
                </span>
                <CommandButton size="small" tone="go" onClick={onDone}>
                    Done
                </CommandButton>
            </div>
            {thing && place.id ? (
                <div className="flex flex-wrap gap-1">
                    <CommandButton
                        size="small"
                        icon={<Icon name="refresh" />}
                        onClick={onRotate}
                    >
                        Turn ({rot * 90}°)
                    </CommandButton>
                    <CommandButton
                        size="small"
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
                    </CommandButton>
                    {thing.kind === 'decor' ? (
                        <>
                            <CommandButton
                                size="small"
                                tone="caution"
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
                            </CommandButton>
                            <CommandButton
                                size="small"
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
                            </CommandButton>
                        </>
                    ) : null}
                </div>
            ) : null}
            <Swatches
                label="Walls"
                colours={WALL_COLOURS}
                current={place.wallColour}
                onPick={(c) => setStyle(c, place.floorColour ?? null)}
            />
            <Swatches
                label="Floor"
                colours={FLOOR_COLOURS}
                current={place.floorColour}
                onPick={(c) => setStyle(place.wallColour ?? null, c)}
            />
        </GroupBox>
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
        <div className="flex items-center gap-2">
            <span className="w-[40px] shrink-0">{label}</span>
            <SwatchPicker
                label={label}
                colours={colours}
                value={current ?? null}
                noneLabel="Default"
                onValueChange={onPick}
            />
        </div>
    );
}

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
        <GroupBox title="Maker" icon={<Icon name="tools" />}>
            <form
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
                <FieldRow
                    label="New Place"
                    labelWidth="auto"
                    error={
                        create.isError
                            ? "Couldn't design that one. Try describing it differently."
                            : undefined
                    }
                >
                    <div className="flex items-center gap-2 justify-self-stretch">
                        <TextBox
                            value={idea}
                            className="min-w-0 flex-1"
                            placeholder="a rooftop bar with fairy lights, a record shop, a little park…"
                            onChange={(e) => setIdea(e.currentTarget.value)}
                        />
                        <CheckBox
                            checked={outdoor}
                            onChange={(e) =>
                                setOutdoor(e.currentTarget.checked)
                            }
                        >
                            outdoors
                        </CheckBox>
                        <CommandButton
                            type="submit"
                            tone="go"
                            disabled={create.isPending}
                        >
                            {create.isPending ? 'Designing…' : 'Build'}
                        </CommandButton>
                    </div>
                </FieldRow>
            </form>
            <form
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
                <FieldRow label="Make a thing">
                    <div className="flex items-center gap-2 justify-self-stretch">
                        <TextBox
                            value={what}
                            className="min-w-0 flex-1"
                            placeholder="a lava lamp shaped like a cat"
                            onChange={(e) => setWhat(e.currentTarget.value)}
                        />
                        <CommandButton
                            type="submit"
                            tone="go"
                            disabled={thing.isPending}
                        >
                            Make
                        </CommandButton>
                    </div>
                </FieldRow>
            </form>
            {(place.kind === 'home' || place.kind === 'public') && place.id ? (
                <Redesign
                    placeId={place.id}
                    name={place.name ?? 'this Place'}
                    home={place.kind === 'home'}
                />
            ) : null}
            {place.customScene && place.id && place.kind === 'public' ? (
                <CommandButton
                    size="small"
                    tone="danger"
                    className="justify-self-start"
                    icon={<Icon name="delete" />}
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
                </CommandButton>
            ) : null}
        </GroupBox>
    );
}

function Redesign({
    placeId,
    name,
    home,
}: {
    placeId: string;
    name: string;
    home: boolean;
}) {
    const redesign = usePostWorldsPartyIdPlacesPlaceIdRedesign();
    const [text, setText] = useState('');
    const [rename, setRename] = useState('');
    const [outdoor, setOutdoor] = useState(false);
    return (
        <form
            className="flex flex-col gap-1"
            onSubmit={(e) => {
                e.preventDefault();
                if (!text.trim()) return;
                if (
                    !window.confirm(
                        `Redesign ${name}? Its scene and spots are replaced; people, keys and things stay.`,
                    )
                )
                    return;
                redesign.mutate(
                    {
                        partyId: WORLD_ID,
                        placeId,
                        data: {
                            text: text.trim(),
                            name: rename.trim() || null,
                            outdoor,
                        },
                    },
                    {
                        onSuccess: () => {
                            setText('');
                            setRename('');
                        },
                    },
                );
            }}
        >
            <FieldRow
                layout="stacked"
                label={`Redesign this ${home ? 'Home' : 'Place'}`}
                error={
                    redesign.isError
                        ? "Couldn't design that one. Try describing it differently."
                        : undefined
                }
            >
                <TextArea
                    value={text}
                    rows={3}
                    maxLength={1500}
                    placeholder="a tarp under the bridge by the canal, an upturned crate, a barrel he sleeps beside…"
                    onChange={(e) => setText(e.currentTarget.value)}
                />
            </FieldRow>
            <div className="flex items-center gap-2">
                <TextBox
                    value={rename}
                    aria-label="New name"
                    className="min-w-0 flex-1"
                    maxLength={40}
                    placeholder={`new name (keeps "${name}")`}
                    onChange={(e) => setRename(e.currentTarget.value)}
                />
                <CheckBox
                    checked={outdoor}
                    onChange={(e) => setOutdoor(e.currentTarget.checked)}
                >
                    outdoors
                </CheckBox>
                <CommandButton
                    type="submit"
                    tone="go"
                    disabled={redesign.isPending}
                >
                    {redesign.isPending ? 'Designing…' : 'Redesign'}
                </CommandButton>
            </div>
        </form>
    );
}
