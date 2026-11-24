import {
    type ComponentPropsWithRef,
    type CSSProperties,
    type KeyboardEvent,
    type ReactNode,
    useRef,
} from 'react';

export type SwatchPickerColour = {
    value: string;
    label?: string;
};

export type SwatchPickerProps = Omit<
    ComponentPropsWithRef<'div'>,
    'children' | 'onChange' | 'defaultValue'
> & {
    colours: ReadonlyArray<string | SwatchPickerColour>;
    value?: string | null;
    onValueChange?: (value: string | null) => void;
    label: string;
    noneLabel?: ReactNode;
    disabled?: boolean;
};

const NONE = '';

const keys: Record<string, 1 | -1> = {
    ArrowRight: 1,
    ArrowDown: 1,
    ArrowLeft: -1,
    ArrowUp: -1,
};

export function SwatchPicker({
    colours,
    value,
    onValueChange,
    label,
    noneLabel,
    disabled = false,
    className,
    onKeyDown,
    ...rest
}: SwatchPickerProps) {
    const root = useRef<HTMLDivElement>(null);
    const options = colours.map((colour) =>
        typeof colour === 'string' ? { value: colour } : colour,
    );
    const values = [
        ...options.map((option) => option.value),
        ...(noneLabel !== undefined ? [NONE] : []),
    ];
    const current = value === null ? NONE : value;
    const stop =
        current !== undefined && values.includes(current) ? current : values[0];

    const choose = (next: string) =>
        onValueChange?.(next === NONE ? null : next);

    const focus = (to: string | undefined) => {
        if (to === undefined) return;
        const all = Array.from(
            root.current?.querySelectorAll<HTMLElement>('[data-swatch]') ?? [],
        );
        for (const item of all)
            item.tabIndex = item.dataset.swatch === to ? 0 : -1;
        all.find((item) => item.dataset.swatch === to)?.focus();
    };

    const handleKeyDown = (event: KeyboardEvent<HTMLDivElement>) => {
        onKeyDown?.(event);
        if (event.defaultPrevented || disabled) return;
        const from = (event.target as HTMLElement).dataset.swatch;
        if (from === undefined) return;
        const step = keys[event.key];
        if (step) {
            event.preventDefault();
            const index = values.indexOf(from);
            focus(values[(index + step + values.length) % values.length]);
            return;
        }
        if (event.key === 'Home' || event.key === 'End') {
            event.preventDefault();
            focus(event.key === 'Home' ? values[0] : values.at(-1));
        }
    };

    return (
        // biome-ignore lint/a11y/useSemanticElements: a picker is a set of toggle buttons, not a form fieldset
        <div
            ref={root}
            role="group"
            aria-label={label}
            className={
                className ? `ui-swatch-picker ${className}` : 'ui-swatch-picker'
            }
            onKeyDown={handleKeyDown}
            {...rest}
        >
            {options.map((option) => {
                const checked = option.value === current;
                return (
                    <button
                        key={option.value}
                        type="button"
                        aria-pressed={checked}
                        aria-label={option.label ?? option.value}
                        title={option.label ?? option.value}
                        tabIndex={option.value === stop ? 0 : -1}
                        disabled={disabled}
                        className="ui-swatch-picker-swatch"
                        data-swatch={option.value}
                        style={
                            {
                                '--ui-swatch': option.value,
                            } as CSSProperties
                        }
                        onClick={() => choose(option.value)}
                    />
                );
            })}
            {noneLabel !== undefined && (
                <button
                    type="button"
                    aria-pressed={current === NONE}
                    tabIndex={stop === NONE ? 0 : -1}
                    disabled={disabled}
                    className="ui-swatch-picker-none"
                    data-swatch={NONE}
                    onClick={() => choose(NONE)}
                >
                    {noneLabel}
                </button>
            )}
        </div>
    );
}
