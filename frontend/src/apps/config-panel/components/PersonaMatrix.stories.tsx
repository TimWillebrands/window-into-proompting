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
            <div
                style={{
                    width: 660,
                    padding: 20,
                    background:
                        'linear-gradient(135deg, #5C87C2 0%, #4A73B0 100%)',
                }}
            >
                <Story />
            </div>
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

/** Opus is benched: Vlad's voice is on the split until it's back. */
export const PinnedModelIsAway: Story = {
    args: { assignments: assignmentsFor(['opus']) },
};

export const EveryoneOnTheSplit: Story = {
    args: { routes: [] },
};
