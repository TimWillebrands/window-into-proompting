import type { Meta, StoryObj } from '@storybook/react-vite';
import { expect, userEvent, within } from 'storybook/test';
import {
    noir,
    reducedMotion,
    reducedTransparency,
    stage,
} from '../../../story-kit/story-kit';
import { Icon } from '../../icons';
import { type TreeItem, TreeView } from './TreeView';

const folder = <Icon name="folder" />;
const open = <Icon name="folder-open" />;

const items: TreeItem[] = [
    {
        id: 'world',
        label: 'The World',
        icon: <Icon name="world" />,
        children: [
            {
                id: 'places',
                label: 'Places',
                icon: folder,
                openIcon: open,
                children: [
                    {
                        id: 'cafe',
                        label: 'Corner Café',
                        icon: <Icon name="coffee" />,
                    },
                    {
                        id: 'park',
                        label: 'The Park',
                        icon: <Icon name="tree" />,
                    },
                    {
                        id: 'ruin',
                        label: 'Old Ruin',
                        icon: <Icon name="ruin" />,
                        disabled: true,
                    },
                ],
            },
            {
                id: 'personas',
                label: 'Personas',
                icon: folder,
                openIcon: open,
                children: [
                    {
                        id: 'marigold',
                        label: 'Marigold',
                        icon: <Icon name="user" />,
                    },
                    { id: 'basil', label: 'Basil', icon: <Icon name="user" /> },
                ],
            },
            {
                id: 'company',
                label: 'The Company',
                icon: folder,
                openIcon: open,
                children: [
                    { id: 'cdu', label: 'CDU', icon: <Icon name="cdu" /> },
                ],
            },
            { id: 'board', label: 'Board', icon: <Icon name="board" /> },
        ],
    },
];

const meta = {
    title: 'Views/TreeView',
    component: TreeView,
    decorators: [stage],
    args: { items, label: 'Folders' },
} satisfies Meta<typeof TreeView>;

export default meta;

type Story = StoryObj<typeof meta>;

export const Default: Story = {
    render: (args) => (
        <TreeView
            {...args}
            defaultExpanded={['world', 'places']}
            defaultSelected="cafe"
            className="ui-story-w-200 ui-story-h-260"
        />
    ),
};

export const Focused: Story = {
    render: (args) => (
        <TreeView
            {...args}
            defaultExpanded={['world', 'places', 'personas']}
            defaultSelected="park"
            className="ui-story-w-200 ui-story-h-260"
            ref={(node) => {
                node?.querySelector<HTMLElement>(
                    '[data-tree-item="park"]',
                )?.focus();
            }}
        />
    ),
};

export const WithoutLines: Story = {
    render: (args) => (
        <TreeView
            {...args}
            lines={false}
            defaultExpanded={['world', 'places']}
            defaultSelected="cafe"
            className="ui-story-w-200 ui-story-h-260"
        />
    ),
};

export const Noir: Story = { ...Default, ...noir };
export const ReducedMotion: Story = { ...Default, ...reducedMotion };
export const ReducedTransparency: Story = {
    ...Default,
    ...reducedTransparency,
};

export const Keyboard: Story = {
    render: (args) => (
        <TreeView
            {...args}
            defaultExpanded={['world']}
            className="ui-story-w-200 ui-story-h-260"
        />
    ),
    play: async ({ canvasElement }) => {
        const node = (id: string) =>
            canvasElement.querySelector(
                `[data-tree-item="${id}"]`,
            ) as HTMLElement;
        await userEvent.click(within(node('world')).getByText('The World'));
        await expect(node('world')).toHaveFocus();
        await userEvent.keyboard('{ArrowDown}');
        await expect(node('places')).toHaveFocus();
        await expect(node('places')).toHaveAttribute('aria-expanded', 'false');
        await userEvent.keyboard('{ArrowRight}');
        await expect(node('places')).toHaveAttribute('aria-expanded', 'true');
        await userEvent.keyboard('{ArrowRight}');
        await expect(node('cafe')).toHaveFocus();
        await expect(node('cafe')).toHaveAttribute('aria-selected', 'true');
        await userEvent.keyboard('{ArrowLeft}');
        await expect(node('places')).toHaveFocus();
        await userEvent.keyboard('{ArrowLeft}');
        await expect(node('places')).toHaveAttribute('aria-expanded', 'false');
        await userEvent.keyboard('{End}');
        await expect(node('board')).toHaveFocus();
    },
};
