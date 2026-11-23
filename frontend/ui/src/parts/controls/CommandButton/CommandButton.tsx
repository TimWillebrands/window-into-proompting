import type { ComponentPropsWithRef, ReactNode } from 'react';
import type { Tone } from '../tone';

export type CommandButtonSize = 'small' | 'medium' | 'large';

export type CommandButtonProps = ComponentPropsWithRef<'button'> & {
    tone?: Tone;
    isDefault?: boolean;
    size?: CommandButtonSize;
    pressed?: boolean;
    icon?: ReactNode;
};

export function CommandButton({
    tone = 'neutral',
    isDefault = false,
    size = 'medium',
    pressed,
    icon,
    type,
    className,
    children,
    ...rest
}: CommandButtonProps) {
    return (
        <button
            type={type ?? (isDefault ? 'submit' : 'button')}
            className={
                className
                    ? `ui-command-button ${className}`
                    : 'ui-command-button'
            }
            data-tone={tone}
            data-size={size}
            data-default={isDefault || undefined}
            aria-pressed={pressed}
            {...rest}
        >
            {icon && (
                <span className="ui-command-button-icon" aria-hidden="true">
                    {icon}
                </span>
            )}
            {children}
        </button>
    );
}
