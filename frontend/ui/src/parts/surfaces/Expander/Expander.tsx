import {
    type ComponentPropsWithRef,
    type ReactNode,
    useId,
    useState,
} from 'react';

export type ExpanderProps = Omit<ComponentPropsWithRef<'section'>, 'title'> & {
    title: ReactNode;
    count?: number;
    open?: boolean;
    defaultOpen?: boolean;
    onOpenChange?: (open: boolean) => void;
};

export function Expander({
    title,
    count,
    open,
    defaultOpen = true,
    onOpenChange,
    className,
    children,
    ...rest
}: ExpanderProps) {
    const base = useId();
    const [own, setOwn] = useState(defaultOpen);
    const isOpen = open ?? own;
    const bodyId = `${base}-body`;
    const toggle = () => {
        setOwn(!isOpen);
        onOpenChange?.(!isOpen);
    };
    return (
        <section
            className={className ? `ui-expander ${className}` : 'ui-expander'}
            data-open={isOpen ? '' : undefined}
            {...rest}
        >
            <h3 className="ui-expander-heading">
                <button
                    type="button"
                    className="ui-expander-toggle"
                    aria-expanded={isOpen}
                    aria-controls={bodyId}
                    onClick={toggle}
                >
                    <span className="ui-expander-arrow" aria-hidden="true" />
                    <span className="ui-expander-title">{title}</span>
                    {count !== undefined && (
                        <span className="ui-expander-count">({count})</span>
                    )}
                </button>
                <span className="ui-expander-rule" aria-hidden="true" />
            </h3>
            <div id={bodyId} className="ui-expander-body" hidden={!isOpen}>
                {children}
            </div>
        </section>
    );
}
