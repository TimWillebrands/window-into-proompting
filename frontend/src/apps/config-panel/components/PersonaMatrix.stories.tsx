import { Icon, Window } from '@partytown/ui';
import type { Meta, StoryObj } from '@storybook/react-vite';
import { expect, fn, mocked, userEvent, within } from 'storybook/test';
import { usePutLlmConfigPersonaRoutesPersonaId } from '#api/party-zone';
import { assignmentsFor, personas, providers, routes } from './modelFixtures';
import PersonaMatrix from './PersonaMatrix';

const meta = {
    title: 'ConfigPanel/PersonaMatrix',
    component: PersonaMatrix,
    parameters: { layout: 'centered' },
    args: {
        personas,
        providers,
        routes,
        assignments: assignmentsFor(),
        onChanged: fn(),
    },
    decorators: [
        (Story) => (
            <Window
                title="Control Panel"
                icon={<Icon name="control-panel" />}
                className="w-[660px]"
                bodyClassName="overflow-auto"
            >
                <div className="p-3">
                    <Story />
                </div>
            </Window>
        ),
    ],
} satisfies Meta<typeof PersonaMatrix>;

export default meta;
type Story = StoryObj<typeof meta>;

export const SplitWithTwoPins: Story = {
    play: async ({ canvasElement }) => {
        const canvas = within(canvasElement);
        await expect(
            canvas.getByRole('combobox', { name: 'Vlad: Character Voice' }),
        ).toHaveValue('opus');
        await expect(
            canvas.getByRole('combobox', { name: 'Bram: General' }),
        ).toHaveValue('');
        await expect(
            canvas.getByText('2 of 7 have a model of their own.'),
        ).toBeVisible();
        await expect(
            within(
                canvas.getByRole('combobox', { name: 'Bram: Decisions' }),
            ).getByRole('option', { name: 'takes turns' }),
        ).toBeInTheDocument();
    },
};

export const PinningAVoice: Story = {
    play: async ({ canvasElement }) => {
        const canvas = within(canvasElement);
        await userEvent.selectOptions(
            canvas.getByRole('combobox', { name: 'Bram: Character Voice' }),
            'opus',
        );
        const save = mocked(
            usePutLlmConfigPersonaRoutesPersonaId,
        ).mock.results.at(-1)?.value;
        await expect(save.mutate).toHaveBeenCalledWith(
            {
                personaId: 'bram',
                data: [{ personaId: 'bram', tier: 2, providerId: 'opus' }],
            },
            expect.anything(),
        );
    },
};

export const OnlyPinned: Story = {
    play: async ({ canvasElement }) => {
        const canvas = within(canvasElement);
        await userEvent.click(canvas.getByLabelText('Only pinned'));
        await expect(canvas.queryByText('Bram')).toBeNull();
        await expect(canvas.getByText('Vlad')).toBeVisible();
    },
};

export const PinnedModelIsAway: Story = {
    args: { assignments: assignmentsFor(['opus']) },
};

export const EveryoneOnTheSplit: Story = {
    args: { routes: [] },
};
