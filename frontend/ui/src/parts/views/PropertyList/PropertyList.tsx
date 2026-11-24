import type { HTMLAttributes, ReactNode } from 'react';

export type PropertyListItem = {
    key?: string;
    label: ReactNode;
    value: ReactNode;
    title?: string;
};

export type PropertyListProps = Omit<
    HTMLAttributes<HTMLDListElement>,
    'children'
> & {
    items: ReadonlyArray<PropertyListItem | false | null | undefined>;
    layout?: 'columns' | 'stacked';
};

export function PropertyList({
    items,
    layout = 'columns',
    className,
    ...rest
}: PropertyListProps) {
    const shown = items.filter((item): item is PropertyListItem => !!item);
    return (
        <dl
            className={
                className ? `ui-property-list ${className}` : 'ui-property-list'
            }
            data-layout={layout}
            {...rest}
        >
            {shown.map((item, index) => (
                <div
                    key={
                        item.key ??
                        (typeof item.label === 'string' ? item.label : index)
                    }
                    className="ui-property-list-row"
                    title={item.title}
                >
                    <dt className="ui-property-list-label">{item.label}</dt>
                    <dd className="ui-property-list-value">{item.value}</dd>
                </div>
            ))}
        </dl>
    );
}
