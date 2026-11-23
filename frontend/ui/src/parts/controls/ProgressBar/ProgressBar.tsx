import type { ComponentPropsWithRef, CSSProperties } from 'react';
import type { Tone } from '../tone';

export type ProgressBarProps = Omit<
    ComponentPropsWithRef<'div'>,
    'children'
> & {
    value?: number;
    max?: number;
    marquee?: boolean;
    label?: string;
    valueText?: string;
    tone?: Tone;
};

export function ProgressBar({
    value = 0,
    max = 100,
    marquee = false,
    label,
    valueText,
    tone,
    className,
    style,
    ...rest
}: ProgressBarProps) {
    const ratio = max > 0 ? Math.min(1, Math.max(0, value / max)) : 0;
    return (
        <div
            role="progressbar"
            aria-label={label}
            aria-valuemin={marquee ? undefined : 0}
            aria-valuemax={marquee ? undefined : max}
            aria-valuenow={
                marquee ? undefined : Math.min(max, Math.max(0, value))
            }
            aria-valuetext={valueText}
            aria-busy={marquee || undefined}
            className={
                className ? `ui-progressbar ${className}` : 'ui-progressbar'
            }
            data-marquee={marquee ? '' : undefined}
            data-tone={tone}
            style={{ ...style, '--ui-progress': ratio } as CSSProperties}
            {...rest}
        >
            <span className="ui-progressbar-fill" />
        </div>
    );
}
