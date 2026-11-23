import type { ReactNode } from 'react';
import './story-kit.css';

export const noir = { globals: { scheme: 'noir' } };
export const reducedMotion = { globals: { reduceMotion: 'on' } };
export const reducedTransparency = {
    globals: { reduceTransparency: 'on', backgrounds: { value: 'busy' } },
};
export const busy = { globals: { backgrounds: { value: 'busy' } } };

export function Desk({
    children,
    size = 'medium',
}: {
    children: ReactNode;
    size?: 'small' | 'medium' | 'large' | 'full';
}) {
    return (
        <div className="ui-story-desk" data-size={size}>
            {children}
        </div>
    );
}

export function States({
    items,
}: {
    items: ReadonlyArray<readonly [string, ReactNode]>;
}) {
    return (
        <div className="ui-story-chrome-states">
            {items.map(([label, node]) => (
                <figure key={label} className="ui-story-chrome-state">
                    <figcaption>{label}</figcaption>
                    <div>{node}</div>
                </figure>
            ))}
        </div>
    );
}

export const preview =
    (selector: string, state: string) =>
    async ({ canvasElement }: { canvasElement: HTMLElement }) => {
        for (const element of canvasElement.ownerDocument.querySelectorAll(
            selector,
        ))
            element.setAttribute('data-ui-preview', state);
    };
