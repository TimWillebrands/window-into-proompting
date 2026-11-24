import type { ComponentPropsWithRef } from 'react';

export type Space = 'none' | 'xxs' | 'xs' | 'sm' | 'md' | 'lg' | 'xl';

export type StackTag =
    | 'div'
    | 'span'
    | 'section'
    | 'header'
    | 'footer'
    | 'nav'
    | 'aside'
    | 'article'
    | 'ul'
    | 'ol'
    | 'li'
    | 'dl'
    | 'dt'
    | 'dd'
    | 'form'
    | 'label'
    | 'fieldset'
    | 'strong'
    | 'p';

type Polymorphic<T extends StackTag, Own> = Own & { as?: T } & Omit<
        ComponentPropsWithRef<T>,
        keyof Own | 'as'
    >;

type StackOwnProps = {
    direction?: 'column' | 'row';
    gap?: Space;
    rowGap?: Space;
    pad?: Space;
    align?: 'stretch' | 'start' | 'center' | 'end' | 'baseline';
    justify?: 'start' | 'center' | 'end' | 'between';
    wrap?: boolean;
    grow?: boolean;
    inline?: boolean;
};

export type StackProps<T extends StackTag = 'div'> = Polymorphic<
    T,
    StackOwnProps
>;

const join = (base: string, className?: string) =>
    className ? `${base} ${className}` : base;

function StackRoot<T extends StackTag = 'div'>({
    as,
    direction = 'column',
    gap,
    rowGap,
    pad,
    align,
    justify,
    wrap = false,
    grow = false,
    inline = false,
    className,
    ...rest
}: StackProps<T>) {
    const Element = (as ?? 'div') as 'div';
    return (
        <Element
            className={join('ui-stack', className)}
            data-direction={direction}
            data-gap={gap}
            data-row-gap={rowGap}
            data-pad={pad}
            data-align={align}
            data-justify={justify}
            data-wrap={wrap ? '' : undefined}
            data-grow={grow ? '' : undefined}
            data-inline={inline ? '' : undefined}
            {...(rest as ComponentPropsWithRef<'div'>)}
        />
    );
}

type StackItemOwnProps = {
    grow?: boolean;
    truncate?: boolean;
};

export type StackItemProps<T extends StackTag = 'span'> = Polymorphic<
    T,
    StackItemOwnProps
>;

function StackItem<T extends StackTag = 'span'>({
    as,
    grow = false,
    truncate = false,
    className,
    ...rest
}: StackItemProps<T>) {
    const Element = (as ?? 'span') as 'span';
    return (
        <Element
            className={join('ui-stack-item', className)}
            data-grow={grow ? '' : undefined}
            data-truncate={truncate ? '' : undefined}
            {...(rest as ComponentPropsWithRef<'span'>)}
        />
    );
}

export const Stack = Object.assign(StackRoot, { Item: StackItem });
