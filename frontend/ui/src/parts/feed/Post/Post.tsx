import type { ComponentPropsWithRef, ReactNode } from 'react';

export type PostProps = Omit<ComponentPropsWithRef<'article'>, 'title'> & {
    picture?: ReactNode;
    title?: ReactNode;
    meta?: ReactNode;
    context?: ReactNode;
    actions?: ReactNode;
    replies?: ReactNode;
    variant?: 'card' | 'flat';
    size?: 'regular' | 'compact';
    unread?: boolean;
    current?: boolean;
};

export function Post({
    picture,
    title,
    meta,
    context,
    actions,
    replies,
    variant = 'card',
    size = 'regular',
    unread = false,
    current = false,
    className,
    children,
    ...rest
}: PostProps) {
    const hasHead = title !== undefined || meta !== undefined;
    return (
        <article
            className={className ? `ui-post ${className}` : 'ui-post'}
            data-variant={variant}
            data-size={size}
            data-unread={unread ? '' : undefined}
            data-current={current ? '' : undefined}
            data-has-picture={picture ? '' : undefined}
            {...rest}
        >
            {context && <div className="ui-post-context">{context}</div>}
            <div className="ui-post-main">
                {picture && (
                    <span className="ui-post-picture" aria-hidden="true">
                        {picture}
                    </span>
                )}
                <div className="ui-post-content">
                    {hasHead && (
                        <header className="ui-post-head">
                            {title !== undefined && (
                                <span className="ui-post-title">{title}</span>
                            )}
                            {meta !== undefined && (
                                <span className="ui-post-meta">{meta}</span>
                            )}
                        </header>
                    )}
                    {children !== undefined && children !== null && (
                        <div className="ui-post-body">{children}</div>
                    )}
                    {actions && (
                        <footer className="ui-post-actions">{actions}</footer>
                    )}
                </div>
            </div>
            {replies && <div className="ui-post-replies">{replies}</div>}
        </article>
    );
}

export type PostMentionProps = ComponentPropsWithRef<'strong'>;

export function PostMention({ className, ...rest }: PostMentionProps) {
    return (
        <strong
            className={
                className ? `ui-post-mention ${className}` : 'ui-post-mention'
            }
            {...rest}
        />
    );
}
