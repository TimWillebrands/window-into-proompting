import type { ComponentPropsWithRef, CSSProperties, ReactNode } from 'react';
import type { Tone } from '../../controls/tone';

export type ItemPickerItem = {
    id: string;
    label: ReactNode;
    picture: ReactNode;
    swatch?: string;
    detail?: ReactNode;
    detailTone?: Tone;
    badge?: ReactNode;
    badgeTone?: Tone;
    tag?: ReactNode;
    hint?: string;
    disabled?: boolean;
};

export type ItemPickerProps = Omit<
    ComponentPropsWithRef<'div'>,
    'children' | 'onSelect' | 'onToggle'
> & {
    items: ReadonlyArray<ItemPickerItem>;
    selected?: ReadonlyArray<string>;
    onToggle?: (id: string) => void;
    label: string;
    empty?: ReactNode;
    size?: 'small' | 'medium';
};

export function ItemPicker({
    items,
    selected = [],
    onToggle,
    label,
    empty,
    size = 'medium',
    className,
    ...rest
}: ItemPickerProps) {
    const chosen = new Set(selected);
    return (
        // biome-ignore lint/a11y/useSemanticElements: a picker is a set of toggle buttons, not a form fieldset
        <div
            role="group"
            aria-label={label}
            className={
                className ? `ui-item-picker ${className}` : 'ui-item-picker'
            }
            data-size={size}
            {...rest}
        >
            {items.length === 0 && empty && (
                <div className="ui-item-picker-empty">{empty}</div>
            )}
            {items.map((item) => (
                <button
                    key={item.id}
                    type="button"
                    className="ui-item-picker-item"
                    aria-pressed={chosen.has(item.id)}
                    disabled={item.disabled}
                    title={item.hint}
                    onClick={() => onToggle?.(item.id)}
                >
                    <span
                        className="ui-item-picker-picture"
                        data-swatch={item.swatch ? '' : undefined}
                        style={
                            item.swatch
                                ? ({
                                      '--ui-item-swatch': item.swatch,
                                  } as CSSProperties)
                                : undefined
                        }
                    >
                        <span className="ui-item-picker-glyph">
                            {item.picture}
                        </span>
                        {item.tag && (
                            <span className="ui-item-picker-tag">
                                {item.tag}
                            </span>
                        )}
                        {item.badge && (
                            <span
                                className="ui-item-picker-badge"
                                data-tone={item.badgeTone ?? 'neutral'}
                            >
                                {item.badge}
                            </span>
                        )}
                    </span>
                    <span className="ui-item-picker-label">{item.label}</span>
                    {item.detail !== undefined && (
                        <span
                            className="ui-item-picker-detail"
                            data-tone={item.detailTone}
                        >
                            {item.detail}
                        </span>
                    )}
                </button>
            ))}
        </div>
    );
}
