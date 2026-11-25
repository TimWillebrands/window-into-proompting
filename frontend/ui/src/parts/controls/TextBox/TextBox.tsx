import type { ComponentPropsWithRef } from 'react';
import { useFieldControl } from '../FieldRow/field-context';

export type TextBoxProps = ComponentPropsWithRef<'input'> & {
    invalid?: boolean;
};

export function TextBox({ className, type = 'text', ...props }: TextBoxProps) {
    const rest = useFieldControl(props);
    return (
        <input
            type={type}
            className={className ? `ui-textbox ${className}` : 'ui-textbox'}
            {...rest}
        />
    );
}
