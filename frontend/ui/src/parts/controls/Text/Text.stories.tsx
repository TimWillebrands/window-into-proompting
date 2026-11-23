import type { Meta, StoryObj } from '@storybook/react-vite';
import {
    noir,
    reducedMotion,
    reducedTransparency,
    States,
    stage,
} from '../story-kit';
import { Text } from './Text';

const meta = {
    title: 'Controls/Text',
    component: Text,
    decorators: [stage],
} satisfies Meta<typeof Text>;

export default meta;

type Story = StoryObj<typeof meta>;

export const Default: Story = {
    render: () => (
        <States
            items={[
                ['inherit', <Text key="a">Ordinary chrome text</Text>],
                [
                    'reading',
                    <Text key="b" size="reading">
                        Something someone wrote, at reading size.
                    </Text>,
                ],
                [
                    'reading, their line breaks',
                    <Text key="b2" as="p" size="reading">
                        {
                            'We stayed on the roof.\nThe city sounds different at four.'
                        }
                    </Text>,
                ],
                [
                    'help (muted p)',
                    <Text key="b3" as="p" tone="muted">
                        Changes apply after you click Save.
                    </Text>,
                ],
                [
                    'title',
                    <Text key="c" as="h2" size="title">
                        Section title
                    </Text>,
                ],
                [
                    'display',
                    <Text key="d" as="h2" size="display">
                        Welcome
                    </Text>,
                ],
                [
                    'muted',
                    <Text key="e" tone="muted">
                        Last seen 5 minutes ago
                    </Text>,
                ],
                [
                    'neutral',
                    <Text key="f" tone="neutral" strong>
                        Model
                    </Text>,
                ],
                [
                    'go',
                    <Text key="g" tone="go" strong>
                        go
                    </Text>,
                ],
                [
                    'caution',
                    <Text key="h" tone="caution" strong>
                        decline
                    </Text>,
                ],
                [
                    'danger',
                    <Text key="i" tone="danger">
                        Could not send the message.
                    </Text>,
                ],
                [
                    'italic',
                    <Text key="j" italic tone="muted">
                        out for a walk
                    </Text>,
                ],
                [
                    'mono',
                    <Text key="k" mono>
                        salience=0.42
                    </Text>,
                ],
            ]}
        />
    ),
};

export const Noir: Story = { ...Default, parameters: noir };
export const ReducedMotion: Story = { ...Default, parameters: reducedMotion };
export const ReducedTransparency: Story = {
    ...Default,
    parameters: reducedTransparency,
};
