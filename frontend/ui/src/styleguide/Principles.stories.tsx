import type { Meta, StoryObj } from '@storybook/react-vite';
import { Page, Section } from './Showcase';

const meta: Meta = {
    title: 'Styleguide/Principles',
    parameters: { layout: 'padded' },
};

export default meta;

const rules = [
    '80% Windows XP Luna, 20% Liquid Glass. Luna is what it looks like; glass is how it feels.',
    'A screenshot reads as XP at a glance: blue title bars, green Start, red Close, the beige face, Tahoma, plastic buttons, task panes.',
    'Luna wins on looks, comfort wins on use. Tiring to read or hard to hit is wrong, even if period-correct.',
    'Glass only on floating chrome, never on content. Always Luna-tinted, never neutral grey.',
    'Colour carries meaning: blue neutral, green go, amber caution, red danger. Pick a tone, not a colour.',
    'Dark mode is a scheme. Parts read tokens and get Noir for free.',
    'Shapes are concentric from the window inward. Controls stay soft rectangles, never capsules.',
    'Only place and presence move. Reduce Motion means very quick, not zero.',
];

const materials = [
    {
        name: 'Luna chrome',
        what: 'Saturated blue gradients, glossy plastic, light from the top left.',
        where: 'Title bars, taskbar, Start, task pane headers, CommandButton.',
        demo: 'ui-sg-luna',
    },
    {
        name: 'XP face',
        what: 'Opaque beige face and white fields, crisp edges.',
        where: 'Window bodies, dialogs, forms, lists: everything that is content.',
        demo: 'ui-sg-face',
    },
    {
        name: 'Glass',
        what: 'Backdrop blur, Luna tint, specular rim top left, soft shadow.',
        where: 'Floating chrome only: taskbar, Start menu, menus, balloons, banners, tooltips, and the rim and shadow of window frames.',
        demo: 'ui-sg-glass',
    },
];

const tones = [
    ['neutral', 'Blue: ordinary and reversible'],
    ['go', 'Green: start, go, create'],
    ['caution', 'Amber: softer high impact, like Log Off'],
    ['danger', 'Red: high impact and destructive, like Close or delete'],
] as const;

export const Principles: StoryObj = {
    render: () => (
        <Page title="Luna with a glass finish">
            <Section title="The rules">
                <ul>
                    {rules.map((rule) => (
                        <li key={rule}>{rule}</li>
                    ))}
                </ul>
            </Section>
            <Section title="The materials">
                <div className="ui-sg-materials">
                    {materials.map((m) => (
                        <div key={m.name} className="ui-sg-material">
                            {m.demo === 'ui-sg-glass' ? (
                                <div className="ui-sg-material-demo ui-sg-busy">
                                    <span className="ui-sg-glass ui-sg-material-chip">
                                        {m.name}
                                    </span>
                                </div>
                            ) : (
                                <div
                                    className={`ui-sg-material-demo ${m.demo}`}
                                >
                                    {m.name}
                                </div>
                            )}
                            <strong>{m.what}</strong>
                            <span>{m.where}</span>
                        </div>
                    ))}
                </div>
            </Section>
            <Section title="The severity code">
                <div className="ui-sg-materials">
                    {tones.map(([tone, meaning]) => (
                        <div key={tone} className="ui-sg-material">
                            <span
                                className="ui-sg-sample"
                                style={{
                                    background: `var(--ui-tone-${tone})`,
                                    color: `var(--ui-tone-${tone}-text)`,
                                }}
                            >
                                tone="{tone}"
                            </span>
                            <span>{meaning}</span>
                        </div>
                    ))}
                </div>
            </Section>
        </Page>
    ),
};
