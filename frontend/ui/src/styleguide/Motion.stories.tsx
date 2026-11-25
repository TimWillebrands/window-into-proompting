import type { Meta, StoryObj } from '@storybook/react-vite';
import { useState } from 'react';
import { flushSync } from 'react-dom';
import { Page, Section, TokenName } from './Showcase';

const meta: Meta = {
    title: 'Styleguide/Motion',
    parameters: { layout: 'padded' },
};

export default meta;

const durations = ['--ui-dur-instant', '--ui-dur-quick', '--ui-dur-move'];
const easings = ['--ui-ease-out', '--ui-ease-spring', '--ui-ease-step'];

function Run({
    duration,
    easing,
    on,
}: {
    duration: string;
    easing: string;
    on: boolean;
}) {
    return (
        <div className="ui-sg-motion-row">
            <span>
                <TokenName name={duration} />
                <br />
                <TokenName name={easing} />
            </span>
            <div className="ui-sg-track" data-on={on ? '' : undefined}>
                <div
                    className="ui-sg-dot"
                    style={{
                        transitionDuration: `var(${duration})`,
                        transitionTimingFunction: `var(${easing})`,
                    }}
                />
            </div>
        </div>
    );
}

function Runs({ reduced }: { reduced: boolean }) {
    const [on, setOn] = useState(false);
    return (
        <div
            className="ui-sg-motion-grid"
            data-ui-reduce-motion={reduced ? '' : undefined}
        >
            <div>
                <button
                    type="button"
                    className="ui-sg-button"
                    onClick={() => setOn(!on)}
                >
                    Run {reduced ? 'with Reduce Motion' : 'normally'}
                </button>
            </div>
            {durations.flatMap((duration) =>
                easings.map((easing) => (
                    <Run
                        key={`${duration}${easing}`}
                        duration={duration}
                        easing={easing}
                        on={on}
                    />
                )),
            )}
        </div>
    );
}

export const Durations: StoryObj = {
    render: () => (
        <Page
            title="Motion"
            intro="Only things that change place or presence move. Under Reduce Motion the travel drops to zero and a very quick crossfade remains, so the change still reads."
        >
            <Section title="Normal">
                <Runs reduced={false} />
            </Section>
            <Section title="Reduce Motion, forced on this subtree">
                <Runs reduced />
            </Section>
        </Page>
    ),
};

function MorphDemo() {
    const [open, setOpen] = useState(false);
    const toggle = () => {
        const next = () => flushSync(() => setOpen((o) => !o));
        if (document.startViewTransition) document.startViewTransition(next);
        else next();
    };
    return (
        <div className="ui-sg-morph-stage ui-sg-busy">
            <button
                type="button"
                className="ui-sg-glass ui-sg-morph"
                data-open={open ? '' : undefined}
                onClick={toggle}
            >
                {open
                    ? 'Notification center: click to fold back into the banner'
                    : 'A banner: click to open the center'}
            </button>
        </div>
    );
}

export const Morph: StoryObj = {
    render: () => (
        <Page
            title="Morph"
            intro="Related shapes morph with the View Transitions API, using --ui-dur-move. Without the API it is an instant swap; with Reduce Motion (the toolbar or the system setting) the morph becomes a crossfade."
        >
            <MorphDemo />
        </Page>
    ),
};
