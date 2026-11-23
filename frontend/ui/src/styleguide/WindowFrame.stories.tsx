import type { Meta, StoryObj } from '@storybook/react-vite';
import type { CSSProperties } from 'react';
import { Window } from '../parts/chrome';
import { Icon } from '../parts/icons';
import { type Scheme, schemes } from '../scheme';
import { Page, Section, schemeNames } from './Showcase';

const meta: Meta = {
    title: 'Styleguide/Window frame',
    parameters: { layout: 'padded' },
};

export default meta;

type Variant = {
    key: string;
    title: string;
    note: string;
    width?: string;
    glass?: boolean;
};

const variants: Variant[] = [
    {
        key: 'glass',
        title: 'Before: thin glass',
        note: '1px edge, see-through tint',
        glass: true,
    },
    { key: '2', title: 'Luna 2px', note: 'slim ring', width: '2px' },
    {
        key: '3',
        title: 'Luna 3px (default)',
        note: '--ui-frame-width',
        width: '3px',
    },
    { key: '4', title: 'Luna 4px', note: 'closest to XP', width: '4px' },
];

const states = [
    { key: 'active', title: 'Active', active: true },
    { key: 'inactive', title: 'Inactive', active: false },
];

type Corner = 'top' | 'bottom';

function Sample({
    scheme,
    variant,
    active,
    closeUp,
}: {
    scheme: Scheme;
    variant: Variant;
    active: boolean;
    closeUp?: Corner;
}) {
    return (
        <div
            className="ui-sg-frame-cell"
            data-glass={variant.glass ? '' : undefined}
            data-close-up={closeUp}
        >
            <Window
                title="Notepad"
                icon={<Icon name="notepad" />}
                active={active}
                className="ui-sg-frame-window"
                style={
                    variant.width
                        ? ({
                              '--ui-frame-width': variant.width,
                          } as CSSProperties)
                        : undefined
                }
                onMinimise={() => undefined}
                onMaximise={() => undefined}
                onClose={() => undefined}
                statusBar={<span>Ln 1, Col 1</span>}
                aria-label={`${schemeNames[scheme]}, ${variant.title}, ${active ? 'active' : 'inactive'}`}
            >
                <p className="ui-sg-frame-text">
                    The body is the XP face. The frame is Luna.
                </p>
            </Window>
        </div>
    );
}

function Grid({ closeUp }: { closeUp?: Corner }) {
    return (
        <div className="ui-sg-frame-grid" data-close-up={closeUp}>
            <span />
            {variants.map((variant) => (
                <div key={variant.key} className="ui-sg-frame-head">
                    <strong>{variant.title}</strong>
                    <span>{variant.note}</span>
                </div>
            ))}
            {schemes.flatMap((scheme) =>
                states.map((state) => (
                    <div
                        key={`${scheme}-${state.key}`}
                        className="ui-sg-frame-row"
                        data-ui-scheme={scheme}
                    >
                        <div className="ui-sg-frame-side">
                            <strong>{schemeNames[scheme]}</strong>
                            <span>{state.title}</span>
                        </div>
                        {variants.map((variant) => (
                            <Sample
                                key={variant.key}
                                scheme={scheme}
                                variant={variant}
                                active={state.active}
                                closeUp={closeUp}
                            />
                        ))}
                    </div>
                )),
            )}
        </div>
    );
}

export const Comparison: StoryObj = {
    render: () => (
        <Page title="Window frame">
            <Grid />
        </Page>
    ),
};

export const CloseUp: StoryObj = {
    render: () => (
        <Page
            title="Window frame: the right-hand corners at 3x"
            intro="The close button is concentric with the window: its top-right radius is the window radius minus its inset from the edge, so it follows the curve instead of meeting it."
        >
            <Section title="Top right: caption buttons">
                <Grid closeUp="top" />
            </Section>
            <Section title="Bottom right: frame ring">
                <Grid closeUp="bottom" />
            </Section>
        </Page>
    ),
};
