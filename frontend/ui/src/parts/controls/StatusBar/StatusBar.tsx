import type { ComponentPropsWithRef, ReactNode } from 'react';

export type StatusBarProps = ComponentPropsWithRef<'div'> & {
    grip?: boolean;
};

export function StatusBar({
    grip = false,
    className,
    children,
    ...rest
}: StatusBarProps) {
    return (
        <div
            className={className ? `ui-statusbar ${className}` : 'ui-statusbar'}
            {...rest}
        >
            {children}
            {grip && <span className="ui-statusbar-grip" aria-hidden="true" />}
        </div>
    );
}

export type StatusBarFieldProps = ComponentPropsWithRef<'div'> & {
    grow?: boolean;
    icon?: ReactNode;
};

export function StatusBarField({
    grow = false,
    icon,
    className,
    children,
    ...rest
}: StatusBarFieldProps) {
    return (
        <div
            className={
                className
                    ? `ui-statusbar-field ${className}`
                    : 'ui-statusbar-field'
            }
            data-grow={grow ? '' : undefined}
            {...rest}
        >
            {icon && (
                <span className="ui-statusbar-icon" aria-hidden="true">
                    {icon}
                </span>
            )}
            {children}
        </div>
    );
}
