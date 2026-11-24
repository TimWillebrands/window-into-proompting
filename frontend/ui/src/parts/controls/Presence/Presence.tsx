import type { HTMLAttributes } from 'react';

export const presenceStatuses = ['online', 'away', 'busy', 'offline'] as const;

export type PresenceStatus = (typeof presenceStatuses)[number];

export type PresenceProps = Omit<
    HTMLAttributes<HTMLSpanElement>,
    'children'
> & {
    status: PresenceStatus;
    shape?: 'dot' | 'buddy';
    size?: 'small' | 'medium';
    active?: boolean;
    label?: string;
};

export function Presence({
    status,
    shape = 'dot',
    size = 'medium',
    active = false,
    label,
    className,
    ...rest
}: PresenceProps) {
    return (
        <span
            className={className ? `ui-presence ${className}` : 'ui-presence'}
            data-status={status}
            data-shape={shape}
            data-size={size}
            data-active={active ? '' : undefined}
            {...(label
                ? { role: 'img', 'aria-label': label, title: label }
                : { 'aria-hidden': true })}
            {...rest}
        >
            {shape === 'buddy' && (
                <svg
                    viewBox="0 0 16 16"
                    className="ui-presence-buddy"
                    aria-hidden="true"
                >
                    <circle
                        className="ui-presence-buddy-fill"
                        cx="8"
                        cy="4.6"
                        r="3.4"
                    />
                    <path
                        className="ui-presence-buddy-fill"
                        d="M2.2 15.2c0-3.6 2.6-6 5.8-6s5.8 2.4 5.8 6z"
                    />
                    <ellipse
                        className="ui-presence-buddy-shine"
                        cx="6.9"
                        cy="3.5"
                        rx="1.3"
                        ry="0.9"
                    />
                    <ellipse
                        className="ui-presence-buddy-shine"
                        cx="6"
                        cy="11.4"
                        rx="2"
                        ry="0.9"
                    />
                </svg>
            )}
        </span>
    );
}
