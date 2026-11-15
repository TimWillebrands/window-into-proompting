import { type ImgHTMLAttributes, useEffect, useState } from 'react';
import { useGetWorldsPartyIdPlaces } from '#api/party-zone';
import { WORLD_ID } from '../lib/world';

const STAND_IN = `data:image/svg+xml,${encodeURIComponent(
    '<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 32 32"><rect width="32" height="32" fill="#d7dfeb"/><circle cx="16" cy="12.5" r="6" fill="#a3b1c6"/><path d="M4 32c1-9 6-12 12-12s11 3 12 12z" fill="#a3b1c6"/></svg>',
)}`;

const STALE_MS = 5 * 60_000;

export function usePortrait(who: string | null | undefined): string {
    const [src, setSrc] = useState(STAND_IN);
    const { data: appearance, isPending } = useGetWorldsPartyIdPlaces(
        WORLD_ID,
        {
            query: {
                staleTime: STALE_MS,
                enabled: !!who,
                select: (r) =>
                    r.data.bodies?.find(
                        (b) => b.participantId === who || b.name === who,
                    )?.appearance ?? null,
            },
        },
    );
    useEffect(() => {
        if (!who || isPending) return;
        let gone = false;
        import('../apps/places/figure/portrait')
            .then((m) => m.portrait(appearance, who))
            .then((url) => {
                if (!gone) setSrc(url);
            })
            .catch((e) => console.warn('Portrait failed', e));
        return () => {
            gone = true;
        };
    }, [who, appearance, isPending]);
    return src;
}

interface Props extends Omit<ImgHTMLAttributes<HTMLImageElement>, 'src'> {
    who: string | null | undefined;
}

export default function Portrait({ who, alt = '', ...img }: Props) {
    return <img alt={alt} {...img} src={usePortrait(who)} />;
}
