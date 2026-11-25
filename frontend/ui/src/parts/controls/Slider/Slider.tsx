import type { ComponentPropsWithRef, CSSProperties, ReactNode } from 'react';
import { useFieldControl } from '../FieldRow/field-context';

export type SliderProps = Omit<ComponentPropsWithRef<'input'>, 'type'> & {
    ticks?: number;
    startLabel?: ReactNode;
    endLabel?: ReactNode;
    invalid?: boolean;
};

export function Slider({
    ticks,
    startLabel,
    endLabel,
    className,
    style,
    ...props
}: SliderProps) {
    const rest = useFieldControl(props);
    const tickCount = ticks && ticks > 1 ? ticks : undefined;
    return (
        <span
            className={className ? `ui-slider ${className}` : 'ui-slider'}
            style={
                tickCount
                    ? ({
                          ...style,
                          '--ui-slider-ticks': tickCount - 1,
                      } as CSSProperties)
                    : style
            }
            data-disabled={rest.disabled ? '' : undefined}
        >
            {startLabel && (
                <span className="ui-slider-end" aria-hidden="true">
                    {startLabel}
                </span>
            )}
            <span className="ui-slider-track">
                <input type="range" className="ui-slider-input" {...rest} />
                {tickCount && (
                    <span className="ui-slider-ticks" aria-hidden="true" />
                )}
            </span>
            {endLabel && (
                <span className="ui-slider-end" aria-hidden="true">
                    {endLabel}
                </span>
            )}
        </span>
    );
}
