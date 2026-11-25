import type { Meta, StoryObj } from '@storybook/react-vite';
import {
    noir,
    picture,
    reducedMotion,
    reducedTransparency,
    States,
    stage,
} from '../../../story-kit/story-kit';
import { UserPicture } from '../UserPicture/UserPicture';
import { PictureStack } from './PictureStack';

const people = (size: 16 | 24 | 32, hues: number[]) =>
    hues.map((hue) => ({
        key: String(hue),
        picture: <UserPicture src={picture(hue)} size={size} />,
    }));

const meta = {
    title: 'Controls/PictureStack',
    component: PictureStack,
    decorators: [stage],
    args: {
        pictures: people(24, [210, 30, 120]),
        label: 'Ada, Grace and Linus',
    },
} satisfies Meta<typeof PictureStack>;

export default meta;

type Story = StoryObj<typeof meta>;

export const Default: Story = {};

export const AllStates: Story = {
    render: () => (
        <States
            items={[
                [
                    'two',
                    <PictureStack
                        key="two"
                        label="Ada and Grace"
                        pictures={people(24, [210, 30])}
                    />,
                ],
                [
                    'three',
                    <PictureStack
                        key="three"
                        label="Ada, Grace and Linus"
                        pictures={people(24, [210, 30, 120])}
                    />,
                ],
                [
                    'more than max',
                    <PictureStack
                        key="more"
                        label="Six people"
                        pictures={people(24, [210, 30, 120, 300, 60, 180])}
                    />,
                ],
                [
                    '16',
                    <PictureStack
                        key="16"
                        label="Five people"
                        size={16}
                        pictures={people(16, [210, 30, 120, 300, 60])}
                    />,
                ],
                [
                    '32, max 4',
                    <PictureStack
                        key="32"
                        label="Five people"
                        size={32}
                        max={4}
                        pictures={people(32, [210, 30, 120, 300, 60])}
                    />,
                ],
                [
                    'placeholders',
                    <PictureStack
                        key="empty"
                        label="Two people"
                        pictures={[
                            { key: 'a', picture: <UserPicture size={24} /> },
                            { key: 'b', picture: <UserPicture size={24} /> },
                        ]}
                    />,
                ],
            ]}
        />
    ),
};

export const Noir: Story = { ...AllStates, ...noir };
export const ReducedMotion: Story = { ...AllStates, ...reducedMotion };
export const ReducedTransparency: Story = {
    ...AllStates,
    ...reducedTransparency,
};
