import type { Meta, StoryObj } from '@storybook/react-vite';
import { useState } from 'react';
import { expect, userEvent, within } from 'storybook/test';
import { Icon, type IconName } from '../../icons';
import { noir, reducedMotion, reducedTransparency, stage } from '../story-kit';
import {
    ListView,
    type ListViewItem,
    type ListViewMode,
    type ListViewSort,
} from './ListView';

const rows: ReadonlyArray<{
    id: string;
    name: string;
    icon: IconName;
    kind: string;
    size: number;
    modified: string;
    disabled?: boolean;
}> = [
    {
        id: 'board',
        name: 'Board',
        icon: 'board',
        kind: 'App',
        size: 12,
        modified: '21:04',
    },
    {
        id: 'diary',
        name: 'Diary',
        icon: 'diary',
        kind: 'App',
        size: 48,
        modified: '20:51',
    },
    {
        id: 'messenger',
        name: 'Messenger',
        icon: 'messenger',
        kind: 'App',
        size: 7,
        modified: '20:12',
    },
    {
        id: 'places',
        name: 'Places',
        icon: 'places',
        kind: 'App',
        size: 230,
        modified: '19:40',
    },
    {
        id: 'personas',
        name: 'Personas',
        icon: 'personas',
        kind: 'Folder',
        size: 64,
        modified: '18:02',
    },
    {
        id: 'jobs',
        name: 'Jobs',
        icon: 'jobs',
        kind: 'Folder',
        size: 3,
        modified: '17:30',
    },
    {
        id: 'cdu',
        name: 'CDU',
        icon: 'cdu',
        kind: 'Service',
        size: 990,
        modified: '09:15',
        disabled: true,
    },
];

const columns = [
    { key: 'name', header: 'Name', sortable: true },
    { key: 'kind', header: 'Type', sortable: true },
    {
        key: 'size',
        header: 'Size',
        align: 'end' as const,
        sortable: true,
        width: 'minmax(64px, 0.7fr)',
    },
    { key: 'modified', header: 'Modified', sortable: true },
];

const toItems = (sort?: ListViewSort): ListViewItem[] => {
    const sorted = [...rows];
    if (sort) {
        const key =
            sort.key === 'name'
                ? 'name'
                : (sort.key as 'kind' | 'size' | 'modified');
        sorted.sort((a, b) => (a[key] < b[key] ? -1 : a[key] > b[key] ? 1 : 0));
        if (sort.direction === 'descending') sorted.reverse();
    }
    return sorted.map((row) => ({
        id: row.id,
        label: row.name,
        icon: <Icon name={row.icon} />,
        largeIcon: <Icon name={row.icon} size={32} />,
        details: [row.kind, `${row.size} KB`],
        cells: {
            kind: row.kind,
            size: `${row.size} KB`,
            modified: row.modified,
        },
        disabled: row.disabled,
    }));
};

function Explorer({
    view,
    selectionMode = 'multiple',
    focus = false,
}: {
    view: ListViewMode;
    selectionMode?: 'none' | 'single' | 'multiple';
    focus?: boolean;
}) {
    const [sort, setSort] = useState<ListViewSort>({
        key: 'name',
        direction: 'ascending',
    });
    return (
        <ListView
            label="Programs"
            view={view}
            items={toItems(sort)}
            columns={columns}
            selectionMode={selectionMode}
            defaultSelected={['diary']}
            sort={sort}
            onSortChange={setSort}
            className={
                view === 'tiles'
                    ? 'ui-story-w-560 ui-story-h-260'
                    : 'ui-story-w-560 ui-story-h-200'
            }
            data-ui-preview={focus ? 'focus' : undefined}
        />
    );
}

const meta = {
    title: 'Controls/ListView',
    component: ListView,
    decorators: [stage],
    args: { items: [], label: 'Programs' },
} satisfies Meta<typeof ListView>;

export default meta;

type Story = StoryObj<typeof meta>;

export const Default: Story = { render: () => <Explorer view="details" /> };
export const DetailsFocused: Story = {
    render: () => <Explorer view="details" focus />,
};
export const Tiles: Story = { render: () => <Explorer view="tiles" /> };
export const Icons: Story = { render: () => <Explorer view="icons" focus /> };
export const List: Story = { render: () => <Explorer view="list" focus /> };
export const SingleSelection: Story = {
    render: () => <Explorer view="details" selectionMode="single" />,
};
export const Empty: Story = {
    render: () => (
        <ListView
            label="Search results"
            items={[]}
            columns={columns}
            empty="There are no items to show in this view."
            className="ui-story-w-560 ui-story-h-200"
        />
    ),
};

const ledger: ListViewItem[] = [
    {
        id: 'wage',
        label: 'Wage from The Works',
        icon: <Icon name="coin" />,
        tone: 'go',
        cells: {
            amount: '+120',
            note: 'Two shifts on the night desk, paid at the end of the week',
        },
    },
    {
        id: 'coffee',
        label: 'Coffee at the corner',
        icon: <Icon name="coffee" />,
        tone: 'danger',
        cells: { amount: '−4', note: 'Flat white' },
    },
    {
        id: 'rent',
        label: 'Rent',
        icon: <Icon name="home" />,
        tone: 'caution',
        cells: { amount: '−60', note: 'Due Friday, not paid yet' },
    },
    {
        id: 'gift',
        label: 'Gift from Basil',
        icon: <Icon name="gift" />,
        tone: 'neutral',
        cells: { amount: '0', note: 'A lantern' },
    },
];

export const TonesAndWrap: Story = {
    render: () => (
        <ListView
            label="Wallet"
            items={ledger}
            defaultSelected={['coffee']}
            columns={[
                { key: 'what', header: 'What', width: 'minmax(140px, 1fr)' },
                {
                    key: 'amount',
                    header: 'Amount',
                    align: 'end',
                    width: '72px',
                },
                {
                    key: 'note',
                    header: 'Note',
                    wrap: true,
                    width: 'minmax(120px, 1.4fr)',
                },
            ]}
            className="ui-story-w-560 ui-story-h-200"
        />
    ),
};

function Launcher({ compact = false }: { compact?: boolean }) {
    const [opened, setOpened] = useState('nothing yet');
    return (
        <div className="ui-story-column">
            <ListView
                label="Places"
                view="tiles"
                tileSize={compact ? 'compact' : 'regular'}
                activateOn="single"
                onActivate={setOpened}
                items={rows.map((row) => ({
                    id: row.id,
                    label: row.name,
                    icon: <Icon name={row.icon} />,
                    largeIcon: (
                        <Icon name={row.icon} size={compact ? 32 : 48} />
                    ),
                    details: [row.kind],
                    disabled: row.disabled,
                }))}
                className={
                    compact
                        ? 'ui-story-w-200 ui-story-h-260'
                        : 'ui-story-w-560 ui-story-h-260'
                }
            />
            <span data-story="opened">Opened: {opened}</span>
        </div>
    );
}

export const SingleClickOpen: Story = {
    render: () => <Launcher />,
    play: async ({ canvasElement }) => {
        const canvas = within(canvasElement);
        await userEvent.click(canvas.getByRole('option', { name: /Diary/ }));
        await expect(canvas.getByText('Opened: diary')).toBeInTheDocument();
        await userEvent.keyboard('{Home}{Enter}');
        await expect(canvas.getByText('Opened: board')).toBeInTheDocument();
    },
};

export const CompactTiles: Story = { render: () => <Launcher compact /> };

export const Noir: Story = { ...DetailsFocused, parameters: noir };
export const NoirTones: Story = { ...TonesAndWrap, parameters: noir };
export const NoirCompactTiles: Story = { ...CompactTiles, parameters: noir };
export const NoirTiles: Story = { ...Tiles, parameters: noir };
export const ReducedMotion: Story = { ...Default, parameters: reducedMotion };
export const ReducedTransparency: Story = {
    ...Default,
    parameters: reducedTransparency,
};

export const Keyboard: Story = {
    render: () => <Explorer view="details" />,
    play: async ({ canvasElement }) => {
        const grid = within(canvasElement);
        const rows = grid.getAllByRole('row').slice(1);
        const diary = rows[2];
        await userEvent.click(diary);
        await expect(diary).toHaveAttribute('aria-selected', 'true');
        await userEvent.keyboard('{ArrowDown}');
        await expect(rows[3]).toHaveFocus();
        await expect(rows[3]).toHaveAttribute('aria-selected', 'true');
        await expect(diary).toHaveAttribute('aria-selected', 'false');
        await userEvent.keyboard('{Shift>}{ArrowDown}{/Shift}');
        await expect(rows[3]).toHaveAttribute('aria-selected', 'true');
        await expect(rows[4]).toHaveAttribute('aria-selected', 'true');
        await userEvent.keyboard('{ArrowUp}{ArrowUp}{ArrowUp}');
        await expect(rows[0]).toHaveFocus();
        await userEvent.click(grid.getByRole('button', { name: /Size/ }));
        await expect(
            grid.getByRole('columnheader', { name: /Size/ }),
        ).toHaveAttribute('aria-sort', 'ascending');
    },
};
