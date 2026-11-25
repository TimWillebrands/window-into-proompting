import type { CSSProperties, HTMLAttributes, ReactNode } from 'react';

export type ShareBarSegment = {
    key: string;
    share: number;
    colour: string;
    label?: ReactNode;
    caption?: ReactNode;
    title?: string;
    state?: 'normal' | 'dim' | 'off';
};

export type ShareBarProps = Omit<HTMLAttributes<HTMLDivElement>, 'children'> & {
    segments: ReadonlyArray<ShareBarSegment>;
    label: string;
};

const shareStyle = (segment: ShareBarSegment) =>
    ({
        '--ui-share': Math.max(0, segment.share),
        '--ui-share-colour': segment.colour,
    }) as CSSProperties;

export function ShareBar({
    segments,
    label,
    className,
    ...rest
}: ShareBarProps) {
    const captioned = segments.some((segment) => segment.caption != null);
    return (
        <div
            className={className ? `ui-share-bar ${className}` : 'ui-share-bar'}
            {...rest}
        >
            <div role="img" aria-label={label} className="ui-share-bar-track">
                {segments.map((segment) => (
                    <span
                        key={segment.key}
                        className="ui-share-bar-segment"
                        data-state={segment.state ?? 'normal'}
                        title={segment.title}
                        style={shareStyle(segment)}
                    >
                        {segment.label}
                    </span>
                ))}
            </div>
            {captioned && (
                <div className="ui-share-bar-captions" aria-hidden="true">
                    {segments.map((segment) => (
                        <span
                            key={segment.key}
                            className="ui-share-bar-caption"
                            style={shareStyle(segment)}
                        >
                            {segment.caption}
                        </span>
                    ))}
                </div>
            )}
        </div>
    );
}
