import type { ComponentPropsWithRef } from 'react';

export type RichTextProps = ComponentPropsWithRef<'div'>;

export function RichText({ className, ...rest }: RichTextProps) {
    return (
        <div
            className={className ? `ui-richtext ${className}` : 'ui-richtext'}
            {...rest}
        />
    );
}
