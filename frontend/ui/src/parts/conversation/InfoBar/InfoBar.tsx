import type { ComponentPropsWithRef, ReactNode } from 'react';
import type { Tone } from '../../controls/tone';

export type InfoBarProps = ComponentPropsWithRef<'div'> & {
    icon?: ReactNode;
    actions?: ReactNode;
    tone?: Tone;
};

export function InfoBar({
    icon,
    actions,
    tone = 'caution',
    className,
    children,
    ...rest
}: InfoBarProps) {
    return (
        <div
            className={className ? `ui-infobar ${className}` : 'ui-infobar'}
            data-tone={tone}
            {...rest}
        >
            {icon && (
                <span className="ui-infobar-icon" aria-hidden="true">
                    {icon}
                </span>
            )}
            <div className="ui-infobar-text">{children}</div>
            {actions && <div className="ui-infobar-actions">{actions}</div>}
        </div>
    );
}
