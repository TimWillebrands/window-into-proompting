import type { ComponentPropsWithRef, ReactNode } from 'react';

export type DesktopIconProps = Omit<
    ComponentPropsWithRef<'button'>,
    'children'
> & {
    icon: ReactNode;
    label: ReactNode;
    selected?: boolean;
};

export function DesktopIcon({
    icon,
    label,
    selected = false,
    className,
    ...rest
}: DesktopIconProps) {
    return (
        <button
            type="button"
            className={
                className ? `ui-desktop-icon ${className}` : 'ui-desktop-icon'
            }
            data-selected={selected ? '' : undefined}
            {...rest}
        >
            <span className="ui-desktop-icon-image" aria-hidden="true">
                {icon}
            </span>
            <span className="ui-desktop-icon-label">{label}</span>
        </button>
    );
}
