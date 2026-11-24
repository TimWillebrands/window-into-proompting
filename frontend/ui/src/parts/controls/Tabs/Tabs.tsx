import {
    type ComponentPropsWithRef,
    type KeyboardEvent,
    type ReactNode,
    useId,
    useRef,
    useState,
} from 'react';

export type TabItem = {
    value: string;
    label: ReactNode;
    icon?: ReactNode;
    disabled?: boolean;
    content: ReactNode;
};

export type TabsProps = Omit<
    ComponentPropsWithRef<'div'>,
    'children' | 'onChange'
> & {
    items: ReadonlyArray<TabItem>;
    value?: string;
    onValueChange?: (value: string) => void;
    label?: string;
};

export function Tabs({
    items,
    value,
    onValueChange,
    label,
    className,
    ...rest
}: TabsProps) {
    const base = useId();
    const list = useRef<HTMLDivElement>(null);
    const enabled = items.filter((item) => !item.disabled);
    const [own, setOwn] = useState(enabled[0]?.value);
    const current = value ?? own;
    const active = items.find((item) => item.value === current) ?? enabled[0];

    const choose = (next: string) => {
        setOwn(next);
        onValueChange?.(next);
    };

    const handleKeyDown = (event: KeyboardEvent<HTMLDivElement>) => {
        const index = enabled.findIndex((item) => item.value === active?.value);
        let to: number | undefined;
        if (event.key === 'ArrowRight') to = (index + 1) % enabled.length;
        else if (event.key === 'ArrowLeft')
            to = (index - 1 + enabled.length) % enabled.length;
        else if (event.key === 'Home') to = 0;
        else if (event.key === 'End') to = enabled.length - 1;
        const target = to === undefined ? undefined : enabled[to];
        if (!target) return;
        event.preventDefault();
        choose(target.value);
        list.current
            ?.querySelector<HTMLElement>(
                `[data-value="${CSS.escape(target.value)}"]`,
            )
            ?.focus();
    };

    return (
        <div
            className={className ? `ui-tabs ${className}` : 'ui-tabs'}
            {...rest}
        >
            <div
                ref={list}
                role="tablist"
                aria-label={label}
                className="ui-tabs-list"
                onKeyDown={handleKeyDown}
            >
                {items.map((item) => {
                    const selected = item.value === active?.value;
                    return (
                        <button
                            key={item.value}
                            type="button"
                            role="tab"
                            id={`${base}-tab-${item.value}`}
                            aria-selected={selected}
                            aria-controls={`${base}-panel-${item.value}`}
                            tabIndex={selected ? 0 : -1}
                            disabled={item.disabled}
                            className="ui-tabs-tab"
                            data-value={item.value}
                            onClick={() => choose(item.value)}
                        >
                            {item.icon && (
                                <span
                                    className="ui-tabs-icon"
                                    aria-hidden="true"
                                >
                                    {item.icon}
                                </span>
                            )}
                            {item.label}
                        </button>
                    );
                })}
            </div>
            {active && (
                <div
                    role="tabpanel"
                    id={`${base}-panel-${active.value}`}
                    aria-labelledby={`${base}-tab-${active.value}`}
                    className="ui-tabs-panel"
                >
                    {active.content}
                </div>
            )}
        </div>
    );
}
