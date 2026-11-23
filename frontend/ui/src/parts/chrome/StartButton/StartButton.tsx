import type { ComponentPropsWithRef, ReactNode } from 'react';
import { Icon } from '../../icons';

export type StartButtonProps = ComponentPropsWithRef<'button'> & {
    open?: boolean;
    icon?: ReactNode;
};

export function StartButton({
    open = false,
    icon = <Icon name="start" />,
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
            {icon && (
                <span className="ui-start-button-icon" aria-hidden="true">
                    {icon}
                </span>
            )}
            <span className="ui-start-button-label">{children}</span>
        </button>
    );
}
