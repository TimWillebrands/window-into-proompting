import type { Meta, StoryObj } from '@storybook/react-vite';
import { ListView } from '../ListView/ListView';
import { noir, reducedMotion, reducedTransparency, stage } from '../story-kit';
import { Text } from '../Text/Text';
import { SunkenField } from './SunkenField';

const meta = {
    title: 'Controls/SunkenField',
    component: SunkenField,
    decorators: [stage],
} satisfies Meta<typeof SunkenField>;

export default meta;

type Story = StoryObj<typeof meta>;

export const Default: Story = {
    render: () => (
        <div className="ui-story-row">
            <SunkenField
                role="region"
                aria-label="Diary"
                tabIndex={0}
                text="reading"
                className="ui-story-w-320 ui-story-h-200"
            >
                <Text as="p" size="reading">
                    {
                        'Walked to the market. The baker remembered my name.\nRain all day after that; stayed in with a book about lighthouses and the people who kept them, alone for months on end.'
                    }
                </Text>
            </SunkenField>
            <ListView
                label="A ListView, for comparison"
                className="ui-story-w-200 ui-story-h-200"
                view="list"
                items={[
                    { id: 'a', label: 'Ada' },
                    { id: 'b', label: 'Grace' },
                ]}
            />
        </div>
    ),
};

export const Empty: Story = {
    render: () => (
        <SunkenField className="ui-story-w-320 ui-story-h-200">
            <Text as="p" tone="muted">
                Nothing written yet.
            </Text>
        </SunkenField>
    ),
};

export const Noir: Story = { ...Default, parameters: noir };
export const ReducedMotion: Story = { ...Default, parameters: reducedMotion };
export const ReducedTransparency: Story = {
    ...Default,
    parameters: reducedTransparency,
};
