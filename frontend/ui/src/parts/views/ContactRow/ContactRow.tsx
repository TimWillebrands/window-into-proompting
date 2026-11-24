import type { HTMLAttributes, MouseEventHandler, ReactNode } from 'react';

export type ContactRowProps = Omit<
    HTMLAttributes<HTMLElement>,
    'onClick' | 'children'
> & {
    name: ReactNode;
    picture?: ReactNode;
    meta?: ReactNode;
    details?: ReactNode;
    selected?: boolean;
    dimmed?: boolean;
    unread?: boolean;
    disabled?: boolean;
    onClick?: MouseEventHandler<HTMLButtonElement>;
};

export function ContactRow({
    name,
    picture,
    meta,
    details,
    selected = false,
    dimmed = false,
    unread = false,
    disabled,
    onClick,
    className,
    ...rest
}: ContactRowProps) {
    const shared = {
        className: className ? `ui-contact-row ${className}` : 'ui-contact-row',
        'data-selected': selected ? '' : undefined,
        'data-dimmed': dimmed ? '' : undefined,
        'data-unread': unread ? '' : undefined,
        'data-has-details': details ? '' : undefined,
    };
    const inner = (
        <>
            {picture && (
                <span className="ui-contact-row-picture">{picture}</span>
            )}
            <span className="ui-contact-row-text">
                <span className="ui-contact-row-line">
                    <span className="ui-contact-row-name">{name}</span>
                    {meta && (
                        <span className="ui-contact-row-meta">{meta}</span>
                    )}
                </span>
                {details && (
                    <span className="ui-contact-row-details">{details}</span>
                )}
            </span>
        </>
    );
    if (onClick)
        return (
            <button
                type="button"
                disabled={disabled}
                onClick={onClick}
                {...shared}
                {...rest}
            >
                {inner}
            </button>
        );
    return (
        <div {...shared} {...rest}>
            {inner}
        </div>
    );
}
