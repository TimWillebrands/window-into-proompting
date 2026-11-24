import type { HTMLAttributes, ReactNode } from 'react';
import type { Tone } from '../../../tone';
import { Icon, type IconName } from '../../icons';

export type StatusTextProps = HTMLAttributes<HTMLElement> & {
    tone?: Tone;
    icon?: ReactNode | false;
    variant?: 'line' | 'bar';
    actions?: ReactNode;
    as?: 'p' | 'span' | 'div';
};

const toneIcons: Record<Tone, IconName> = {
    neutral: 'info',
    go: 'check',
    caution: 'warning',
    danger: 'error',
};

export function StatusText({
    tone = 'neutral',
    icon,
    variant = 'line',
    actions,
    as,
    className,
    children,
    ...rest
}: StatusTextProps) {
    const Tag = as ?? (variant === 'bar' ? 'div' : 'p');
    const glyph = icon ?? <Icon name={toneIcons[tone]} />;
    return (
        <Tag
            className={
                className ? `ui-status-text ${className}` : 'ui-status-text'
            }
            data-tone={tone}
            data-variant={variant}
            {...rest}
        >
            {glyph && (
                <span className="ui-status-text-icon" aria-hidden="true">
                    {glyph}
                </span>
            )}
            <span className="ui-status-text-body">{children}</span>
            {actions && (
                <span className="ui-status-text-actions">{actions}</span>
            )}
        </Tag>
    );
}
