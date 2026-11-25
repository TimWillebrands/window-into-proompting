import { useId } from 'react';
import type { Look } from './appearance';

export default function FaceGlyph({
    look,
    size = 16,
    eyes = false,
}: {
    look: Pick<Look, 'skin' | 'hair' | 'top'>;
    size?: number;
    eyes?: boolean;
}) {
    const clip = `face-${useId().replace(/[^a-zA-Z0-9-]/g, '')}`;
    return (
        <svg
            width={size}
            height={size}
            viewBox="0 0 20 20"
            aria-hidden="true"
            focusable="false"
        >
            <circle cx="10" cy="10" r="10" fill={look.top} />
            <clipPath id={clip}>
                <circle cx="10" cy="10" r="7.5" />
            </clipPath>
            <g clipPath={`url(#${clip})`}>
                <rect width="20" height="20" fill={look.skin} />
                <rect width="20" height={eyes ? 8.5 : 7} fill={look.hair} />
                <rect
                    y="15.5"
                    width="20"
                    height="5"
                    fill="#000"
                    opacity="0.15"
                />
            </g>
            {eyes ? (
                <>
                    <circle cx="7.6" cy="11.6" r="1.1" fill="#2a2030" />
                    <circle cx="12.4" cy="11.6" r="1.1" fill="#2a2030" />
                </>
            ) : null}
        </svg>
    );
}
