import {
    UserPicture,
    type UserPictureProps,
    type UserPictureSize,
} from '@partytown/ui';
import { type ImgHTMLAttributes, useEffect, useState } from 'react';
import { useGetWorldsPartyIdPlaces } from '#api/party-zone';
import { WORLD_ID } from '../lib/world';

const STALE_MS = 5 * 60_000;

export function usePortrait(
    who: string | null | undefined,
): string | undefined {
    const [src, setSrc] = useState<string>();
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

type FramedProps = Omit<UserPictureProps, 'src' | 'size'> & {
    who: string | null | undefined;
    size: UserPictureSize;
};

type BareProps = Omit<ImgHTMLAttributes<HTMLImageElement>, 'src'> & {
    who: string | null | undefined;
    size?: undefined;
};

export default function Portrait(props: FramedProps | BareProps) {
    const src = usePortrait(props.who);
    if (props.size !== undefined) {
        const { who: _who, ...picture } = props;
        return <UserPicture {...picture} src={src} />;
    }
    const { who: _who, alt = '', ...img } = props;
    return <img alt={alt} {...img} src={src} />;
}
