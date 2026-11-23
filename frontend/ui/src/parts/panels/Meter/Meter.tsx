import type { CSSProperties, HTMLAttributes } from 'react';
import type { Tone } from '../../controls/tone';

export type MeterProps = Omit<HTMLAttributes<HTMLSpanElement>, 'children'> & {
    value: number;
    diverging?: boolean;
    tone?: Tone;
    label?: string;
    valueText?: string;
};

const clamp = (n: number, lo: number, hi: number) =>
    Math.min(hi, Math.max(lo, Number.isFinite(n) ? n : 0));

export function Meter({
    value,
    diverging = false,
    tone = 'neutral',
    label,
    valueText,
    className,
    style,
    ...rest
}: MeterProps) {
    const min = diverging ? -1 : 0;
    const v = clamp(value, min, 1);
    const start = diverging ? Math.min(0.5, 0.5 + v / 2) : 0;
    const end = diverging ? Math.max(0.5, 0.5 + v / 2) : v;
    const sign = !diverging || v >= 0 ? 'positive' : 'negative';
    return (
        // biome-ignore lint/a11y/useSemanticElements: a native meter can't be styled to the Luna track in every engine
        <span
            role="meter"
            aria-label={label}
            aria-valuemin={min}
            aria-valuemax={1}
            aria-valuenow={v}
            aria-valuetext={valueText}
            className={className ? `ui-meter ${className}` : 'ui-meter'}
            data-diverging={diverging ? '' : undefined}
            data-sign={sign}
            data-tone={diverging ? undefined : tone}
            style={
                {
                    ...style,
                    '--ui-meter-start': start,
                    '--ui-meter-end': end,
                } as CSSProperties
            }
            {...rest}
        >
            <span className="ui-meter-fill" />
        </span>
    );
}
