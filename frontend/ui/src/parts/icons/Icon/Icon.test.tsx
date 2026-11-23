import { renderToStaticMarkup } from 'react-dom/server';
import { describe, expect, it } from 'vitest';
import registry from '../../../../icons/icons.json';
import { Icon } from './Icon';
import { iconNames, iconSizes, icons } from './registry.gen';

describe('Icon', () => {
    it('is decorative unless it has a label', () => {
        const html = renderToStaticMarkup(<Icon name="home" />);
        expect(html).toContain('alt=""');
        expect(html).toContain('aria-hidden="true"');
        expect(html).toContain('width="16"');
    });

    it('names itself when labelled', () => {
        const html = renderToStaticMarkup(
            <Icon name="search" size={32} label="Search" />,
        );
        expect(html).toContain('alt="Search"');
        expect(html).not.toContain('aria-hidden');
        expect(html).toContain('width="32"');
    });

    it('marks the tier so pixel art renders crisp', () => {
        expect(renderToStaticMarkup(<Icon name="home" />)).toContain(
            'data-tier="pixel"',
        );
        expect(renderToStaticMarkup(<Icon name="world" />)).toContain(
            'data-tier="real"',
        );
    });

    it('lets real art win over pixel per size', () => {
        expect(icons.world.sizes[48].tier).toBe('real');
        expect(icons.board.sizes[32].tier).toBe('real');
        expect(icons.board.sizes[48].tier).toBe('pixel');
    });

    it('has art at every size for every name', () => {
        for (const name of iconNames)
            for (const size of iconSizes)
                expect(icons[name].sizes[size].src, `${name}-${size}`).toMatch(
                    new RegExp(`${name}-${size}`),
                );
    });

    it('matches the registry file the pipeline wrote', () => {
        expect(registry.icons.map((icon) => icon.name)).toEqual([...iconNames]);
        for (const icon of registry.icons)
            expect(icons[icon.name as (typeof iconNames)[number]].tier).toBe(
                icon.tier,
            );
    });
});
