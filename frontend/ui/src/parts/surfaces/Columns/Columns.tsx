import {
    Children,
    type ComponentPropsWithRef,
    type CSSProperties,
    cloneElement,
    isValidElement,
    type ReactElement,
    type ReactNode,
} from 'react';
import type { Space } from '../Stack/Stack';

export type ColumnsBreakpoint = 360 | 520 | 640 | 820;

export type ColumnsProps = ComponentPropsWithRef<'div'> & {
    gap?: Space;
    pad?: Space;
    narrowBelow?: ColumnsBreakpoint;
    narrow?: 'stack' | 'one';
    show?: number;
    switcher?: ReactNode;
    grow?: boolean;
};

export type ColumnProps = ComponentPropsWithRef<'div'> & {
    as?: 'div' | 'section';
    weight?: number;
    minWidth?: string;
    gap?: Space;
};

function ColumnsRoot({
    gap = 'md',
    pad,
    narrowBelow = 520,
    narrow = 'stack',
    show = 0,
    switcher,
    grow = false,
    className,
    children,
    ...rest
}: ColumnsProps) {
    const columns = Children.toArray(children).filter(
        (child): child is ReactElement<ColumnProps> => isValidElement(child),
    );
    return (
        <div
            className={className ? `ui-columns ${className}` : 'ui-columns'}
            data-grow={grow ? '' : undefined}
            data-pad={pad}
            {...rest}
        >
            {switcher && narrow === 'one' && (
                <div
                    className="ui-columns-switcher"
                    data-narrow-below={narrowBelow}
                >
                    {switcher}
                </div>
            )}
            <div
                className="ui-columns-track"
                data-gap={gap}
                data-narrow={narrow}
                data-narrow-below={narrowBelow}
            >
                {columns.map((column, index) =>
                    cloneElement(column, {
                        'data-shown': index === show ? '' : undefined,
                    } as Partial<ColumnProps>),
                )}
            </div>
        </div>
    );
}

function Column({
    as = 'div',
    weight = 1,
    minWidth,
    gap,
    className,
    style,
    ...rest
}: ColumnProps) {
    const Element = as as 'div';
    return (
        <Element
            className={className ? `ui-column ${className}` : 'ui-column'}
            data-gap={gap}
            style={
                {
                    '--ui-column-weight': weight,
                    '--ui-column-min': minWidth,
                    ...style,
                } as CSSProperties
            }
            {...rest}
        />
    );
}

export const Columns = Object.assign(ColumnsRoot, { Column });
