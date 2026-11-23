import type { Meta, StoryObj } from '@storybook/react-vite';
import { type CSSProperties, useLayoutEffect, useRef, useState } from 'react';
import { Page, Section, TokenName } from './Showcase';

const meta: Meta = {
    title: 'Styleguide/Shape',
    parameters: { layout: 'padded' },
};

export default meta;

const radii = [
    '--ui-radius-control',
    '--ui-radius-pane',
    '--ui-radius-float',
    '--ui-radius-window',
    '--ui-radius-pill',
];

function Measured({ className, label }: { className: string; label: string }) {
    const box = useRef<HTMLDivElement>(null);
    const [radius, setRadius] = useState('');
    useLayoutEffect(() => {
        if (box.current)
            setRadius(getComputedStyle(box.current).borderTopLeftRadius);
    }, []);
    return (
        <div ref={box} className={className}>
            {label} {radius}
        </div>
    );
}

export const Radii: StoryObj = {
    render: () => (
        <Page
            title="Shape"
            intro="Generous Liquid Glass radii, concentric from the window inward. Controls are capped and never become capsules; pills only where XP had them."
        >
            <Section title="The scale">
                <div className="ui-sg-radius-row">
                    {radii.map((token) => (
                        <div key={token} className="ui-sg-swatch">
                            <div
                                className="ui-sg-radius"
                                style={{ borderRadius: `var(${token})` }}
                            />
                            <TokenName name={token} />
                        </div>
                    ))}
                </div>
            </Section>
            <Section title="Concentric: window, pane, control">
                <p>
                    The window sets --ui-radius-here and --ui-inset. A real
                    window's inset is its frame, --ui-frame-width, so its body
                    is concentric at the window radius minus the frame. A pane
                    sits a step further in and uses --ui-radius-pane, then sets
                    its own here and inset. The control uses --ui-radius-inner
                    and lands on the control cap.
                </p>
                <div className="ui-sg-nest-window">
                    <span>Window: --ui-radius-window, inset --ui-space-3</span>
                    <div className="ui-sg-nest-pane">
                        <span>Pane: --ui-radius-pane, inset --ui-space-4</span>
                        <Measured
                            className="ui-sg-nest-leaf"
                            label="Control: --ui-radius-inner ="
                        />
                    </div>
                </div>
            </Section>
            <Section title="--ui-radius-inner follows the inset">
                <div className="ui-sg-columns">
                    {['--ui-space-2', '--ui-space-4', '--ui-space-6'].map(
                        (inset) => (
                            <div
                                key={inset}
                                className="ui-sg-inset-demo"
                                style={
                                    {
                                        '--ui-inset': `var(${inset})`,
                                    } as CSSProperties
                                }
                            >
                                <Measured
                                    className=""
                                    label={`inset ${inset}, inner =`}
                                />
                            </div>
                        ),
                    )}
                </div>
            </Section>
        </Page>
    ),
};
