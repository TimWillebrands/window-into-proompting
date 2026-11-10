import type { Meta, StoryObj } from '@storybook/react-vite';
import { expect, fireEvent, fn, userEvent, within } from 'storybook/test';
import ModelSplit from './ModelSplit';
import { assignmentsFor, providers } from './modelFixtures';

const meta = {
    title: 'ConfigPanel/ModelSplit',
    component: ModelSplit,
    parameters: { layout: 'centered' },
    args: { providers, assignments: assignmentsFor(), onSaved: fn() },
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
} satisfies Meta<typeof ModelSplit>;

export default meta;
type Story = StoryObj<typeof meta>;

export const HalfAndHalf: Story = {
    play: async ({ canvasElement }) => {
        const canvas = within(canvasElement);
        await expect(
            canvas.getByRole('img', {
                name: 'General: claude-haiku-5-5 50%, nemotron-3-super:free 50%',
            }),
        ).toBeVisible();
        await expect(
            canvas.getByRole('img', {
                name: 'Decisions: jev-latest 50%, d1:3b 50%',
            }),
        ).toBeVisible();
        await expect(
            canvas.getByText('every call takes the next one'),
        ).toBeVisible();
        await expect(canvas.queryByText('Save split')).toBeNull();
    },
};

export const SavingADraggedShare: Story = {
    play: async ({ canvasElement, args }) => {
        const canvas = within(canvasElement);
        fireEvent.change(
            canvas.getByRole('slider', { name: 'claude-haiku-5-5 share' }),
            { target: { value: '25' } },
        );
        await expect(
            canvas.getByRole('img', {
                name: 'General: claude-haiku-5-5 33%, nemotron-3-super:free 67%',
            }),
        ).toBeVisible();
        await userEvent.click(
            canvas.getByRole('button', { name: 'Save split' }),
        );
        await expect(args.onSaved).toHaveBeenCalled();
        await expect(canvas.queryByText('Save split')).toBeNull();
    },
};

export const LocalBoxAsleepAndNemotronBenched: Story = {
    args: {
        availability: [
            {
                providerId: 'local',
                state: 2,
                reason: 'Connection refused',
                until: null,
            },
            {
                providerId: 'nemotron',
                state: 1,
                reason: '402 (Payment Required)',
                until: '2026-11-07T14:32:00Z',
            },
        ],
        assignments: assignmentsFor(['local', 'nemotron']),
    },
};

export const NothingSpeaks: Story = {
    args: {
        providers: providers.filter((p) =>
            ['jev', 'd1', 'opus'].includes(p.id ?? ''),
        ),
        assignments: [],
    },
};
