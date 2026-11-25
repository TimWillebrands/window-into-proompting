import type { ComponentPropsWithRef, CSSProperties } from 'react';
import type { Space } from '../Stack/Stack';

export type TilesProps = ComponentPropsWithRef<'div'> & {
    as?: 'div' | 'ul';
    minWidth?: string;
    gap?: Space;
};

export function Tiles({
    as = 'div',
    minWidth,
    gap = 'sm',
    className,
    style,
    ...rest
}: TilesProps) {
    const Element = as as 'div';
    return (
        <Element
            className={className ? `ui-tiles ${className}` : 'ui-tiles'}
            data-gap={gap}
            style={
                minWidth
                    ? ({ '--ui-tile-min': minWidth, ...style } as CSSProperties)
                    : style
            }
            {...rest}
        />
    );
}
