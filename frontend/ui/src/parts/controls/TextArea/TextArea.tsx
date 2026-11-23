import type { ComponentPropsWithRef } from 'react';
import { useFieldControl } from '../FieldRow/field-context';

export type TextAreaProps = ComponentPropsWithRef<'textarea'> & {
    invalid?: boolean;
    mono?: boolean;
};

export function TextArea({
    className,
    rows = 4,
    mono = false,
    ...props
}: TextAreaProps) {
    const rest = useFieldControl(props);
    return (
        <textarea
            rows={rows}
            className={className ? `ui-textarea ${className}` : 'ui-textarea'}
            data-mono={mono ? '' : undefined}
            {...rest}
        />
    );
}
