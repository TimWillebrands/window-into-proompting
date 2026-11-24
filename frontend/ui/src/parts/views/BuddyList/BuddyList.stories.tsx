import type { Meta, StoryObj } from '@storybook/react-vite';
import { useState } from 'react';
import { expect, fn, userEvent, within } from 'storybook/test';
import {
    noir,
    picture,
    reducedMotion,
    reducedTransparency,
    stage,
} from '../../../story-kit/story-kit';
import { PictureStack } from '../../controls/PictureStack/PictureStack';
import { Presence } from '../../controls/Presence/Presence';
import { Text } from '../../controls/Text/Text';
import { UserPicture } from '../../controls/UserPicture/UserPicture';
import { SunkenField } from '../../surfaces/SunkenField/SunkenField';
import { BuddyList, type BuddyListGroup } from './BuddyList';

const buddy = (
    id: string,
    name: string,
    status: 'online' | 'busy' | 'away' | 'offline',
    note?: string,
) => ({
    id,
    title: `Send an instant message to ${name}`,
    picture: <Presence status={status} shape="buddy" />,
    name: (
        <>
            {name}
            {note && (
                <Text tone="muted" italic>
                    {' '}
                    - {note}
                </Text>
            )}
        </>
    ),
});

const pics = (hues: number[]) =>
    hues.map((hue) => ({
        key: String(hue),
        picture: <UserPicture src={picture(hue)} size={24} />,
    }));

const groups: BuddyListGroup[] = [
    {
        id: 'conversations',
        title: 'Conversations',
        items: [
            {
                id: 'c-ada',
                unread: true,
                picture: <UserPicture src={picture(210)} size={32} />,
                name: 'Ada',
                meta: '21:04',
                details: 'are you around tonight?',
            },
            {
                id: 'c-group',
                picture: (
                    <PictureStack
                        label="Ada, Linus and Hedy"
                        pictures={pics([210, 120, 300])}
                    />
                ),
                name: 'Ada, Linus and Hedy',
                meta: 'Tue',
                details: 'Linus: bring the good mugs',
            },
        ],
    },
    {
        id: 'online',
        title: 'Online',
        items: [
            buddy('ada', 'Ada', 'online', 'at the café'),
            buddy('linus', 'Linus', 'online'),
            buddy('hedy', 'Hedy', 'away', 'Asleep'),
        ],
    },
    {
        id: 'busy',
        title: 'Busy',
        items: [buddy('grace', 'Grace', 'busy')],
    },
    {
        id: 'afk',
        title: 'AFK',
        items: [],
        empty: 'Nobody is away.',
    },
];

function Contacts({
    initial = [],
    onActivate,
}: {
    initial?: string[];
    onActivate?: (id: string) => void;
}) {
    const [selected, setSelected] = useState<string[]>(initial);
    const [opened, setOpened] = useState<string>();
    return (
        <SunkenField className="ui-story-chat-w-260">
            <BuddyList
                groups={groups}
                selectionMode="multiple"
                selected={selected}
                onSelectionChange={setSelected}
                onActivate={(id) => {
                    setOpened(id);
                    onActivate?.(id);
                }}
                onItemContextMenu={fn()}
            >
                <Text as="p" tone="muted">
                    {selected.length} selected
                    {opened ? `, opened ${opened}` : ''}
                </Text>
            </BuddyList>
        </SunkenField>
    );
}

const meta = {
    title: 'Views/BuddyList',
    component: BuddyList,
    decorators: [stage],
    args: { groups },
} satisfies Meta<typeof BuddyList>;

export default meta;

type Story = StoryObj<typeof meta>;

export const Default: Story = { render: () => <Contacts /> };

export const MultipleSelected: Story = {
    render: () => <Contacts initial={['ada', 'linus']} />,
};

export const States: Story = {
    render: () => (
        <SunkenField className="ui-story-chat-w-260">
            <BuddyList
                selectionMode="multiple"
                defaultSelected={['sel']}
                groups={[
                    {
                        id: 'states',
                        title: 'States',
                        items: [
                            buddy('rest', 'At rest', 'online'),
                            buddy('hover', 'Hover', 'online'),
                            buddy('focus', 'Focus', 'away'),
                            buddy('sel', 'Selected', 'online'),
                            {
                                ...buddy('dim', 'Dimmed', 'offline'),
                                dimmed: true,
                            },
                            {
                                ...buddy('off', 'Opening…', 'online'),
                                disabled: true,
                            },
                        ],
                    },
                ]}
            />
        </SunkenField>
    ),
    play: async ({ canvasElement }) => {
        canvasElement
            .querySelector('[data-buddy-item="hover"]')
            ?.setAttribute('data-ui-preview', 'hover');
        canvasElement
            .querySelector('[data-buddy-item="focus"]')
            ?.setAttribute('data-ui-preview', 'focus');
    },
};

export const Keyboard: Story = {
    render: () => <Contacts />,
    play: async ({ canvasElement }) => {
        const list = within(canvasElement);
        const ada = list.getByRole('option', { name: /at the café/ });
        await userEvent.click(ada);
        await expect(ada).toHaveAttribute('aria-selected', 'true');
        await userEvent.keyboard('{Shift>}{ArrowDown}{/Shift}');
        const linus = list.getByRole('option', { name: 'Linus' });
        await expect(linus).toHaveFocus();
        await expect(ada).toHaveAttribute('aria-selected', 'true');
        await expect(linus).toHaveAttribute('aria-selected', 'true');
        const grace = list.getByRole('option', { name: 'Grace' });
        const user = userEvent.setup();
        await user.keyboard('{Control>}');
        await user.click(grace);
        await user.keyboard('{/Control}');
        await expect(grace).toHaveAttribute('aria-selected', 'true');
        await expect(ada).toHaveAttribute('aria-selected', 'true');
        await expect(list.getByText(/3 selected/)).toBeInTheDocument();
        await userEvent.keyboard('{ArrowUp}');
        await expect(
            list.getByRole('option', { name: /Asleep/ }),
        ).toHaveFocus();
        await userEvent.keyboard('{Enter}');
        await expect(list.getByText(/opened hedy/)).toBeInTheDocument();
        await userEvent.click(list.getByRole('button', { name: /Online/ }));
        await expect(list.queryByRole('option', { name: 'Linus' })).toBeNull();
    },
};

export const Noir: Story = {
    render: () => <Contacts initial={['ada', 'linus']} />,
    ...noir,
};
export const ReducedMotion: Story = { ...Default, ...reducedMotion };
export const ReducedTransparency: Story = {
    ...Default,
    ...reducedTransparency,
};
