import type { Meta, StoryObj } from '@storybook/react-vite';
import { Page, SchemePair, TokenName } from './Showcase';

const meta: Meta = {
    title: 'Styleguide/Elevation',
    parameters: { layout: 'padded' },
};

export default meta;

const shadows = [
    '--ui-shadow-control',
    '--ui-shadow-window-inactive',
    '--ui-shadow-window',
    '--ui-shadow-float',
    '--ui-glass-shadow',
];

export const Shadows: StoryObj = {
    render: () => (
        <Page
            title="Elevation"
            intro="Few levels, soft and Luna-tinted. A window lifts when it becomes active; floating chrome sits highest."
        >
            <SchemePair>
                {() => (
                    <div className="ui-sg-elevations">
                        {shadows.map((token) => (
                            <div
                                key={token}
                                className="ui-sg-elevation"
                                style={{ boxShadow: `var(${token})` }}
                            >
                                <TokenName name={token} />
                            </div>
                        ))}
                    </div>
                )}
            </SchemePair>
        </Page>
    ),
};
