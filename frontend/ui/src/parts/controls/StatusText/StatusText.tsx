import type { HTMLAttributes, ReactNode } from 'react';
import { Icon, type IconName } from '../../icons';
import type { Tone } from '../tone';

export type StatusTextProps = HTMLAttributes<HTMLElement> & {
    tone?: Tone;
    icon?: ReactNode | false;
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
    as: Tag = 'p',
    className,
    children,
    ...rest
}: StatusTextProps) {
    const glyph = icon ?? <Icon name={toneIcons[tone]} />;
    return (
        <Tag
            className={
                className ? `ui-status-text ${className}` : 'ui-status-text'
            }
            data-tone={tone}
            {...rest}
        >
            {glyph && (
                <span className="ui-status-text-icon" aria-hidden="true">
                    {glyph}
                </span>
            )}
            <span className="ui-status-text-body">{children}</span>
        </Tag>
    );
}
