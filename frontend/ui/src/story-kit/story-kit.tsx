import type { Decorator } from '@storybook/react-vite';
import type { ReactNode } from 'react';
import './story-kit.css';

export const noir = { globals: { scheme: 'noir' } };
export const reducedMotion = { globals: { reduceMotion: 'on' } };
export const reducedTransparency = {
    globals: { reduceTransparency: 'on', backgrounds: { value: 'busy' } },
};
export const busy = { globals: { backgrounds: { value: 'busy' } } };

export type StageOptions = {
    width?: 'auto' | 'wide';
};

export function Stage({
    width = 'auto',
    children,
}: StageOptions & { children: ReactNode }) {
    return (
        <div className="ui-story-stage" data-width={width}>
            {children}
        </div>
    );
}

export const stage: Decorator = (Story, context) => (
    <Stage {...(context.parameters.stage as StageOptions | undefined)}>
        <Story />
    </Stage>
);

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
    layout = 'rows',
}: {
    items: ReadonlyArray<readonly [string, ReactNode]>;
    layout?: 'rows' | 'tiles';
}) {
    if (layout === 'tiles')
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
    return (
        <div className="ui-story-states">
            {items.map(([label, node]) => (
                <div key={label} className="ui-story-state">
                    <span className="ui-story-state-label">{label}</span>
                    <div>{node}</div>
                </div>
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

export const picture = (hue: number) =>
    `data:image/svg+xml,${encodeURIComponent(
        `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 32 32"><rect width="32" height="32" fill="hsl(${hue} 60% 80%)"/><circle cx="16" cy="12.5" r="6" fill="hsl(${hue} 45% 45%)"/><path d="M4 32c1-9 6-12 12-12s11 3 12 12z" fill="hsl(${hue} 45% 45%)"/></svg>`,
    )}`;
