import type { ComponentPropsWithRef, ReactNode } from 'react';

export type ConsoleTone =
    | 'default'
    | 'muted'
    | 'neutral'
    | 'go'
    | 'caution'
    | 'danger';

export type ConsoleProps = ComponentPropsWithRef<'div'>;

export function Console({ className, children, ...rest }: ConsoleProps) {
    return (
        <div
            className={className ? `ui-console ${className}` : 'ui-console'}
            data-ui-scheme="noir"
            {...rest}
        >
            {children}
        </div>
    );
}

export type ConsoleLineProps = ComponentPropsWithRef<'div'> & {
    prompt?: ReactNode;
    tone?: ConsoleTone;
};

export function ConsoleLine({
    prompt,
    tone = 'default',
    className,
    children,
    ...rest
}: ConsoleLineProps) {
    return (
        <div
            className={
                className ? `ui-console-line ${className}` : 'ui-console-line'
            }
            data-tone={tone}
            {...rest}
        >
            {prompt !== undefined && (
                <span className="ui-console-prompt">{prompt} </span>
            )}
            {children}
        </div>
    );
}
