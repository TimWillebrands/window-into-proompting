import type { Meta, StoryObj } from '@storybook/react-vite';
import { Page, Section } from '../../../styleguide/Showcase';
import { Icon } from './Icon';
import { type IconName, iconNames, iconSizes, icons } from './registry.gen';
import './stories.css';

const meta: Meta = {
    title: 'Icons/Catalogue',
    parameters: { layout: 'padded' },
};

export default meta;

function note(name: IconName) {
    const entry = icons[name];
    if (entry.tier === 'pixel') return null;
    const scaled = iconSizes.filter((size) => entry.sizes[size].scaled);
    const pixel = iconSizes.filter(
        (size) => entry.sizes[size].tier === 'pixel',
    );
    const parts = [
        scaled.length ? `${scaled.join(', ')} scaled down` : '',
        pixel.length ? `${pixel.join(', ')} still pixel` : '',
    ].filter(Boolean);
    return parts.length ? parts.join(' · ') : null;
}

function Card({ name }: { name: IconName }) {
    const entry = icons[name];
    const extra = note(name);
    return (
        <figure className="ui-sg-icon-card" data-tier={entry.tier}>
            <div className="ui-sg-icon-sizes">
                {iconSizes.map((size) => (
                    <span
                        key={size}
                        className="ui-sg-icon-cell"
                        data-tier={entry.sizes[size].tier}
                    >
                        <Icon name={name} size={size} />
                    </span>
                ))}
            </div>
            <figcaption>
                <code>{name}</code>
                <span className="ui-sg-icon-tier" data-tier={entry.tier}>
                    {entry.tier}
                </span>
                {entry.tier === 'pixel' && (
                    <span className="ui-sg-icon-emoji">{entry.source}</span>
                )}
                {extra && <span className="ui-sg-icon-note">{extra}</span>}
            </figcaption>
        </figure>
    );
}

function Grid({ names }: { names: IconName[] }) {
    return (
        <div className="ui-sg-icon-grid">
            {names.map((name) => (
                <Card key={name} name={name} />
            ))}
        </div>
    );
}

const real = iconNames.filter((name) => icons[name].tier === 'real');
const pixel = iconNames.filter((name) => icons[name].tier === 'pixel');

export const All: StoryObj = {
    render: () => (
        <Page
            title="Icon catalogue"
            intro={`${iconNames.length} names: ${real.length} with real art, ${pixel.length} still pixel. Every pixel icon is waiting for real XP-style art; drawing it replaces the pixel one by name, with no call site changing.`}
        >
            <Section title={`Real art (${real.length})`}>
                <Grid names={real} />
            </Section>
            <Section title={`Pixel, needs real art (${pixel.length})`}>
                <Grid names={pixel} />
            </Section>
        </Page>
    ),
};
