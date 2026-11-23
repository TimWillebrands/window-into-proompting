import type { Meta, StoryObj } from '@storybook/react-vite';
import {
    noir,
    reducedMotion,
    reducedTransparency,
    States,
    stage,
} from '../../controls/story-kit';
import { picture } from '../story-kit';
import { UserPicture, userPictureSizes } from './UserPicture';

const meta = {
    title: 'Conversation/UserPicture',
    component: UserPicture,
    decorators: [stage],
    args: { src: picture(210), alt: 'Ada', size: 48 },
} satisfies Meta<typeof UserPicture>;

export default meta;

type Story = StoryObj<typeof meta>;

export const Default: Story = {
    render: () => (
        <States
            items={[
                [
                    'sizes',
                    <span key="a" className="ui-story-chat-row">
                        {userPictureSizes.map((size) => (
                            <UserPicture
                                key={size}
                                src={picture(210)}
                                alt={`Ada at ${size}`}
                                size={size}
                            />
                        ))}
                    </span>,
                ],
                [
                    'placeholder',
                    <span key="b" className="ui-story-chat-row">
                        {userPictureSizes.map((size) => (
                            <UserPicture key={size} size={size} />
                        ))}
                    </span>,
                ],
                [
                    'presence',
                    <span key="c" className="ui-story-chat-row">
                        <UserPicture
                            src={picture(120)}
                            alt="Online"
                            size={24}
                            presence="online"
                        />
                        <UserPicture
                            src={picture(30)}
                            alt="Away"
                            size={48}
                            presence="away"
                            presenceLabel="Away"
                        />
                        <UserPicture
                            src={picture(330)}
                            alt="Offline"
                            size={96}
                            presence="offline"
                            presenceLabel="Offline"
                        />
                    </span>,
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
