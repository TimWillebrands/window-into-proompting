import {
    type ComponentPropsWithRef,
    createContext,
    type ReactNode,
    useContext,
    useState,
} from 'react';

type RadioGroupContextValue = {
    name: string;
    value: string | undefined;
    disabled: boolean;
    select: (value: string) => void;
};

const RadioGroupContext = createContext<RadioGroupContextValue | null>(null);

export type RadioButtonProps = Omit<ComponentPropsWithRef<'input'>, 'type'> & {
    value: string;
    invalid?: boolean;
    children?: ReactNode;
};

export function RadioButton({
    invalid,
    className,
    style,
    children,
    value,
    onChange,
    ...rest
}: RadioButtonProps) {
    const group = useContext(RadioGroupContext);
    const disabled = rest.disabled ?? group?.disabled;
    const grouped = group
        ? {
              name: group.name,
              checked: group.value === value,
              disabled,
          }
        : {};
    return (
        <label
            className={className ? `ui-radio ${className}` : 'ui-radio'}
            style={style}
            data-disabled={disabled ? '' : undefined}
        >
            <input
                type="radio"
                className="ui-radio-input"
                value={value}
                aria-invalid={invalid || rest['aria-invalid']}
                onChange={(event) => {
                    onChange?.(event);
                    if (event.target.checked) group?.select(value);
                }}
                {...rest}
                {...grouped}
            />
            <span className="ui-radio-dot" aria-hidden="true" />
            {children && <span className="ui-radio-label">{children}</span>}
        </label>
    );
}

export type RadioGroupProps = Omit<
    ComponentPropsWithRef<'div'>,
    'onChange' | 'defaultValue'
> & {
    name: string;
    value?: string;
    defaultValue?: string;
    onValueChange?: (value: string) => void;
    orientation?: 'vertical' | 'horizontal';
    disabled?: boolean;
};

export function RadioGroup({
    name,
    value,
    defaultValue,
    onValueChange,
    orientation = 'vertical',
    disabled = false,
    className,
    children,
    ...rest
}: RadioGroupProps) {
    const [own, setOwn] = useState(defaultValue);
    const current = value ?? own;
    return (
        <div
            role="radiogroup"
            aria-orientation={orientation}
            aria-disabled={disabled || undefined}
            className={
                className ? `ui-radio-group ${className}` : 'ui-radio-group'
            }
            data-orientation={orientation}
            {...rest}
        >
            <RadioGroupContext
                value={{
                    name,
                    value: current,
                    disabled,
                    select: (next) => {
                        setOwn(next);
                        onValueChange?.(next);
                    },
                }}
            >
                {children}
            </RadioGroupContext>
        </div>
    );
}
