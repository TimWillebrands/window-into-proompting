import type { Meta, StoryObj } from '@storybook/react-vite';
import { fn } from 'storybook/test';
import {
    noir,
    reducedMotion,
    reducedTransparency,
    stage,
} from '../../../story-kit/story-kit';
import { Icon } from '../../icons';
import { CategoryItem, CategoryView } from './CategoryView';

const meta = {
    title: 'Views/CategoryView',
    component: CategoryView,
    decorators: [stage],
    parameters: { stage: { width: 'wide' } },
    args: { title: 'Pick a category' },
} satisfies Meta<typeof CategoryView>;

export default meta;

type Story = StoryObj<typeof meta>;

const items = (
    <>
        <CategoryItem
            icon={<Icon name="models" size={48} />}
            title="Language Models"
            description="Which models there are and who speaks through which"
            onClick={fn()}
        />
        <CategoryItem
            icon={<Icon name="users" size={48} />}
            title="People"
            description="Who lives here and what they are to each other"
            onClick={fn()}
        />
        <CategoryItem
            icon={<Icon name="clock" size={48} />}
            title="Date and Time"
            description="How fast the World clock runs"
            onClick={fn()}
        />
    </>
);

export const Default: Story = {
    render: (args) => <CategoryView {...args}>{items}</CategoryView>,
};

export const AllStates: Story = {
    render: () => (
        <CategoryView title="Pick a category">
            <CategoryItem
                icon={<Icon name="models" size={48} />}
                title="Default"
                description="Resting"
            />
            <CategoryItem
                icon={<Icon name="models" size={48} />}
                title="Hover"
                description="Link lights up"
                data-ui-preview="hover"
            />
            <CategoryItem
                icon={<Icon name="models" size={48} />}
                title="Focus"
                description="Focus ring"
                data-ui-preview="focus"
            />
            <CategoryItem
                icon={<Icon name="models" size={48} />}
                title="Disabled"
                description="Not available"
                disabled
            />
        </CategoryView>
    ),
};

export const Untitled: Story = {
    render: () => <CategoryView>{items}</CategoryView>,
};

export const Noir: Story = {
    ...Default,
    ...noir,
};
export const ReducedMotion: Story = {
    ...Default,
    ...reducedMotion,
};
export const ReducedTransparency: Story = {
    ...Default,
    ...reducedTransparency,
};
