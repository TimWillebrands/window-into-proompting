import {
    type ComponentPropsWithRef,
    createContext,
    type KeyboardEvent,
    type ReactNode,
    useContext,
    useLayoutEffect,
    useRef,
} from 'react';

const itemSelector = '.ui-toolbar-button:not(:disabled)';

export type ToolbarVariant = 'regular' | 'bare';

export const ToolbarVariantContext = createContext<ToolbarVariant>('regular');

export type ToolbarProps = ComponentPropsWithRef<'div'> & {
    labels?: 'right' | 'below' | 'none';
    orientation?: 'horizontal' | 'vertical';
    variant?: ToolbarVariant;
};

export function Toolbar({
    labels = 'right',
    orientation = 'horizontal',
    variant,
    className,
    onKeyDown,
    onFocus,
    children,
    ref,
    ...rest
}: ToolbarProps) {
    const root = useRef<HTMLDivElement | null>(null);
    const surrounding = useContext(ToolbarVariantContext);
    const look = variant ?? surrounding;

    const items = () =>
        Array.from(
            root.current?.querySelectorAll<HTMLElement>(itemSelector) ?? [],
        );

    const makeStop = (target: HTMLElement | undefined) => {
        for (const item of items()) item.tabIndex = item === target ? 0 : -1;
    };

    useLayoutEffect(() => {
        const all = items();
        if (all.length === 0) return;
        const current = all.find((item) => item.tabIndex === 0);
        makeStop(current ?? all[0]);
    });

    const handleKeyDown = (event: KeyboardEvent<HTMLDivElement>) => {
        onKeyDown?.(event);
        if (event.defaultPrevented) return;
        const all = items();
        const index = all.indexOf(event.target as HTMLElement);
        if (index < 0) return;
        const back = orientation === 'vertical' ? 'ArrowUp' : 'ArrowLeft';
        const next = orientation === 'vertical' ? 'ArrowDown' : 'ArrowRight';
        let to: number | undefined;
        if (event.key === next) to = (index + 1) % all.length;
        else if (event.key === back) to = (index - 1 + all.length) % all.length;
        else if (event.key === 'Home') to = 0;
        else if (event.key === 'End') to = all.length - 1;
        if (to === undefined) return;
        event.preventDefault();
        makeStop(all[to]);
        all[to]?.focus();
    };

    return (
        <div
            ref={(node) => {
                root.current = node;
                if (typeof ref === 'function') return ref(node);
                if (ref) ref.current = node;
            }}
            role="toolbar"
            aria-orientation={orientation}
            className={className ? `ui-toolbar ${className}` : 'ui-toolbar'}
            data-labels={labels}
            data-orientation={orientation}
            data-variant={look === 'bare' ? 'bare' : undefined}
            onKeyDown={handleKeyDown}
            onFocus={(event) => {
                onFocus?.(event);
                if (items().includes(event.target as HTMLElement))
                    makeStop(event.target as HTMLElement);
            }}
            {...rest}
        >
            {children}
        </div>
    );
}

export type ToolbarButtonProps = ComponentPropsWithRef<'button'> & {
    icon?: ReactNode;
    pressed?: boolean;
    hasMenu?: boolean;
};

export function ToolbarButton({
    icon,
    pressed,
    hasMenu,
    type = 'button',
    className,
    children,
    ...rest
}: ToolbarButtonProps) {
    return (
        <button
            type={type}
            className={
                className
                    ? `ui-toolbar-button ${className}`
                    : 'ui-toolbar-button'
            }
            aria-pressed={pressed}
            aria-haspopup={hasMenu ? 'menu' : undefined}
            data-icon-only={children ? undefined : ''}
            {...rest}
        >
            {icon && (
                <span className="ui-toolbar-button-icon" aria-hidden="true">
                    {icon}
                </span>
            )}
            {children && (
                <span className="ui-toolbar-button-label">{children}</span>
            )}
            {hasMenu && (
                <span className="ui-toolbar-button-menu" aria-hidden="true" />
            )}
        </button>
    );
}

export function ToolbarSeparator() {
    return <hr className="ui-toolbar-separator" />;
}
