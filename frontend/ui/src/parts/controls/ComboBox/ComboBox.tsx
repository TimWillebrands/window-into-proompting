import type { ComponentPropsWithRef, ReactNode } from 'react';
import { useFieldControl } from '../FieldRow/field-context';

export type ComboBoxOption = {
    value: string;
    label: ReactNode;
    disabled?: boolean;
};

export type ComboBoxProps = Omit<
    ComponentPropsWithRef<'select'>,
    'multiple' | 'size'
> & {
    options?: ReadonlyArray<ComboBoxOption>;
    placeholder?: string;
    invalid?: boolean;
};

export function ComboBox({
    className,
    options,
    placeholder,
    children,
    ...props
}: ComboBoxProps) {
    const rest = useFieldControl(props);
    const showsPlaceholder =
        placeholder !== undefined &&
        rest.value === undefined &&
        rest.defaultValue === undefined;
    return (
        <span
            className={className ? `ui-combobox ${className}` : 'ui-combobox'}
        >
            <select
                className="ui-combobox-select"
                defaultValue={showsPlaceholder ? '' : undefined}
                {...rest}
            >
                {placeholder !== undefined && (
                    <option value="" disabled hidden>
                        {placeholder}
                    </option>
                )}
                {options?.map((option) => (
                    <option
                        key={option.value}
                        value={option.value}
                        disabled={option.disabled}
                    >
                        {option.label}
                    </option>
                ))}
                {children}
            </select>
            <span className="ui-combobox-button" aria-hidden="true" />
        </span>
    );
}
