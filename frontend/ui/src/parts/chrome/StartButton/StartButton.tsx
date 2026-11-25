import type { ComponentPropsWithRef } from 'react';

export type StartButtonProps = ComponentPropsWithRef<'button'> & {
    open?: boolean;
};

export function StartButton({
    open = false,
    className,
    children = 'start',
    ...rest
}: StartButtonProps) {
    return (
        <button
            type="button"
            className={
                className ? `ui-start-button ${className}` : 'ui-start-button'
            }
            data-ui-start-button=""
            data-open={open ? '' : undefined}
            aria-haspopup="menu"
            aria-expanded={open}
            {...rest}
        >
            <span className="ui-start-button-label">{children}</span>
        </button>
    );
}
