import type { ComponentPropsWithRef, ReactNode } from 'react';

export type PageHeaderProps = Omit<ComponentPropsWithRef<'header'>, 'title'> & {
    title: ReactNode;
    icon?: ReactNode;
    description?: ReactNode;
    aside?: ReactNode;
};

export function PageHeader({
    title,
    icon,
    description,
    aside,
    className,
    ...rest
}: PageHeaderProps) {
    return (
        <header
            className={
                className ? `ui-page-header ${className}` : 'ui-page-header'
            }
            {...rest}
        >
            {icon && (
                <span className="ui-page-header-icon" aria-hidden="true">
                    {icon}
                </span>
            )}
            <div className="ui-page-header-text">
                <h2 className="ui-page-header-title">{title}</h2>
                {description && (
                    <p className="ui-page-header-description">{description}</p>
                )}
            </div>
            {aside && <div className="ui-page-header-aside">{aside}</div>}
        </header>
    );
}
