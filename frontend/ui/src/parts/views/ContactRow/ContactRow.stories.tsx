import type { Meta, StoryObj } from '@storybook/react-vite';
import { fn } from 'storybook/test';
import {
    noir,
    picture,
    reducedMotion,
    reducedTransparency,
    stage,
} from '../../../story-kit/story-kit';
import { Presence } from '../../controls/Presence/Presence';
import { Text } from '../../controls/Text/Text';
import { UserPicture } from '../../controls/UserPicture/UserPicture';
import { SunkenField } from '../../surfaces/SunkenField/SunkenField';
import { ContactRow } from './ContactRow';

const meta = {
    title: 'Views/ContactRow',
    component: ContactRow,
    decorators: [stage],
    args: { name: 'Ada' },
} satisfies Meta<typeof ContactRow>;

export default meta;

type Story = StoryObj<typeof meta>;

export const Default: Story = {
    render: () => (
        <SunkenField
            role="group"
            aria-label="Contacts"
            className="ui-story-chat-w-260 ui-story-chat-list"
        >
            <ContactRow
                onClick={fn()}
                picture={<Presence status="online" shape="buddy" />}
                name={
                    <>
                        Ada{' '}
                        <Text tone="muted" italic>
                            - at the café
                        </Text>
                    </>
                }
            />
            <ContactRow
                onClick={fn()}
                data-ui-preview="hover"
                picture={<Presence status="busy" shape="buddy" />}
                name={
                    <>
                        Grace <Text tone="muted">(Busy)</Text>
                    </>
                }
            />
            <ContactRow
                onClick={fn()}
                data-ui-preview="focus"
                picture={<Presence status="away" shape="buddy" />}
                name="Linus (focus)"
            />
            <ContactRow
                onClick={fn()}
                selected
                aria-pressed
                picture={<UserPicture src={picture(210)} size={24} />}
                name="Selected"
                meta={<Presence status="online" size="small" />}
            />
            <ContactRow
                onClick={fn()}
                dimmed
                aria-pressed={false}
                picture={<UserPicture src={picture(30)} size={24} />}
                name="Dimmed"
                meta={<Presence status="offline" size="small" />}
            />
            <ContactRow
                onClick={fn()}
                disabled
                picture={<Presence status="online" shape="buddy" />}
                name="Opening…"
            />
        </SunkenField>
    ),
};

export const WithDetails: Story = {
    render: () => (
        <SunkenField
            role="group"
            aria-label="Conversations"
            className="ui-story-chat-w-260 ui-story-chat-list"
        >
            <ContactRow
                onClick={fn()}
                unread
                picture={<UserPicture src={picture(210)} size={32} />}
                name="Ada"
                meta="21:04"
                details="are you around tonight?"
            />
            <ContactRow
                onClick={fn()}
                picture={<UserPicture src={picture(120)} size={32} />}
                name="Linus"
                meta="Tue"
                details="You: see you then"
            />
            <ContactRow
                picture={<UserPicture src={picture(30)} size={32} />}
                name="Static row"
                meta="you"
            />
        </SunkenField>
    ),
};

export const Noir: Story = { ...Default, ...noir };
export const ReducedMotion: Story = { ...Default, ...reducedMotion };
export const ReducedTransparency: Story = {
    ...Default,
    ...reducedTransparency,
};
