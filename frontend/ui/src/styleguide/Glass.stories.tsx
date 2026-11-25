import type { Meta, StoryObj } from '@storybook/react-vite';
import { schemes } from '../scheme';
import { Page, Section, schemeNames } from './Showcase';

const meta: Meta = {
    title: 'Styleguide/Glass',
    parameters: { layout: 'padded' },
};

export default meta;

const fallbacks = [
    { title: 'Normal', attributes: {} },
    {
        title: 'Reduce Transparency, or no backdrop-filter',
        attributes: { 'data-ui-reduce-transparency': '' },
    },
    {
        title: 'Increase Contrast',
        attributes: { 'data-ui-contrast': 'more' },
    },
];

function Tiles() {
    return (
        <>
            <div className="ui-sg-glass ui-sg-glass-tile">
                <strong>Regular</strong>
                <span>Menus, balloons, banners: text sits on it.</span>
            </div>
            <div className="ui-sg-glass-clear ui-sg-glass-tile">
                <strong>Clear</strong>
                <span>The lightest tint, for thin edges over the desktop.</span>
            </div>
            <div className="ui-sg-glass-canvas ui-sg-glass-tile">
                <strong>Over a canvas</strong>
                <span>Lighter blur over Places and Figures.</span>
            </div>
        </>
    );
}

export const Strengths: StoryObj = {
    render: () => (
        <Page
            title="Glass"
            intro="One material, only on floating chrome, always Luna-tinted. Never on content."
        >
            {schemes.map((scheme) => (
                <Section key={scheme} title={schemeNames[scheme]}>
                    <div
                        data-ui-scheme={scheme}
                        className="ui-sg-glass-stage ui-sg-busy"
                    >
                        <Tiles />
                    </div>
                </Section>
            ))}
        </Page>
    ),
};

export const Fallbacks: StoryObj = {
    render: () => (
        <Page
            title="Glass fallbacks"
            intro="The tokens fall back to solid Luna on their own: Reduce Transparency and a missing backdrop-filter give the same opaque fill, Increase Contrast adds strong edges. Here each one is forced on a subtree with the data attributes; the toolbar forces them on the whole page."
        >
            {fallbacks.map((fallback) => (
                <Section key={fallback.title} title={fallback.title}>
                    {schemes.map((scheme) => (
                        <div
                            key={scheme}
                            data-ui-scheme={scheme}
                            className="ui-sg-glass-stage ui-sg-busy"
                            {...fallback.attributes}
                        >
                            <Tiles />
                        </div>
                    ))}
                </Section>
            ))}
        </Page>
    ),
};
