import {
    type ComponentPropsWithRef,
    type ReactNode,
    useId,
    useState,
} from 'react';

export type TaskPaneProps = ComponentPropsWithRef<'aside'>;

export function TaskPane({ className, children, ...rest }: TaskPaneProps) {
    return (
        <aside
            className={className ? `ui-taskpane ${className}` : 'ui-taskpane'}
            {...rest}
        >
            {children}
        </aside>
    );
}

export type TaskPaneSectionProps = Omit<
    ComponentPropsWithRef<'section'>,
    'title'
> & {
    title: ReactNode;
    icon?: ReactNode;
    aside?: ReactNode;
    special?: boolean;
    collapsible?: boolean;
    open?: boolean;
    defaultOpen?: boolean;
    onOpenChange?: (open: boolean) => void;
};

export function TaskPaneSection({
    title,
    icon,
    aside,
    special = false,
    collapsible = true,
    open,
    defaultOpen = true,
    onOpenChange,
    className,
    children,
    ...rest
}: TaskPaneSectionProps) {
    const base = useId();
    const [own, setOwn] = useState(defaultOpen);
    const isOpen = !collapsible || (open ?? own);
    const titleId = `${base}-title`;
    const bodyId = `${base}-body`;
    const asideNode = aside !== undefined && aside !== null && (
        <span className="ui-taskpane-aside">{aside}</span>
    );
    const toggle = () => {
        setOwn(!isOpen);
        onOpenChange?.(!isOpen);
    };
    return (
        <section
            className={
                className
                    ? `ui-taskpane-section ${className}`
                    : 'ui-taskpane-section'
            }
            aria-labelledby={titleId}
            data-special={special ? '' : undefined}
            data-open={isOpen ? '' : undefined}
            data-has-icon={icon ? '' : undefined}
            {...rest}
        >
            <h3 className="ui-taskpane-heading">
                {icon && (
                    <span
                        className="ui-taskpane-section-icon"
                        aria-hidden="true"
                    >
                        {icon}
                    </span>
                )}
                {collapsible ? (
                    <button
                        type="button"
                        id={titleId}
                        className="ui-taskpane-toggle"
                        aria-expanded={isOpen}
                        aria-controls={bodyId}
                        onClick={toggle}
                    >
                        <span className="ui-taskpane-title">{title}</span>
                        {asideNode}
                        <span
                            className="ui-taskpane-chevron"
                            aria-hidden="true"
                        />
                    </button>
                ) : (
                    <span id={titleId} className="ui-taskpane-title">
                        <span className="ui-taskpane-title-text">{title}</span>
                        {asideNode}
                    </span>
                )}
            </h3>
            <div id={bodyId} className="ui-taskpane-body" hidden={!isOpen}>
                {children}
            </div>
        </section>
    );
}

type LinkProps = {
    icon?: ReactNode;
    children: ReactNode;
};

export type TaskPaneLinkProps =
    | (LinkProps &
          Omit<ComponentPropsWithRef<'a'>, 'children'> & { href: string })
    | (LinkProps &
          Omit<ComponentPropsWithRef<'button'>, 'children'> & {
              href?: undefined;
          });

export function TaskPaneLink(props: TaskPaneLinkProps) {
    const inner = (icon: ReactNode, children: ReactNode) => (
        <>
            {icon && (
                <span className="ui-taskpane-link-icon" aria-hidden="true">
                    {icon}
                </span>
            )}
            <span className="ui-taskpane-link-text">{children}</span>
        </>
    );
    if (props.href !== undefined) {
        const { icon, children, className, ...rest } = props;
        return (
            <a
                className={
                    className
                        ? `ui-taskpane-link ${className}`
                        : 'ui-taskpane-link'
                }
                {...rest}
            >
                {inner(icon, children)}
            </a>
        );
    }
    const { icon, children, className, type = 'button', ...rest } = props;
    return (
        <button
            type={type}
            className={
                className ? `ui-taskpane-link ${className}` : 'ui-taskpane-link'
            }
            {...rest}
        >
            {inner(icon, children)}
        </button>
    );
}
