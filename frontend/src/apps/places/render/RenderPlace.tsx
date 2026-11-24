import { useEffect, useState } from 'react';
import type { Body } from '../../../api/model/body';
import type { FurniView } from '../../../api/model/furniView';
import type { Place } from '../../../api/model/place';
import {
    getRenderDraftsKey,
    getWorldsPartyIdPlaces,
} from '../../../api/party-zone';
import { meshesPending } from '../scene/meshes';
import PlaceScene from '../scene/PlaceScene';

export interface RenderRequest {
    placeId: string;
    world: string;
    draft?: string;
    hour?: number;
}

interface Drawn {
    place: Place;
    places: Place[];
    bodies: Body[];
    furni: FurniView[];
}

const SETTLED_FRAMES = 45;

const ready = () => {
    document.documentElement.dataset.renderReady = '1';
};

function asDrafted(furni: FurniView[], placeId: string): FurniView[] {
    return furni
        .filter((f) => f.placeId !== placeId || f.kind !== 'feature')
        .map((f) =>
            f.placeId === placeId ? { ...f, tileX: null, tileZ: null } : f,
        );
}

async function load({ world, placeId, draft }: RenderRequest): Promise<Drawn> {
    const snapshot = (await getWorldsPartyIdPlaces(world)).data;
    const places = snapshot.places ?? [];
    const furni = snapshot.furni ?? [];
    const live = places.find((p) => p.id === placeId);
    if (!live) throw new Error(`No place ${placeId} in world ${world}`);
    let place = live;
    if (draft) {
        const stashed = await getRenderDraftsKey(draft);
        if (stashed.status !== 200) throw new Error('The draft has expired');
        place = stashed.data;
    }
    return {
        place,
        places: places.map((p) => (p.id === placeId ? place : p)),
        bodies: snapshot.bodies ?? [],
        furni: draft ? asDrafted(furni, placeId) : furni,
    };
}

export default function RenderPlace(request: RenderRequest) {
    const { world, placeId, draft, hour } = request;
    const [drawn, setDrawn] = useState<Drawn | null>(null);
    const [failed, setFailed] = useState<string | null>(null);

    useEffect(() => {
        localStorage.setItem('places.renderer', 'webgl');
        load({ world, placeId, draft }).then(setDrawn, (e: unknown) => {
            setFailed(e instanceof Error ? e.message : String(e));
            ready();
        });
    }, [world, placeId, draft]);

    useEffect(() => {
        if (!drawn) return;
        let settled = 0;
        let frame = 0;
        const tick = () => {
            const drawing = document.querySelector('canvas') !== null;
            settled = drawing && !meshesPending() ? settled + 1 : 0;
            if (settled >= SETTLED_FRAMES) return ready();
            frame = requestAnimationFrame(tick);
        };
        frame = requestAnimationFrame(tick);
        return () => cancelAnimationFrame(frame);
    }, [drawn]);

    if (failed) return <p>{failed}</p>;
    if (!drawn) return null;
    return (
        <main
            style={{
                position: 'fixed',
                inset: 0,
                display: 'flex',
                flexDirection: 'column',
            }}
        >
            <PlaceScene
                place={drawn.place}
                places={drawn.places}
                bodies={drawn.bodies}
                furni={drawn.furni}
                playerId={null}
                hour={hour}
            />
        </main>
    );
}
