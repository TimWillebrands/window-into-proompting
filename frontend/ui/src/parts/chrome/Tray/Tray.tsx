import type { ComponentPropsWithRef, ReactNode } from 'react';
import type { Tone } from '../../../tone';
import { moveFocus } from '../Taskbar/Taskbar';

export type TrayProps = ComponentPropsWithRef<'div'> & {
    clock?: ReactNode;
};

export function Tray({ clock, className, children, ...rest }: TrayProps) {
    return (
        <div
            role="toolbar"
            aria-label="Notification area"
            onKeyDown={moveFocus}
            className={className ? `ui-tray ${className}` : 'ui-tray'}
            {...rest}
        >
            {children}
            {clock !== undefined && (
                <span className="ui-tray-clock">{clock}</span>
            )}
        </div>
    );
}

export type TrayButtonProps = Omit<
    ComponentPropsWithRef<'button'>,
    'children'
> & {
    icon: ReactNode;
    label: string;
    count?: number;
    countTone?: Tone;
    pressed?: boolean;
};

const shownCount = (count: number) => (count > 99 ? '99+' : String(count));

export function TrayButton({
    icon,
    label,
    count = 0,
    countTone = 'danger',
    pressed,
    className,
    ...rest
}: TrayButtonProps) {
    const spoken = count > 0 ? `${label}, ${shownCount(count)} new` : label;
    return (
        <button
            type="button"
            className={
                className ? `ui-tray-button ${className}` : 'ui-tray-button'
            }
            aria-label={spoken}
            title={spoken}
            aria-pressed={pressed}
            {...rest}
        >
            <span className="ui-tray-button-icon" aria-hidden="true">
                {icon}
            </span>
            {count > 0 && (
                <span
                    className="ui-tray-count"
                    data-tone={countTone}
                    aria-hidden="true"
                >
                    {shownCount(count)}
                </span>
            )}
        </button>
    );
}
