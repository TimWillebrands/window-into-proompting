import type { Decorator } from '@storybook/react-vite';
import type { ReactNode } from 'react';
import type { Scheme } from '../../scheme';
import './story-kit.css';

export type StageOptions = {
    scheme?: Scheme;
    reduceMotion?: boolean;
    reduceTransparency?: boolean;
    width?: 'auto' | 'wide';
};

export function Stage({
    scheme,
    reduceMotion,
    reduceTransparency,
    width = 'auto',
    children,
}: StageOptions & { children: ReactNode }) {
    return (
        <div
            className="ui-story-stage"
            data-ui-scheme={scheme}
            data-ui-reduce-motion={reduceMotion ? '' : undefined}
            data-ui-reduce-transparency={reduceTransparency ? '' : undefined}
            data-width={width}
        >
            {children}
        </div>
    );
}

export const stage: Decorator = (Story, context) => (
    <Stage {...(context.parameters.stage as StageOptions | undefined)}>
        <Story />
    </Stage>
);

export const noir = { stage: { scheme: 'noir' } };
export const reducedMotion = { stage: { reduceMotion: true } };
export const reducedTransparency = { stage: { reduceTransparency: true } };

export function States({
    items,
}: {
    items: ReadonlyArray<readonly [string, ReactNode]>;
}) {
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
