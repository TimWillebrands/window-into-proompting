import type { HTMLAttributes, ReactNode } from 'react';

export type PictureStackProps = Omit<
    HTMLAttributes<HTMLSpanElement>,
    'children'
> & {
    pictures: ReadonlyArray<{ key: string; picture: ReactNode }>;
    max?: number;
    size?: 16 | 24 | 32;
    label: string;
};

export function PictureStack({
    pictures,
    max = 3,
    size = 24,
    label,
    className,
    ...rest
}: PictureStackProps) {
    const shown = pictures.length > max ? pictures.slice(0, max - 1) : pictures;
    const more = pictures.length - shown.length;
    return (
        <span
            className={
                className ? `ui-picture-stack ${className}` : 'ui-picture-stack'
            }
            data-size={size}
            role="img"
            aria-label={label}
            {...rest}
        >
            {shown.map(({ key, picture }) => (
                <span key={key} className="ui-picture-stack-item">
                    {picture}
                </span>
            ))}
            {more > 0 && (
                <span className="ui-picture-stack-more" aria-hidden="true">
                    +{more}
                </span>
            )}
        </span>
    );
}
