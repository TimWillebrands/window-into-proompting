import type { Meta, StoryObj } from '@storybook/react-vite';
import {
    noir,
    reducedMotion,
    reducedTransparency,
    stage,
} from '../../../story-kit/story-kit';
import { RichText } from './RichText';

const meta = {
    title: 'Views/RichText',
    component: RichText,
    decorators: [stage],
} satisfies Meta<typeof RichText>;

export default meta;

type Story = StoryObj<typeof meta>;

export const Default: Story = {
    render: () => (
        <RichText className="ui-story-chat-w-420">
            <h2>Plans for tonight</h2>
            <p>
                We meet at the <strong>café</strong> at eight. Bring{' '}
                <em>something warm</em>, and read <a href="#notes">the notes</a>{' '}
                first.
            </p>
            <ul>
                <li>Coffee for everyone</li>
                <li>
                    The word of the day is <code>serendipity</code>
                </li>
            </ul>
            <blockquote>Nobody leaves before the song ends.</blockquote>
            <pre>
                <code>{'tick every 30s\nmax 20 actions/hour'}</code>
            </pre>
            <hr />
            <table>
                <thead>
                    <tr>
                        <th>Who</th>
                        <th>Brings</th>
                    </tr>
                </thead>
                <tbody>
                    <tr>
                        <td>Ada</td>
                        <td>Mugs</td>
                    </tr>
                    <tr>
                        <td>Grace</td>
                        <td>Biscuits</td>
                    </tr>
                </tbody>
            </table>
        </RichText>
    ),
};

export const Noir: Story = { ...Default, ...noir };
export const ReducedMotion: Story = { ...Default, ...reducedMotion };
export const ReducedTransparency: Story = {
    ...Default,
    ...reducedTransparency,
};
