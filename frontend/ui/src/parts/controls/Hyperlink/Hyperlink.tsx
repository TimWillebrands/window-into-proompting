import type { ComponentPropsWithRef } from 'react';

export type HyperlinkProps =
    | (ComponentPropsWithRef<'a'> & { href: string })
    | (ComponentPropsWithRef<'button'> & { href?: undefined });

export function Hyperlink(props: HyperlinkProps) {
    if (props.href !== undefined) {
        const { className, ...rest } = props;
        return (
            <a
                className={
                    className ? `ui-hyperlink ${className}` : 'ui-hyperlink'
                }
                {...rest}
            />
        );
    }
    const { className, type = 'button', ...rest } = props;
    return (
        <button
            type={type}
            className={className ? `ui-hyperlink ${className}` : 'ui-hyperlink'}
            {...rest}
        />
    );
}
