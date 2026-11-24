import type { CSSProperties, HTMLAttributes } from 'react';

export type SwatchProps = Omit<HTMLAttributes<HTMLSpanElement>, 'color'> & {
    colour?: string;
    ring?: string;
    shape?: 'square' | 'dot';
    size?: 'small' | 'medium' | 'large';
    label?: string;
};

export function Swatch({
    colour,
    ring,
    shape = 'square',
    size = 'medium',
    label,
    className,
    style,
    ...rest
}: SwatchProps) {
    const shared = {
        className: className ? `ui-swatch ${className}` : 'ui-swatch',
        'data-shape': shape,
        'data-size': size,
        'data-empty': colour ? undefined : '',
        'data-ring': ring ? '' : undefined,
        style: {
            ...style,
            '--ui-swatch': colour,
            '--ui-swatch-ring': ring,
        } as CSSProperties,
    };
    if (label)
        return <span role="img" aria-label={label} {...shared} {...rest} />;
    return <span aria-hidden="true" {...shared} {...rest} />;
}
