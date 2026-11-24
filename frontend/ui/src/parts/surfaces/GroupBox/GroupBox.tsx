import type { ComponentPropsWithRef, ReactNode } from 'react';

export type GroupBoxProps = Omit<ComponentPropsWithRef<'fieldset'>, 'title'> & {
    title: ReactNode;
    icon?: ReactNode;
    current?: boolean;
};

export function GroupBox({
    title,
    icon,
    current = false,
    className,
    children,
    ...rest
}: GroupBoxProps) {
    return (
        <fieldset
            className={className ? `ui-groupbox ${className}` : 'ui-groupbox'}
            data-current={current ? '' : undefined}
            aria-current={current || undefined}
            {...rest}
        >
            <legend className="ui-groupbox-title">
                {icon && (
                    <span className="ui-groupbox-icon" aria-hidden="true">
                        {icon}
                    </span>
                )}
                {title}
            </legend>
            {children}
        </fieldset>
    );
}
