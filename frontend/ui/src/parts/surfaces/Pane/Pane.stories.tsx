import type { Meta, StoryObj } from '@storybook/react-vite';
import type { CSSProperties } from 'react';
import {
    noir,
    reducedMotion,
    reducedTransparency,
    States,
    stage,
} from '../../../story-kit/story-kit';
import { Text } from '../../controls/Text/Text';
import { Toolbar, ToolbarButton } from '../../controls/Toolbar/Toolbar';
import { Icon } from '../../icons';
import { Stack } from '../Stack/Stack';
import { Pane } from './Pane';

const meta = {
    title: 'Surfaces/Pane',
    component: Pane,
    decorators: [stage],
} satisfies Meta<typeof Pane>;

export default meta;

type Story = StoryObj<typeof meta>;

const box: CSSProperties = { width: 320, height: 200 };

const entries = Array.from(
    { length: 10 },
    (_, index) =>
        `Entry ${index + 1}. Walked to the market; the baker remembered my name.`,
);

const page = (
    <Stack gap="md">
        {entries.map((entry) => (
            <Text key={entry} as="p" size="reading">
                {entry}
            </Text>
        ))}
    </Stack>
);

export const Default: Story = {
    render: () => (
        <Stack style={box}>
            <Toolbar aria-label="Diary">
                <ToolbarButton icon={<Icon name="new" />}>New</ToolbarButton>
            </Toolbar>
            <Pane pad="lg" tabIndex={0} role="region" aria-label="Diary">
                {page}
            </Pane>
        </Stack>
    ),
};

export const Surfaces: Story = {
    render: () => (
        <States
            items={[
                [
                    'plain',
                    <Stack key="plain" style={box}>
                        <Pane pad="lg" tabIndex={0} aria-label="Plain">
                            {page}
                        </Pane>
                    </Stack>,
                ],
                [
                    'sunken',
                    <Stack key="sunken" style={box}>
                        <Pane
                            surface="sunken"
                            pad="md"
                            text="reading"
                            tabIndex={0}
                            aria-label="Sunken"
                        >
                            {page}
                        </Pane>
                    </Stack>,
                ],
                [
                    'side by side',
                    <Stack key="row" direction="row" gap="md" style={box}>
                        <Pane
                            surface="sunken"
                            pad="sm"
                            tabIndex={0}
                            aria-label="Left"
                        >
                            {page}
                        </Pane>
                        <Pane
                            surface="sunken"
                            pad="sm"
                            tabIndex={0}
                            aria-label="Right"
                        >
                            {page}
                        </Pane>
                    </Stack>,
                ],
            ]}
        />
    ),
};

export const Padding: Story = {
    render: () => (
        <States
            items={(['none', 'xs', 'md', 'xl'] as const).map((pad) => [
                pad,
                <Stack key={pad} style={{ width: 240, height: 64 }}>
                    <Pane surface="sunken" pad={pad}>
                        <Text>pad=&quot;{pad}&quot;</Text>
                    </Pane>
                </Stack>,
            ])}
        />
    ),
};

export const Focus: Story = {
    render: () => (
        <Stack style={box}>
            <Pane
                surface="sunken"
                pad="md"
                tabIndex={0}
                aria-label="Focused pane"
                autoFocus
            >
                {page}
            </Pane>
        </Stack>
    ),
};

export const Noir: Story = { ...Surfaces, ...noir };
export const ReducedMotion: Story = { ...Default, ...reducedMotion };
export const ReducedTransparency: Story = {
    ...Default,
    ...reducedTransparency,
};
