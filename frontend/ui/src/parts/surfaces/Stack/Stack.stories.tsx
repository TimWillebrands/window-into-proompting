import type { Meta, StoryObj } from '@storybook/react-vite';
import type { CSSProperties } from 'react';
import {
    noir,
    reducedMotion,
    reducedTransparency,
    States,
    stage,
} from '../../../story-kit/story-kit';
import { CommandButton } from '../../controls/CommandButton/CommandButton';
import { FieldRow } from '../../controls/FieldRow/FieldRow';
import { Text } from '../../controls/Text/Text';
import { TextBox } from '../../controls/TextBox/TextBox';
import { Icon } from '../../icons';
import { type Space, Stack } from './Stack';

const meta = {
    title: 'Surfaces/Stack',
    component: Stack,
    decorators: [stage],
} satisfies Meta<typeof Stack>;

export default meta;

type Story = StoryObj<typeof meta>;

const buttons = (
    <>
        <CommandButton>OK</CommandButton>
        <CommandButton>Cancel</CommandButton>
        <CommandButton>Apply</CommandButton>
    </>
);

const gaps: Space[] = ['none', 'xxs', 'xs', 'sm', 'md', 'lg', 'xl'];

export const Default: Story = {
    render: () => (
        <States
            items={[
                [
                    'column',
                    <Stack key="column" gap="sm">
                        {buttons}
                    </Stack>,
                ],
                [
                    'row',
                    <Stack key="row" direction="row" gap="sm">
                        {buttons}
                    </Stack>,
                ],
            ]}
        />
    ),
};

export const Gaps: Story = {
    render: () => (
        <States
            items={gaps.map((gap) => [
                gap,
                <Stack key={gap} direction="row" gap={gap}>
                    {buttons}
                </Stack>,
            ])}
        />
    ),
};

const tall: CSSProperties = { width: 320, height: 72 };

export const AlignAndJustify: Story = {
    render: () => (
        <States
            items={[
                [
                    'align="center"',
                    <Stack key="a" direction="row" gap="sm" align="center">
                        <Icon name="info" size={32} />
                        <Text>Centred against the icon</Text>
                    </Stack>,
                ],
                [
                    'align="baseline"',
                    <Stack key="b" direction="row" gap="sm" align="baseline">
                        <Text size="title">Title</Text>
                        <Text tone="muted">sits on the same line</Text>
                    </Stack>,
                ],
                [
                    'justify="end"',
                    <Stack
                        key="c"
                        direction="row"
                        gap="sm"
                        justify="end"
                        style={tall}
                    >
                        {buttons}
                    </Stack>,
                ],
                [
                    'justify="between"',
                    <Stack
                        key="d"
                        direction="row"
                        justify="between"
                        align="center"
                        style={tall}
                    >
                        <Text>3 items</Text>
                        <CommandButton>Clear</CommandButton>
                    </Stack>,
                ],
            ]}
        />
    ),
};

export const WrapAndGrow: Story = {
    render: () => (
        <States
            items={[
                [
                    'wrap',
                    <Stack
                        key="wrap"
                        direction="row"
                        gap="xs"
                        wrap
                        style={{ width: 200 }}
                    >
                        {buttons}
                        {buttons}
                    </Stack>,
                ],
                [
                    'Stack.Item grow',
                    <Stack
                        key="grow"
                        direction="row"
                        gap="sm"
                        align="center"
                        style={{ width: 320 }}
                    >
                        <Icon name="search" />
                        <Stack.Item grow>
                            <Text>Takes the rest of the row</Text>
                        </Stack.Item>
                        <CommandButton>Find</CommandButton>
                    </Stack>,
                ],
            ]}
        />
    ),
};

export const Truncate: Story = {
    render: () => (
        <Stack direction="row" gap="xs" align="center" style={{ width: 220 }}>
            <Icon name="document" />
            <Stack.Item grow truncate>
                A very long file name that would push the row wider than its
                window.txt
            </Stack.Item>
            <Text tone="muted">2 KB</Text>
        </Stack>
    ),
};

export const PaddedAndInline: Story = {
    render: () => (
        <States
            items={[
                [
                    'pad="lg"',
                    <Stack key="pad" pad="lg" gap="sm">
                        {buttons}
                    </Stack>,
                ],
                [
                    'inline',
                    <Text key="inline">
                        Saved by{' '}
                        <Stack
                            as="span"
                            inline
                            direction="row"
                            gap="xxs"
                            align="center"
                        >
                            <Icon name="user" />
                            Ada
                        </Stack>{' '}
                        just now.
                    </Text>,
                ],
            ]}
        />
    ),
};

export const Elements: Story = {
    render: () => (
        <States
            items={[
                [
                    'as="form" noValidate',
                    <Stack
                        key="form"
                        as="form"
                        noValidate
                        gap="sm"
                        onSubmit={(event) => event.preventDefault()}
                        style={{ width: 320 }}
                    >
                        <FieldRow label="Name">
                            <TextBox required defaultValue="Ada" />
                        </FieldRow>
                        <Stack direction="row" justify="end">
                            <CommandButton isDefault type="submit">
                                Save
                            </CommandButton>
                        </Stack>
                    </Stack>,
                ],
                [
                    'as="dl"',
                    <Stack key="dl" as="dl" direction="row" wrap gap="lg">
                        {[
                            ['Book', '1,240¢'],
                            ['Cash', '310¢'],
                            ['Return', '+4.2%'],
                        ].map(([label, value]) => (
                            <Stack key={label} gap="xxs">
                                <Stack.Item as="dt">{label}</Stack.Item>
                                <Stack.Item as="dd">
                                    <Text strong>{value}</Text>
                                </Stack.Item>
                            </Stack>
                        ))}
                    </Stack>,
                ],
            ]}
        />
    ),
};

export const Noir: Story = { ...AlignAndJustify, ...noir };
export const ReducedMotion: Story = { ...Default, ...reducedMotion };
export const ReducedTransparency: Story = {
    ...Default,
    ...reducedTransparency,
};
