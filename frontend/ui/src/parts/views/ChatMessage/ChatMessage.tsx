import type { ComponentPropsWithRef, CSSProperties, ReactNode } from 'react';
import type { Tone } from '../../../tone';

export type ChatMessageProps = Omit<
    ComponentPropsWithRef<'article'>,
    'title'
> & {
    sender: ReactNode;
    picture?: ReactNode;
    time?: ReactNode;
    actions?: ReactNode;
    details?: ReactNode;
    error?: ReactNode;
    layout?: 'row' | 'transcript';
    kind?: 'say' | 'emote';
    own?: boolean;
    tone?: Tone;
    live?: boolean;
    continued?: boolean;
    colour?: string;
    manner?: ChatMessageManner;
};

export type ChatMessageManner = 'plain' | 'quiet' | 'loud' | 'faint';

export function ChatMessage({
    sender,
    picture,
    time,
    actions,
    details,
    error,
    layout = 'row',
    kind = 'say',
    own = false,
    tone,
    live = false,
    continued = false,
    colour,
    manner = 'plain',
    className,
    style,
    children,
    ...rest
}: ChatMessageProps) {
    const who = colour ? (
        <span className="ui-chat-message-dot" aria-hidden="true" />
    ) : null;
    const shared = {
        className: className
            ? `ui-chat-message ${className}`
            : 'ui-chat-message',
        'data-layout': layout,
        'data-kind': kind,
        'data-own': own ? '' : undefined,
        'data-tone': tone,
        'data-live': live ? '' : undefined,
        'data-manner': manner === 'plain' ? undefined : manner,
        'aria-busy': live || undefined,
        style: colour
            ? ({ ...style, '--ui-chat-who': colour } as CSSProperties)
            : style,
    };
    if (kind === 'emote')
        return (
            <article {...shared} {...rest}>
                <p className="ui-chat-message-emote">
                    {who}
                    <span className="ui-chat-message-sender">{sender}</span>{' '}
                    {children}
                </p>
            </article>
        );
    return (
        <article
            {...shared}
            data-continued={continued ? '' : undefined}
            {...rest}
        >
            {!continued && (
                <header className="ui-chat-message-header">
                    {picture && (
                        <span className="ui-chat-message-picture">
                            {picture}
                        </span>
                    )}
                    {who}
                    <span className="ui-chat-message-sender">{sender}</span>
                    {time && (
                        <span className="ui-chat-message-time">{time}</span>
                    )}
                    {actions && (
                        <span className="ui-chat-message-actions">
                            {actions}
                        </span>
                    )}
                </header>
            )}
            {children != null && children !== false && (
                <div className="ui-chat-message-body">{children}</div>
            )}
            {error && <p className="ui-chat-message-error">{error}</p>}
            {details && (
                <div className="ui-chat-message-details">{details}</div>
            )}
        </article>
    );
}
