import type { ReactNode } from 'react';

export type GlyphName =
    | 'minimise'
    | 'maximise'
    | 'restore'
    | 'close'
    | 'submenu'
    | 'check'
    | 'power'
    | 'log-off';

const paths: Record<GlyphName, ReactNode> = {
    minimise: <rect x="2" y="8" width="6" height="2.5" />,
    maximise: <path fillRule="evenodd" d="M1 1h10v10H1zM2 3.5V10h8V3.5z" />,
    restore: (
        <path
            fillRule="evenodd"
            d="M1 4h7v7H1zM2 6.2V10h5V6.2zM4 1h7v7H9V7h1V3.2H5V4H4z"
        />
    ),
    close: (
        <path
            d="M2.5 2.5l7 7M9.5 2.5l-7 7"
            fill="none"
            stroke="currentColor"
            strokeWidth="2"
            strokeLinecap="round"
        />
    ),
    submenu: <path d="M4 2l4 4-4 4z" />,
    check: (
        <path
            d="M2.5 6.5l2.5 2.5 4.5-6"
            fill="none"
            stroke="currentColor"
            strokeWidth="1.8"
            strokeLinecap="round"
            strokeLinejoin="round"
        />
    ),
    power: (
        <g
            fill="none"
            stroke="currentColor"
            strokeWidth="1.6"
            strokeLinecap="round"
        >
            <path d="M3.6 3.4a4 4 0 1 0 4.8 0" />
            <path d="M6 1.5v4" />
        </g>
    ),
    'log-off': (
        <g
            fill="none"
            stroke="currentColor"
            strokeWidth="1.5"
            strokeLinecap="round"
            strokeLinejoin="round"
        >
            <path d="M5 2H2.5v8H5" />
            <path d="M5 6h5.5M8.5 4l2 2-2 2" />
        </g>
    ),
};

export function Glyph({ name }: { name: GlyphName }) {
    return (
        <svg
            className="ui-glyph"
            viewBox="0 0 12 12"
            width="12"
            height="12"
            fill="currentColor"
            aria-hidden="true"
            focusable="false"
        >
            {paths[name]}
        </svg>
    );
}
