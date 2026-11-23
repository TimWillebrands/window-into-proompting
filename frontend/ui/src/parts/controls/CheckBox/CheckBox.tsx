import {
    type ComponentPropsWithRef,
    type ReactNode,
    useImperativeHandle,
    useLayoutEffect,
    useRef,
} from 'react';

export type CheckBoxProps = Omit<ComponentPropsWithRef<'input'>, 'type'> & {
    indeterminate?: boolean;
    invalid?: boolean;
    children?: ReactNode;
};

export function CheckBox({
    indeterminate = false,
    invalid,
    className,
    style,
    children,
    ref,
    ...rest
}: CheckBoxProps) {
    const input = useRef<HTMLInputElement>(null);
    useImperativeHandle(ref, () => input.current as HTMLInputElement);
    useLayoutEffect(() => {
        if (input.current) input.current.indeterminate = indeterminate;
    }, [indeterminate]);
    return (
        <label
            className={className ? `ui-checkbox ${className}` : 'ui-checkbox'}
            style={style}
            data-disabled={rest.disabled ? '' : undefined}
        >
            <input
                ref={input}
                type="checkbox"
                className="ui-checkbox-input"
                aria-invalid={invalid || rest['aria-invalid']}
                {...rest}
            />
            <span className="ui-checkbox-box" aria-hidden="true" />
            {children && <span className="ui-checkbox-label">{children}</span>}
        </label>
    );
}
