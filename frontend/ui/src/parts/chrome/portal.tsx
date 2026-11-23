import { type ReactNode, useLayoutEffect, useState } from 'react';
import { createPortal } from 'react-dom';

const inherited = [
    'data-ui-scheme',
    'data-ui-reduce-motion',
    'data-ui-reduce-transparency',
    'data-ui-contrast',
] as const;

type Attributes = Partial<Record<(typeof inherited)[number], string>>;

const attributesOf = (owner: Element | null | undefined): Attributes => {
    const found: Attributes = {};
    if (!owner) return found;
    for (const name of inherited) {
        const holder = owner.closest(`[${name}]`);
        const value = holder?.getAttribute(name);
        if (value !== null && value !== undefined) found[name] = value;
    }
    return found;
};

export function ChromePortal({
    owner,
    children,
}: {
    owner: Element | null | undefined;
    children: ReactNode;
}) {
    const [host, setHost] = useState<HTMLElement | null>(null);
    const [attributes, setAttributes] = useState<Attributes>({});
    useLayoutEffect(() => {
        setHost(document.body);
        setAttributes(attributesOf(owner));
    }, [owner]);
    if (!host) return null;
    return createPortal(
        <div className="ui-chrome-layer" {...attributes}>
            {children}
        </div>,
        host,
    );
}
