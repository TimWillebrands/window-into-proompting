import {
    type ComponentPropsWithRef,
    type CSSProperties,
    type ReactNode,
    useId,
} from 'react';
import { FieldContext } from './field-context';

export type FieldRowProps = Omit<ComponentPropsWithRef<'div'>, 'children'> & {
    label: ReactNode;
    layout?: 'horizontal' | 'stacked';
    labelWidth?: 'auto' | 'compact' | (string & {});
    hint?: ReactNode;
    error?: ReactNode;
    required?: boolean;
    controlId?: string;
    children: ReactNode;
};

export function FieldRow({
    label,
    layout = 'horizontal',
    labelWidth,
    hint,
    error,
    required = false,
    controlId,
    className,
    style,
    children,
    ...rest
}: FieldRowProps) {
    const base = useId();
    const id = controlId ?? `${base}-control`;
    const hintId = hint ? `${base}-hint` : undefined;
    const errorId = error ? `${base}-error` : undefined;
    const describedBy =
        [errorId, hintId].filter(Boolean).join(' ') || undefined;
    const named = labelWidth === 'auto' || labelWidth === 'compact';
    const width =
        labelWidth && !named
            ? ({
                  ...style,
                  '--ui-field-label-width': labelWidth,
              } as CSSProperties)
            : style;
    return (
        <div
            className={className ? `ui-field-row ${className}` : 'ui-field-row'}
            data-layout={layout}
            data-label-width={named ? labelWidth : undefined}
            style={width}
            data-invalid={error ? '' : undefined}
            {...rest}
        >
            <label className="ui-field-row-label" htmlFor={id}>
                {label}
                {required && (
                    <span className="ui-field-row-required" aria-hidden="true">
                        *
                    </span>
                )}
            </label>
            <div className="ui-field-row-control">
                <FieldContext
                    value={{
                        id,
                        describedBy,
                        invalid: Boolean(error),
                        required,
                    }}
                >
                    {children}
                </FieldContext>
                {error && (
                    <span id={errorId} className="ui-field-row-error">
                        {error}
                    </span>
                )}
                {hint && (
                    <span id={hintId} className="ui-field-row-hint">
                        {hint}
                    </span>
                )}
            </div>
        </div>
    );
}
