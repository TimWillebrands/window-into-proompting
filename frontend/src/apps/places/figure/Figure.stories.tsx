import { Canvas, useThree } from '@react-three/fiber';
import type { Meta, StoryObj } from '@storybook/react-vite';
import { useEffect, useMemo } from 'react';
import type { Appearance } from '../../../api/model/appearance';
import type { Body } from '../../../api/model/body';
import type { Place } from '../../../api/model/place';
import '../places.css';
import '../../wardrobe/wardrobe.css';
import DressingRoom from '../../wardrobe/DressingRoom';
import { type Look, lookOf as paletteLook } from '../iso/appearance';
import Avatar, { type AvatarState } from '../scene/Avatar';
import PlaceScene from '../scene/PlaceScene';
import type { SceneDef } from '../scene/scene';
import type { FigureDoc } from './doc';
import { EXTRAS, figureDoc, HAIR } from './dress';
import { SAMPLES } from './samples';
import designed from './samples.llm.json';

const LOOK: Look = {
    skin: '#f1c9a5',
    hair: '#5a3a22',
    hairStyle: 'short',
    top: '#d9534f',
    bottom: '#3b3f58',
    accessory: 'none',
    topStyle: 'tshirt',
    bottomStyle: 'jeans',
    shoes: '#2b2b2b',
    shoeStyle: 'sneakers',
    legwear: 'none',
    facialHair: 'none',
    build: 'average',
    height: 'average',
    eyes: '#1d1d1d',
};

const lookOf = (design: FigureDoc): Look => ({
    ...LOOK,
    shoeStyle: 'sneakers',
    legwear: 'none',
    design,
});

function Person({
    doc,
    x,
    turn,
    hangout,
    close,
    talking,
    phone,
    moving,
    seat,
}: {
    doc: FigureDoc;
    x: number;
    turn: number;
    hangout: string | null;
    close: boolean;
    talking?: boolean;
    phone?: boolean;
    moving?: boolean;
    seat?: number;
}) {
    const look = useMemo(() => lookOf(doc), [doc]);
    const state = useMemo(
        () => (): AvatarState => ({
            pose: {
                tile: { x: -0.5, y: -0.5 },
                moving: !!moving,
                toward: moving
                    ? { x: -0.5 + Math.sin(turn), y: -0.5 + Math.cos(turn) }
                    : undefined,
                slot: {
                    tile: { x: -0.5, y: -0.5 },
                    face: 0,
                    seat: seat !== undefined,
                    h: seat ?? 0,
                },
            },
            ground: 0,
            asleep: false,
            lying: false,
            phone: !!phone,
            talking: !!talking,
            hangout,
            partner: null,
        }),
        [hangout, talking, phone, moving, turn, seat],
    );
    return (
        <group position={[x, 0, 0]} rotation={[0, moving ? 0 : turn, 0]}>
            <Avatar look={look} isMe={false} state={state} close={close} />
        </group>
    );
}

function Aim({ y }: { y: number }) {
    const camera = useThree((s) => s.camera);
    useEffect(() => {
        camera.lookAt(0, y, 0);
    }, [camera, y]);
    return null;
}

interface LineupProps {
    close: boolean;
    turn: number;
    hangout?: string | null;
    portrait?: boolean;
    talking?: boolean;
    phone?: boolean;
    moving?: boolean;
    seat?: number;
}

function Sheet({
    looks,
    columns = 9,
    y = 0.78,
    distance = 9,
}: {
    looks: Partial<Look>[];
    columns?: number;
    y?: number;
    distance?: number;
}) {
    const docs = useMemo(
        () => looks.map((l) => figureDoc({ ...LOOK, ...l })),
        [looks],
    );
    const rows = Math.ceil(docs.length / columns);
    return (
        <div
            style={{
                width: 1400,
                height: 340 * rows + 40,
                background: '#39304a',
            }}
        >
            <Canvas
                flat
                dpr={1}
                orthographic
                camera={{ position: [0, 1.2, distance], zoom: 200 }}
            >
                <color attach="background" args={['#3d3350']} />
                <ambientLight intensity={1.25} />
                <directionalLight position={[2, 4, 3]} intensity={2.1} />
                <Aim y={y} />
                {docs.map((d, i) => (
                    <group
                        key={JSON.stringify(looks[i])}
                        position={[
                            ((i % columns) - (columns - 1) / 2) * 0.72,
                            -Math.floor(i / columns) * 1.65 + (rows - 1) * 0.82,
                            0,
                        ]}
                    >
                        <Person
                            doc={d}
                            x={0}
                            turn={0.35}
                            hangout={null}
                            close={false}
                        />
                    </group>
                ))}
            </Canvas>
        </div>
    );
}

/** Real answers to the production prompt, from models that saw nothing else, through the production path. */
function Designed({ turn = 0.35 }: { turn?: number }) {
    const docs = useMemo(
        () =>
            designed.map((d) => ({
                id: d.id,
                doc: figureDoc(
                    paletteLook(
                        {
                            ...d.picks,
                            design: JSON.stringify(d.design),
                        } as Appearance,
                        '#808080',
                    ),
                ),
            })),
        [],
    );
    return (
        <div style={{ width: 1400, height: 640, background: '#39304a' }}>
            <Canvas
                flat
                shadows="percentage"
                dpr={1}
                camera={{ position: [0, 1, 5.6], fov: 26 }}
            >
                <color attach="background" args={['#3d3350']} />
                <ambientLight intensity={1.2} />
                <directionalLight
                    position={[2, 4, 3]}
                    intensity={2.2}
                    castShadow
                />
                <Aim y={0.68} />
                {docs.map(({ id, doc }, i) => (
                    <Person
                        key={id}
                        doc={doc}
                        x={(i - (docs.length - 1) / 2) * 0.78}
                        turn={turn}
                        hangout={null}
                        close
                    />
                ))}
                <mesh rotation={[-Math.PI / 2, 0, 0]} receiveShadow>
                    <circleGeometry args={[7, 32]} />
                    <meshStandardMaterial color="#5b4d73" />
                </mesh>
            </Canvas>
        </div>
    );
}

const hairLooks = Object.keys(HAIR).map((hairStyle, i) => ({
    hairStyle,
    hair: ['#1f1b18', '#5a3a22', '#e3c36b', '#b2462a', '#e57fb0', '#3b6fd6'][
        i % 6
    ],
    topStyle: [
        'tshirt',
        'hoodie',
        'band-tee',
        'flannel',
        'long-sleeve',
        'tank',
    ][i % 6],
    bottomStyle: [
        'jeans',
        'skinny-jeans',
        'baggy-jeans',
        'bootcut',
        'plaid-skirt',
        'cargo',
    ][i % 6],
    shoeStyle: [
        'sneakers',
        'skate-shoes',
        'hi-tops',
        'boots',
        'platforms',
        'loafers',
    ][i % 6],
    skin: ['#f6d7bf', '#d6a77a', '#9a6440', '#f1c9a5', '#5e3b24', '#c68c5d'][
        i % 6
    ],
}));

const extraLooks = Object.keys(EXTRAS).map((accessory, i) => ({
    accessory,
    topStyle: [
        'tshirt',
        'hoodie',
        'band-tee',
        'sweater',
        'shirt',
        'jacket',
        'mesh',
        'corset',
        'tracksuit',
        'suit',
        'dress',
        'baby-tee',
    ][i % 12],
    legwear: ['none', 'knee-socks', 'striped-socks', 'tights', 'fishnets'][
        i % 5
    ],
    bottomStyle: ['mini-skirt', 'shorts', 'plaid-skirt', 'jeans'][i % 4],
    shoeStyle: ['heels', 'sandals', 'platforms', 'boots'][i % 4],
    hairStyle: ['bob', 'long', 'short', 'spiky'][i % 4],
    top: ['#4a7fd6', '#2b2b2b', '#6e2233', '#f3d34a'][i % 4],
    bottom: ['#2c3e66', '#2b2b2b', '#6e2233', '#7a5233'][i % 4],
}));

function Lineup({
    close,
    turn,
    hangout = null,
    portrait = false,
    talking,
    phone,
    moving,
    seat,
}: LineupProps) {
    return (
        <div style={{ width: 1200, height: 640, background: '#39304a' }}>
            <Canvas
                flat
                shadows="percentage"
                dpr={1}
                camera={
                    portrait
                        ? { position: [0, 1.3, 4], fov: 11 }
                        : { position: [0, 1, 5.2], fov: 26 }
                }
            >
                <color attach="background" args={['#3d3350']} />
                <ambientLight intensity={1.2} />
                <directionalLight
                    position={[2, 4, 3]}
                    intensity={2.2}
                    castShadow
                />
                <Aim y={portrait ? 1.1 : 0.66} />
                {SAMPLES.map((d, i) => (
                    <Person
                        key={d.body.skin + d.top.color}
                        doc={d}
                        x={(i - (SAMPLES.length - 1) / 2) * 0.85}
                        turn={turn}
                        hangout={hangout}
                        close={close}
                        talking={talking}
                        phone={phone}
                        moving={moving}
                        seat={seat}
                    />
                ))}
                <mesh rotation={[-Math.PI / 2, 0, 0]} receiveShadow>
                    <circleGeometry args={[6, 32]} />
                    <meshStandardMaterial color="#5b4d73" />
                </mesh>
            </Canvas>
        </div>
    );
}

const meta: Meta<typeof Lineup> = {
    title: 'Places/Figure',
    component: Lineup,
    parameters: { layout: 'fullscreen' },
};
export default meta;
type Story = StoryObj<typeof Lineup>;

export const Close: Story = { args: { close: true, turn: 0.35 } };
export const Scene: Story = { args: { close: false, turn: 0.35 } };
export const Portrait: Story = {
    args: { close: true, turn: 0.3, portrait: true },
};
export const Back: Story = { args: { close: true, turn: 3.5 } };
export const Walk: Story = { args: { close: true, turn: 0.9, moving: true } };
export const Talk: Story = { args: { close: true, turn: 0.4, talking: true } };
export const Phone: Story = { args: { close: true, turn: 0.4, phone: true } };
export const Sit: Story = { args: { close: true, turn: 0.9, seat: 0.45 } };
export const SitScene: Story = {
    args: { close: false, turn: 0.9, seat: 0.45 },
};
export const Pose: Story = {
    args: { close: true, turn: 0.5, hangout: 'think' },
};
export const Lean: Story = {
    args: { close: true, turn: 0.5, hangout: 'lean' },
};
export const Wave: Story = {
    args: { close: true, turn: 0.3, hangout: 'wave' },
};
export const CrossLegged: Story = {
    args: { close: true, turn: 0.5, hangout: 'cross-legged' },
};
export const Dance: Story = {
    args: { close: true, turn: 0.5, hangout: 'dance' },
};

const lounge: Omit<SceneDef, 'id'> = {
    map: `
        ##ww####ww####
        #............#
        #............#
        #............#
        #............#
        D............#
        #............#
        ##############
    `,
    props: [
        { p: 'sofa', at: [2, 1], spot: 'sofa' },
        { p: 'bar-stool', at: [8, 2], spot: 'bar' },
        { p: 'big-plant', at: [12, 1] },
    ],
    spots: {
        sofa: { at: [3, 3] },
        floor: { at: [6, 4] },
        bar: { at: [9, 3] },
        door: { at: [2, 5] },
    },
    floor: '#7a6a8a',
    wall: '#3f3550',
};

const place: Place = {
    id: 'story-lounge',
    name: 'the lounge',
    kind: 'public',
    scene: 'custom',
    customScene: JSON.stringify(lounge),
    spots: ['sofa', 'floor', 'bar', 'door'].map((id) => ({
        id,
        name: id,
        capacity: 3,
    })),
};

const body = (i: number, spot: string, o: Partial<Body> = {}): Body =>
    ({
        participantId: `p${i}`,
        name: ['Marit', 'Jonas', 'Sol', 'Denise', 'Vlad', 'Ottilie', 'Remy'][i],
        placeId: place.id,
        spotId: spot,
        posture: 'Standing',
        appearance: {
            ...designed[i].picks,
            design: JSON.stringify(designed[i].design),
        },
        ...o,
    }) as Body;

const bodies: Body[] = [
    body(0, 'bar'),
    body(1, 'door'),
    body(2, 'floor', { pose: 'dance' }),
    body(3, 'bar'),
    body(4, 'floor'),
    body(5, 'sofa', { posture: 'Sitting' }),
    body(6, 'sofa', { posture: 'Sitting' }),
];

function Room({ pixel, hour = 13 }: { pixel: boolean; hour?: number }) {
    useMemo(
        () => localStorage.setItem('places.pixel', pixel ? 'on' : 'off'),
        [pixel],
    );
    return (
        <div
            style={{
                width: 1000,
                height: 640,
                display: 'flex',
                flexDirection: 'column',
            }}
        >
            <PlaceScene
                place={place}
                places={[place]}
                bodies={bodies}
                furni={[]}
                playerId={null}
                hour={hour}
            />
        </div>
    );
}

export const Hairstyles: StoryObj<typeof Sheet> = {
    render: () => <Sheet looks={hairLooks} />,
};
export const Accessories: StoryObj<typeof Sheet> = {
    render: () => <Sheet looks={extraLooks} />,
};

export const LlmDesigned: StoryObj<typeof Designed> = {
    render: () => <Designed />,
};
export const LlmDesignedBack: StoryObj<typeof Designed> = {
    render: () => <Designed turn={3.4} />,
};

export const FittingRoom: StoryObj<typeof DressingRoom> = {
    render: () => (
        <div style={{ width: 340, height: 460, display: 'flex' }}>
            <DressingRoom
                appearance={
                    {
                        skin: 'fair',
                        design: JSON.stringify(SAMPLES[0]),
                    } as Appearance
                }
                fallbackColour="#d9534f"
                mood="idle"
            />
        </div>
    ),
};

export const InTheRoom: StoryObj<typeof Room> = {
    render: () => <Room pixel={false} />,
};
export const InTheRoomAtNight: StoryObj<typeof Room> = {
    render: () => <Room pixel={false} hour={22} />,
};
export const InTheRoomPixel: StoryObj<typeof Room> = {
    render: () => <Room pixel />,
};
