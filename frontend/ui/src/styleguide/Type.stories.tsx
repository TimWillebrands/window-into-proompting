import type { Meta, StoryObj } from '@storybook/react-vite';
import { useLayoutEffect, useRef, useState } from 'react';
import { Page, Section, TokenName } from './Showcase';

const meta: Meta = {
    title: 'Styleguide/Type',
    parameters: { layout: 'padded' },
};

export default meta;

const ramp = [
    {
        token: '--ui-text-logon',
        className: 'ui-sg-logon-text',
        use: 'The Welcome screen greeting only',
        sample: 'welcome',
    },
    {
        token: '--ui-text-display',
        className: 'ui-sg-display-text',
        use: 'Welcome screens, the Start button',
        sample: 'Welcome to Partytown',
    },
    {
        token: '--ui-text-title',
        className: 'ui-sg-title-text',
        use: 'Section titles, the Start menu user',
        sample: 'Display Properties',
    },
    {
        token: '--ui-text-caption',
        className: 'ui-sg-caption-text',
        use: 'Window and dialog title bars',
        sample: 'Untitled - Notepad',
    },
    {
        token: '--ui-text-reading',
        className: 'ui-sg-reading-text',
        use: 'Anything a Persona or Player wrote',
        sample: 'The kettle is on, come by the Board later.',
    },
    {
        token: '--ui-text-chrome',
        className: 'ui-sg-chrome-text',
        use: 'Menus, buttons, labels, list rows',
        sample: 'File  Edit  View  Favorites  Tools  Help',
    },
];

const fonts = [
    {
        token: '--ui-font-ui',
        className: 'ui-sg-reading-text',
        sample: 'Tahoma carries the chrome: Ag 0123456789 äéñ',
    },
    {
        token: '--ui-font-title',
        className: 'ui-sg-title-text',
        sample: 'Trebuchet MS Bold for titles',
    },
    {
        token: '--ui-font-mono',
        className: 'ui-sg-mono-text',
        sample: 'C:\\Documents and Settings\\Party> dir /w',
    },
    {
        token: '--ui-font-display',
        className: 'ui-sg-display-text',
        sample: 'Franklin Gothic for welcome',
    },
    {
        token: '--ui-font-emoji',
        className: 'ui-sg-emoji-text',
        sample: '🎉 🏠 ☕ 👩‍💻 🇳🇱 ❤️',
    },
];

function Stack({ token }: { token: string }) {
    const probe = useRef<HTMLSpanElement>(null);
    const [stack, setStack] = useState('');
    useLayoutEffect(() => {
        if (probe.current)
            setStack(
                getComputedStyle(probe.current).getPropertyValue(token).trim(),
            );
    }, [token]);
    return (
        <span ref={probe} className="ui-sg-label">
            {stack}
        </span>
    );
}

export const Ramp: StoryObj = {
    render: () => (
        <Page
            title="Type"
            intro="Nothing outside the ramp. Chrome is XP's own 8pt Tahoma; comfort comes from the reading size and its line height, not from bigger chrome."
        >
            <Section title="The ramp">
                {ramp.map((step) => (
                    <div key={step.token} className="ui-sg-type-row">
                        <div>
                            <TokenName name={step.token} />
                            <br />
                            <span>{step.use}</span>
                        </div>
                        <span className={step.className}>{step.sample}</span>
                    </div>
                ))}
            </Section>
            <Section title="Chrome versus reading">
                <div className="ui-sg-columns">
                    <div className="ui-sg-section">
                        <TokenName name="--ui-text-chrome, --ui-leading-chrome" />
                        <div className="ui-sg-menu ui-sg-chrome-text">
                            <span>New Message</span>
                            <span data-selected="">Reply to Sender</span>
                            <span>Forward</span>
                            <span>Delete</span>
                        </div>
                    </div>
                    <div className="ui-sg-section">
                        <TokenName name="--ui-text-reading, --ui-leading-reading" />
                        <p className="ui-sg-reading-text">
                            Marigold left a note on the Board: the café on the
                            corner has a new espresso machine, and whoever gets
                            there first tomorrow should save two seats by the
                            window. Bring the crossword. 🙂
                        </p>
                    </div>
                </div>
            </Section>
        </Page>
    ),
};

export const Fonts: StoryObj = {
    render: () => (
        <Page
            title="Fonts"
            intro="Fonts come only from tokens. Each stack starts with the Windows face and falls back to a self-hosted one, so Linux and phones still look like XP."
        >
            {fonts.map((font) => (
                <div key={font.token} className="ui-sg-type-row">
                    <div>
                        <TokenName name={font.token} />
                        <br />
                        <Stack token={font.token} />
                    </div>
                    <span className={font.className}>{font.sample}</span>
                </div>
            ))}
        </Page>
    ),
};
