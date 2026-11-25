import type { ComponentPropsWithRef, ReactNode } from 'react';

export type LogonScreenProps = Omit<ComponentPropsWithRef<'div'>, 'title'> & {
    title: ReactNode;
    hint?: ReactNode;
    footerStart?: ReactNode;
    footerEnd?: ReactNode;
};

export function LogonScreen({
    title,
    hint,
    footerStart,
    footerEnd,
    className,
    children,
    ...rest
}: LogonScreenProps) {
    return (
        <div
            className={className ? `ui-logon ${className}` : 'ui-logon'}
            {...rest}
        >
            <div className="ui-logon-band" data-edge="top" />
            <div className="ui-logon-face">
                <div className="ui-logon-welcome">
                    <h1 className="ui-logon-title">{title}</h1>
                    {hint && <p className="ui-logon-hint">{hint}</p>}
                </div>
                <div className="ui-logon-divider" aria-hidden="true" />
                <div className="ui-logon-tiles">{children}</div>
            </div>
            <div className="ui-logon-band" data-edge="bottom">
                <div className="ui-logon-footer-start">{footerStart}</div>
                <div className="ui-logon-footer-end">{footerEnd}</div>
            </div>
        </div>
    );
}

export type LogonTileProps = Omit<
    ComponentPropsWithRef<'button'>,
    'children'
> & {
    picture: ReactNode;
    name: ReactNode;
    subtitle?: ReactNode;
    selected?: boolean;
    dimmed?: boolean;
    footer?: ReactNode;
    children?: ReactNode;
};

export function LogonTile({
    picture,
    name,
    subtitle,
    selected = false,
    dimmed = false,
    footer,
    className,
    children,
    ...rest
}: LogonTileProps) {
    return (
        <div
            className={
                className ? `ui-logon-tile ${className}` : 'ui-logon-tile'
            }
            data-selected={selected ? '' : undefined}
            data-dimmed={dimmed ? '' : undefined}
        >
            <button
                type="button"
                className="ui-logon-tile-button"
                aria-pressed={selected}
                {...rest}
            >
                <span className="ui-logon-picture" aria-hidden="true">
                    {picture}
                </span>
                <span className="ui-logon-tile-text">
                    <span className="ui-logon-name">{name}</span>
                    {subtitle && (
                        <span className="ui-logon-subtitle">{subtitle}</span>
                    )}
                </span>
            </button>
            {selected && children != null && children !== false && (
                <div className="ui-logon-tile-more">{children}</div>
            )}
            {footer != null && footer !== false && (
                <div className="ui-logon-tile-more">{footer}</div>
            )}
        </div>
    );
}

export type LogonActionProps = ComponentPropsWithRef<'button'> & {
    icon?: ReactNode;
};

export function LogonAction({
    icon,
    className,
    children,
    ...rest
}: LogonActionProps) {
    return (
        <button
            type="button"
            className={
                className ? `ui-logon-action ${className}` : 'ui-logon-action'
            }
            {...rest}
        >
            {icon && (
                <span className="ui-logon-action-icon" aria-hidden="true">
                    {icon}
                </span>
            )}
            <span>{children}</span>
        </button>
    );
}
