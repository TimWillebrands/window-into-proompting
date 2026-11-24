import { type ComponentPropsWithRef, type ReactNode, useId } from 'react';

export type CategoryViewProps = Omit<
    ComponentPropsWithRef<'section'>,
    'title'
> & {
    title?: ReactNode;
};

export function CategoryView({
    title,
    className,
    children,
    ...rest
}: CategoryViewProps) {
    const titleId = useId();
    return (
        <section
            aria-labelledby={title ? titleId : undefined}
            className={
                className ? `ui-category-view ${className}` : 'ui-category-view'
            }
            {...rest}
        >
            {title && (
                <h2 id={titleId} className="ui-category-view-title">
                    {title}
                </h2>
            )}
            <ul className="ui-category-view-items">{children}</ul>
        </section>
    );
}

export type CategoryItemProps = Omit<
    ComponentPropsWithRef<'button'>,
    'title'
> & {
    icon: ReactNode;
    title: ReactNode;
    description?: ReactNode;
};

export function CategoryItem({
    icon,
    title,
    description,
    className,
    type = 'button',
    ...rest
}: CategoryItemProps) {
    return (
        <li className="ui-category-item-slot">
            <button
                type={type}
                className={
                    className
                        ? `ui-category-item ${className}`
                        : 'ui-category-item'
                }
                {...rest}
            >
                <span className="ui-category-item-icon" aria-hidden="true">
                    {icon}
                </span>
                <span className="ui-category-item-text">
                    <span className="ui-category-item-title">{title}</span>
                    {description && (
                        <span className="ui-category-item-description">
                            {description}
                        </span>
                    )}
                </span>
            </button>
        </li>
    );
}
