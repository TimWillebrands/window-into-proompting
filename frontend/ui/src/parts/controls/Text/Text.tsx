import type { HTMLAttributes, ReactNode } from 'react';
import type { Tone } from '../../../tone';

export type TextSize = 'chrome' | 'reading' | 'title' | 'display';

export type TextProps = HTMLAttributes<HTMLElement> & {
    as?: 'span' | 'p' | 'div' | 'h1' | 'h2' | 'h3' | 'h4' | 'em' | 'strong';
    size?: TextSize;
    tone?: Tone | 'muted';
    strong?: boolean;
    italic?: boolean;
    mono?: boolean;
    children?: ReactNode;
};

export function Text({
    as: Tag = 'span',
    size,
    tone,
    strong,
    italic,
    mono,
    className,
    children,
    ...rest
}: TextProps) {
    return (
        <Tag
            className={className ? `ui-text ${className}` : 'ui-text'}
            data-size={size}
            data-tone={tone}
            data-strong={strong ? '' : undefined}
            data-italic={italic ? '' : undefined}
            data-mono={mono ? '' : undefined}
            {...rest}
        >
            {children}
        </Tag>
    );
}
