import { createContext, useContext } from 'react';

export type FieldContextValue = {
    id: string;
    describedBy?: string;
    invalid: boolean;
    required: boolean;
};

export const FieldContext = createContext<FieldContextValue | null>(null);

export type FieldControlProps = {
    id?: string;
    'aria-describedby'?: string;
    'aria-invalid'?: boolean | 'true' | 'false' | 'grammar' | 'spelling';
    required?: boolean;
    invalid?: boolean;
};

export function useFieldControl<P extends FieldControlProps>(
    props: P,
): Omit<P, 'invalid'> {
    const field = useContext(FieldContext);
    const { invalid, ...rest } = props;
    const isInvalid =
        invalid ||
        (props['aria-invalid'] !== undefined &&
            props['aria-invalid'] !== false &&
            props['aria-invalid'] !== 'false') ||
        field?.invalid;
    const describedBy =
        [field?.describedBy, props['aria-describedby']]
            .filter(Boolean)
            .join(' ') || undefined;
    return {
        ...rest,
        id: props.id ?? field?.id,
        'aria-describedby': describedBy,
        'aria-invalid': isInvalid || undefined,
        required: props.required ?? (field?.required || undefined),
    };
}
