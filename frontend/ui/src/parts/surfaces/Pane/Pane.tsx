import type { ComponentPropsWithRef } from 'react';
import type { Space } from '../Stack/Stack';

export type PaneProps = ComponentPropsWithRef<'div'> & {
    surface?: 'plain' | 'sunken';
    pad?: Space;
    text?: 'chrome' | 'reading';
    scroll?: 'y' | 'both';
    grow?: boolean;
};

export function Pane({
    surface = 'plain',
    pad,
    text,
    scroll = 'y',
    grow = true,
    className,
    ...rest
}: PaneProps) {
    return (
        <div
            className={className ? `ui-pane ${className}` : 'ui-pane'}
            data-surface={surface}
            data-pad={pad}
            data-text={text}
            data-scroll={scroll}
            data-grow={grow ? '' : undefined}
            {...rest}
        />
    );
}
