import type { Meta, StoryObj } from '@storybook/react-vite';
import { expect } from 'storybook/test';
import { Page, SchemePair, Section } from '../../../styleguide/Showcase';
import { Icon } from './Icon';
import { type IconName, iconNames, iconSizes } from './registry.gen';
import './stories.css';

const meta = {
    title: 'Icons/Icon',
    component: Icon,
    parameters: { layout: 'padded' },
    args: { name: 'home', size: 32 },
    argTypes: {
        name: { control: 'select', options: iconNames },
        size: { control: 'inline-radio', options: iconSizes },
        label: { control: 'text' },
    },
} satisfies Meta<typeof Icon>;

export default meta;

type Story = StoryObj<typeof meta>;

export const Playground: Story = {};

const sample: IconName[] = [
    'world',
    'home',
    'check',
    'add',
    'close',
    'warning',
    'search',
    'zoom-in',
    'zoom-out',
    'locate',
    'undo',
    'redo',
    'coin',
    'palette',
    'vr-headset',
    'galaxy',
    'mountain',
    'fire',
    'theatre',
    'lie-down',
];

function Sheet() {
    return (
        <div className="ui-sg-icon-sheet">
            {sample.map((name) => (
                <span key={name} className="ui-sg-icon-sample">
                    <span className="ui-sg-icon-sizes">
                        {iconSizes.map((size) => (
                            <Icon key={size} name={name} size={size} />
                        ))}
                    </span>
                    <code>{name}</code>
                </span>
            ))}
        </div>
    );
}

export const Default: Story = {
    render: () => (
        <div className="ui-sg-icon-faces">
            <div className="ui-sg-icon-face" data-ui-scheme="luna-blue">
                <Sheet />
            </div>
            <div className="ui-sg-icon-face" data-ui-scheme="noir">
                <Sheet />
            </div>
        </div>
    ),
};

const menu: [IconName, string][] = [
    ['folder-open', 'Open…'],
    ['save', 'Save'],
    ['print', 'Print…'],
    ['search', 'Search'],
    ['settings', 'Settings'],
    ['help', 'Help and Support'],
];

const desktop: [IconName, string][] = [
    ['world', 'World Monitor'],
    ['messenger', 'Messenger'],
    ['places', 'Places'],
    ['board', 'Partyboard'],
    ['home', 'Home'],
    ['chat', 'Chat'],
];

function Menu() {
    return (
        <div className="ui-sg-icon-menu">
            {menu.map(([name, text], i) => (
                <span key={name} data-selected={i === 1 ? '' : undefined}>
                    <Icon name={name} />
                    {text}
                </span>
            ))}
        </div>
    );
}

function Dialog() {
    return (
        <div className="ui-sg-icon-dialog">
            <Icon name="warning" size={32} />
            <span>
                This Persona is still talking. If you end the Party now, the
                conversation stops where it is.
            </span>
        </div>
    );
}

function Desktop() {
    return (
        <div className="ui-sg-icon-desktop">
            {desktop.map(([name, text]) => (
                <span key={name}>
                    <Icon name={name} size={48} />
                    {text}
                </span>
            ))}
        </div>
    );
}

export const Sizes: Story = {
    render: () => (
        <Page
            title="Icon sizes"
            intro="Three sizes, each for its place: 16 in menus, lists and toolbars, 32 in dialogs and task panes, 48 on the desktop and in the Start menu."
        >
            <Section title="16: menus, lists, toolbars">
                <Menu />
            </Section>
            <Section title="32: dialogs, task panes">
                <Dialog />
            </Section>
            <Section title="48: desktop, Start menu">
                <Desktop />
            </Section>
            <Section title="Real and pixel side by side">
                <div className="ui-sg-icon-row">
                    {(['world', 'messenger', 'home', 'chat'] as const).map(
                        (name) =>
                            iconSizes.map((size) => (
                                <Icon
                                    key={`${name}-${size}`}
                                    name={name}
                                    size={size}
                                />
                            )),
                    )}
                </div>
            </Section>
        </Page>
    ),
};

export const LabelledAndDecorative: Story = {
    render: () => (
        <Page
            title="Labelled and decorative"
            intro="An icon next to its words is decorative: it renders alt='' and aria-hidden, so a screen reader reads the words once. An icon that stands alone, like a toolbar button without text, gets a label."
        >
            <Section title="Decorative (the default)">
                <span className="ui-sg-icon-line" data-story="decorative">
                    <Icon name="warning" />
                    Disk space is running low
                </span>
            </Section>
            <Section title="Labelled">
                <button type="button" className="ui-sg-button">
                    <Icon name="search" label="Search" />
                </button>
            </Section>
        </Page>
    ),
    play: async ({ canvas, canvasElement }) => {
        await expect(
            canvas.getByRole('img', { name: 'Search' }),
        ).toBeInTheDocument();
        const decorative = canvasElement.querySelector(
            '[data-story="decorative"] img',
        );
        await expect(decorative).toHaveAttribute('alt', '');
        await expect(decorative).toHaveAttribute('aria-hidden', 'true');
    },
};

export const Noir: Story = {
    render: () => (
        <Page
            title="Icons in both schemes"
            intro="Icons are art, not tokens: the same pixels in Luna Blue and Noir. Their drop shadow and outlines keep them standing on either face."
        >
            <SchemePair>
                {() => (
                    <>
                        <Menu />
                        <Dialog />
                        <Desktop />
                    </>
                )}
            </SchemePair>
        </Page>
    ),
};
