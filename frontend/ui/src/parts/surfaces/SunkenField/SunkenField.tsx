import type { ComponentPropsWithRef } from 'react';

export type SunkenFieldProps = ComponentPropsWithRef<'div'> & {
    text?: 'chrome' | 'reading';
};

export function SunkenField({
    text = 'chrome',
    className,
    ...rest
}: SunkenFieldProps) {
    return (
        <div
            className={
                className ? `ui-sunken-field ${className}` : 'ui-sunken-field'
            }
            data-text={text}
            {...rest}
        />
    );
}
