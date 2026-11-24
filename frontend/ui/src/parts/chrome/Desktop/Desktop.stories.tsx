import type { Meta, StoryObj } from '@storybook/react-vite';
import { useReducer, useState } from 'react';
import { expect, userEvent, waitFor, within } from 'storybook/test';
import {
    Desk,
    noir,
    reducedMotion,
    reducedTransparency,
} from '../../../story-kit/story-kit';
import { Presence } from '../../controls/Presence/Presence';
import { Text } from '../../controls/Text/Text';
import { TextArea } from '../../controls/TextArea/TextArea';
import { Icon, type IconName } from '../../icons';
import { Expander } from '../../surfaces/Expander/Expander';
import { Pane } from '../../surfaces/Pane/Pane';
import { WindowLayout } from '../../surfaces/WindowLayout/WindowLayout';
import { ContactRow } from '../../views/ContactRow/ContactRow';
import { DesktopIcon } from '../DesktopIcon/DesktopIcon';
import {
    StartMenu,
    StartMenuAction,
    StartMenuItem,
} from '../StartMenu/StartMenu';
import { Tray, TrayButton } from '../Tray/Tray';
import {
    emptyDesktop,
    type WindowAction,
    type WindowManagerState,
    windowActions,
    windowReducer,
} from '../WindowManager/WindowManager';
import { Desktop, launchProgram, type Program } from './Desktop';

const programs: (Program & { icon: IconName })[] = [
    {
        id: 'notepad',
        title: 'Untitled - Notepad',
        icon: 'notepad',
        defaultBounds: { x: 72, y: 40, width: 420, height: 280 },
        render: () => (
            <TextArea
                aria-label="Text"
                className="ui-story-fill"
                defaultValue="Drag me by the title bar, resize me from any edge, or push me against the left, right or top of the screen to snap."
            />
        ),
    },
    {
        id: 'computer',
        title: 'My Computer',
        icon: 'computer',
        defaultBounds: { x: 360, y: 120, width: 380, height: 260 },
        render: () => (
            <div className="ui-story-body">
                <Text as="p">
                    Every window is a program. The desktop places it, stacks it,
                    minimises it to the taskbar and remembers where it was.
                </Text>
            </div>
        ),
    },
    {
        id: 'calculator',
        title: 'Calculator',
        icon: 'calculator',
        singleton: false,
        defaultBounds: { width: 260, height: 220 },
        minSize: { width: 200, height: 160 },
        render: (_props, window) => (
            <div className="ui-story-body">
                <Text as="p">{window.id}</Text>
            </div>
        ),
    },
];

const messenger: Program & { icon: IconName } = {
    id: 'messenger',
    title: 'Messenger',
    icon: 'messenger',
    skin: 'msn',
    defaultBounds: { x: 880, y: 40, width: 300, height: 480 },
    render: () => (
        <WindowLayout scroll="none" contentLabel="Contacts">
            <Pane surface="sunken" pad="xs">
                <Expander title="Online" count={2}>
                    {['Ada', 'Linus'].map((name) => (
                        <ContactRow
                            key={name}
                            name={name}
                            picture={<Presence shape="buddy" status="online" />}
                        />
                    ))}
                </Expander>
            </Pane>
        </WindowLayout>
    ),
};

const withMessenger = [...programs, messenger];

const programOf = (id: string) => {
    const program = withMessenger.find((entry) => entry.id === id);
    if (!program) throw new Error(id);
    return program;
};

const area = { width: 1280, height: 770 };

const seeded = (...actions: WindowAction[]) =>
    actions.reduce(windowReducer, emptyDesktop);

const twoWindows = seeded(
    launchProgram(programOf('notepad'), { area }),
    launchProgram(programOf('computer'), { area }),
    windowActions.move('computer', { x: 540, y: 140 }, area),
);

function Demo({
    initial = twoWindows,
    skinned = false,
}: {
    initial?: WindowManagerState;
    skinned?: boolean;
}) {
    const [state, dispatch] = useReducer(windowReducer, initial);
    const [byName, setByName] = useState(false);
    const installed = skinned ? withMessenger : programs;
    const shown = byName
        ? [...installed].sort((a, b) => a.title.localeCompare(b.title))
        : installed;
    const launch = (id: string) => dispatch(launchProgram(programOf(id)));
    const closeStart = () => dispatch(windowActions.startMenu(false));
    return (
        <Desktop
            className="ui-story-fill"
            state={state}
            onAction={dispatch}
            programs={installed}
            minSize={{ width: 240, height: 160 }}
            desktopMenu={[
                {
                    id: 'arrange',
                    label: 'Arrange Icons By',
                    items: [
                        {
                            id: 'name',
                            label: 'Name',
                            checked: byName,
                            onSelect: () => setByName(true),
                        },
                    ],
                },
                { id: 'refresh', label: 'Refresh', onSelect: () => {} },
                { type: 'separator', id: 'properties-separator' },
                {
                    id: 'properties',
                    label: 'Properties',
                    onSelect: () => launch('computer'),
                },
            ]}
            icons={shown.map((program) => (
                <DesktopIcon
                    key={program.id}
                    icon={<Icon name={program.icon} size={48} />}
                    label={program.title}
                    onClick={() => launch(program.id)}
                />
            ))}
            startMenu={
                <StartMenu
                    user="Visitor"
                    picture={<Icon name="user" size={32} />}
                    onClose={closeStart}
                    programs={installed.map((program) => (
                        <StartMenuItem
                            key={program.id}
                            icon={<Icon name={program.icon} size={32} />}
                            label={program.title}
                            onClick={() => {
                                closeStart();
                                launch(program.id);
                            }}
                        />
                    ))}
                    footer={
                        <StartMenuAction tone="caution" onClick={closeStart}>
                            Log Off
                        </StartMenuAction>
                    }
                />
            }
            tray={
                <Tray clock="21:04">
                    <TrayButton
                        icon={<Icon name="bell" />}
                        label="Notifications"
                    />
                </Tray>
            }
        />
    );
}

const meta = {
    title: 'Chrome/Desktop',
    component: Demo,
    parameters: { layout: 'fullscreen' },
    decorators: [
        (Story, context) => (
            <Desk size="full">
                <Story key={context.id} />
            </Desk>
        ),
    ],
    beforeEach: () => {
        if (!navigator.webdriver) return;
        const original = document.startViewTransition;
        Object.defineProperty(document, 'startViewTransition', {
            value: undefined,
            configurable: true,
        });
        return () => {
            Object.defineProperty(document, 'startViewTransition', {
                value: original,
                configurable: true,
            });
        };
    },
} satisfies Meta<typeof Demo>;

export default meta;

type Story = StoryObj<typeof meta>;

const frameOf = (canvas: HTMLElement, title: string) => {
    const window = within(canvas).getByRole('region', { name: title });
    const frame = window.parentElement;
    if (!frame) throw new Error(title);
    return { window, frame };
};

const pointer = (
    type: string,
    target: Element,
    clientX: number,
    clientY: number,
) =>
    target.dispatchEvent(
        new PointerEvent(type, {
            bubbles: true,
            cancelable: true,
            composed: true,
            pointerId: 1,
            pointerType: 'mouse',
            isPrimary: true,
            button: 0,
            buttons: type === 'pointerup' ? 0 : 1,
            clientX,
            clientY,
        }),
    );

const settle = () =>
    Promise.all(
        document.getAnimations().map((animation) => animation.finished),
    );

const nextFrame = () =>
    new Promise((resolve) => requestAnimationFrame(() => resolve(null)));

async function drag(
    target: Element,
    from: { x: number; y: number },
    to: { x: number; y: number },
) {
    pointer('pointerdown', target, from.x, from.y);
    const steps = 6;
    for (let step = 1; step <= steps; step++) {
        pointer(
            'pointermove',
            target,
            from.x + ((to.x - from.x) * step) / steps,
            from.y + ((to.y - from.y) * step) / steps,
        );
        await nextFrame();
    }
    pointer('pointerup', target, to.x, to.y);
    await nextFrame();
}

const titleBarOf = (window: HTMLElement) => {
    const bar = window.querySelector('.ui-window-titlebar');
    if (!bar) throw new Error('no title bar');
    const box = bar.getBoundingClientRect();
    return { bar, x: box.left + 60, y: box.top + box.height / 2 };
};

export const Default: Story = {};

export const Empty: Story = { args: { initial: emptyDesktop } };

export const Maximised: Story = {
    args: {
        initial: seeded(
            launchProgram(programOf('notepad'), { area }),
            windowActions.maximise('notepad'),
        ),
    },
};

export const SnappedSideBySide: Story = {
    args: {
        initial: seeded(
            launchProgram(programOf('notepad'), { area }),
            launchProgram(programOf('computer'), { area }),
            windowActions.snap('notepad', 'left', area),
            windowActions.snap('computer', 'right', area),
        ),
    },
};

export const OpensFocusesAndCascades: Story = {
    args: { initial: emptyDesktop },
    play: async ({ canvasElement }) => {
        const canvas = within(canvasElement);
        await userEvent.click(
            canvas.getByRole('button', { name: 'Calculator' }),
        );
        await userEvent.click(
            canvas.getAllByRole('button', {
                name: 'Calculator',
            })[0] as HTMLElement,
        );
        const tasks = within(
            canvas.getByRole('toolbar', { name: 'Open windows' }),
        );
        await waitFor(() =>
            expect(
                tasks.getAllByRole('button', { name: 'Calculator' }),
            ).toHaveLength(2),
        );
        const [first, second] = canvas.getAllByRole('region', {
            name: 'Calculator',
        });
        if (!first || !second) throw new Error('two calculators');
        await expect(first.parentElement?.style.transform).toBe(
            'translate(48px, 24px)',
        );
        await expect(second.parentElement?.style.transform).toBe(
            'translate(72px, 48px)',
        );
        const a = first.getBoundingClientRect();
        await expect(second).not.toHaveAttribute('data-inactive');
        await expect(first).toHaveAttribute('data-inactive');
        pointer('pointerdown', first, a.left + 20, a.top + 60);
        pointer('pointerup', first, a.left + 20, a.top + 60);
        await waitFor(() => expect(first).not.toHaveAttribute('data-inactive'));
        await expect(second).toHaveAttribute('data-inactive');
    },
};

export const KeepsStateWhenMinimised: Story = {
    play: async ({ canvasElement }) => {
        await settle();
        const canvas = within(canvasElement);
        const { window, frame } = frameOf(canvasElement, 'Untitled - Notepad');
        const text = within(window).getByRole('textbox', { name: 'Text' });
        await userEvent.clear(text);
        await userEvent.type(text, 'Still here');
        await userEvent.click(
            within(window).getByRole('button', { name: 'Minimise' }),
        );
        await waitFor(() => expect(frame).toHaveAttribute('data-minimised'));
        await expect(frame).toHaveAttribute('inert');
        await expect(window).not.toBeVisible();
        await expect(text).not.toHaveFocus();
        const task = within(
            canvas.getByRole('toolbar', { name: 'Open windows' }),
        ).getByRole('button', { name: 'Untitled - Notepad' });
        await userEvent.click(task);
        await waitFor(() =>
            expect(frame).not.toHaveAttribute('data-minimised'),
        );
        await expect(window).toBeVisible();
        await expect(window.isConnected).toBe(true);
        await expect(text).toHaveValue('Still here');
        await expect(
            window
                .getAnimations()
                .filter((animation) => animation instanceof CSSAnimation),
        ).toHaveLength(0);
    },
};

export const SkinnedProgram: Story = {
    args: {
        skinned: true,
        initial: seeded(
            launchProgram(programOf('notepad'), { area }),
            launchProgram(messenger, { area }),
        ),
    },
    play: async ({ canvasElement }) => {
        const { window } = frameOf(canvasElement, 'Messenger');
        await expect(window).toHaveAttribute('data-ui-skin', 'msn');
        await expect(window).toHaveAttribute('data-title-bar', 'tall');
        const task = within(
            within(canvasElement).getByRole('toolbar', {
                name: 'Open windows',
            }),
        ).getByRole('button', { name: 'Messenger' });
        await expect(task.closest('[data-ui-skin]')).toBeNull();
    },
};

export const MinimiseAndRestore: Story = {
    play: async ({ canvasElement }) => {
        const canvas = within(canvasElement);
        const { window } = frameOf(canvasElement, 'My Computer');
        await userEvent.click(
            within(window).getByRole('button', { name: 'Minimise' }),
        );
        await waitFor(() =>
            expect(
                canvas.queryByRole('region', { name: 'My Computer' }),
            ).not.toBeInTheDocument(),
        );
        const tasks = within(
            canvas.getByRole('toolbar', { name: 'Open windows' }),
        );
        const task = tasks.getByRole('button', { name: 'My Computer' });
        await expect(task).toHaveAttribute('aria-pressed', 'false');
        await expect(
            tasks.getByRole('button', { name: 'Untitled - Notepad' }),
        ).toHaveAttribute('aria-pressed', 'true');
        await userEvent.click(task);
        await waitFor(() =>
            expect(task).toHaveAttribute('aria-pressed', 'true'),
        );
        await expect(
            canvas.getByRole('region', { name: 'My Computer' }),
        ).toBeInTheDocument();
        await userEvent.click(task);
        await waitFor(() =>
            expect(
                canvas.queryByRole('region', { name: 'My Computer' }),
            ).not.toBeInTheDocument(),
        );
    },
};

export const MaximiseByDoubleClick: Story = {
    args: { initial: seeded(launchProgram(programOf('computer'), { area })) },
    play: async ({ canvasElement }) => {
        const { window } = frameOf(canvasElement, 'My Computer');
        const { bar } = titleBarOf(window);
        await userEvent.dblClick(bar);
        await waitFor(() => expect(window).toHaveAttribute('data-maximised'));
        await userEvent.click(
            within(window).getByRole('button', { name: 'Restore' }),
        );
        await waitFor(() =>
            expect(window).not.toHaveAttribute('data-maximised'),
        );
    },
};

export const DragsByTheTitleBar: Story = {
    play: async ({ canvasElement }) => {
        await settle();
        const { window, frame } = frameOf(canvasElement, 'My Computer');
        const before = window.getBoundingClientRect();
        const { bar, x, y } = titleBarOf(window);
        await drag(bar, { x, y }, { x: x + 120, y: y + 80 });
        await waitFor(() => {
            const after = window.getBoundingClientRect();
            expect(Math.round(after.left - before.left)).toBe(120);
            expect(Math.round(after.top - before.top)).toBe(80);
        });
        await expect(frame.style.transform).toBe(
            `translate(${Math.round(before.left + 120)}px, ${Math.round(before.top + 80)}px)`,
        );
    },
};

export const ResizesByTheCorner: Story = {
    play: async ({ canvasElement }) => {
        await settle();
        const { window, frame } = frameOf(canvasElement, 'My Computer');
        const before = window.getBoundingClientRect();
        const grip = frame.querySelector('[data-grip="se"]');
        if (!grip) throw new Error('no grip');
        const box = grip.getBoundingClientRect();
        const from = {
            x: box.left + box.width / 2,
            y: box.top + box.height / 2,
        };
        await drag(grip, from, { x: from.x + 100, y: from.y + 60 });
        await waitFor(() => {
            const after = window.getBoundingClientRect();
            expect(Math.round(after.width - before.width)).toBe(100);
            expect(Math.round(after.height - before.height)).toBe(60);
        });
        await drag(
            grip,
            { x: from.x + 100, y: from.y + 60 },
            { x: from.x - 900, y: from.y - 900 },
        );
        await waitFor(() => {
            const after = window.getBoundingClientRect();
            expect(Math.round(after.width)).toBe(240);
            expect(Math.round(after.height)).toBe(160);
        });
    },
};

export const SnapsToTheEdges: Story = {
    play: async ({ canvasElement }) => {
        await settle();
        const { window } = frameOf(canvasElement, 'My Computer');
        const desk = canvasElement
            .querySelector('.ui-desktop-area')
            ?.getBoundingClientRect();
        if (!desk) throw new Error('no desktop');
        const start = titleBarOf(window);
        await drag(start.bar, start, { x: desk.left + 1, y: start.y + 40 });
        await waitFor(() => {
            const box = window.getBoundingClientRect();
            expect(Math.round(box.left)).toBe(Math.round(desk.left));
            expect(Math.round(box.width)).toBe(Math.round(desk.width / 2));
            expect(Math.round(box.height)).toBe(Math.round(desk.height));
        });
        const snapped = titleBarOf(window);
        await drag(snapped.bar, snapped, {
            x: snapped.x + 300,
            y: snapped.y + 100,
        });
        await waitFor(() =>
            expect(Math.round(window.getBoundingClientRect().width)).toBe(380),
        );
        const away = titleBarOf(window);
        await drag(away.bar, away, { x: away.x, y: desk.top + 1 });
        await waitFor(() => expect(window).toHaveAttribute('data-maximised'));
    },
};

export const KeyboardThroughTheTaskbar: Story = {
    play: async ({ canvasElement }) => {
        const canvas = within(canvasElement);
        const tasks = within(
            canvas.getByRole('toolbar', { name: 'Open windows' }),
        );
        const notepad = tasks.getByRole('button', {
            name: 'Untitled - Notepad',
        });
        notepad.focus();
        await userEvent.keyboard('{ArrowRight}');
        const computer = tasks.getByRole('button', { name: 'My Computer' });
        await expect(computer).toHaveFocus();
        await userEvent.keyboard('{ArrowLeft}{Enter}');
        const window = canvas.getByRole('region', {
            name: 'Untitled - Notepad',
        });
        await waitFor(() =>
            expect(window).not.toHaveAttribute('data-inactive'),
        );
        await waitFor(() => expect(window).toHaveFocus());
        await userEvent.keyboard('{Escape}');
        await expect(
            canvas.getByRole('region', { name: 'Untitled - Notepad' }),
        ).toBeInTheDocument();
    },
};

export const StartMenuOpens: Story = {
    play: async ({ canvasElement }) => {
        const canvas = within(canvasElement);
        await userEvent.click(canvas.getByRole('button', { name: 'start' }));
        const menu = await canvas.findByRole('menu', { name: 'Start menu' });
        await userEvent.click(
            within(menu).getByRole('menuitem', { name: 'Calculator' }),
        );
        await waitFor(() =>
            expect(
                canvas.queryByRole('menu', { name: 'Start menu' }),
            ).not.toBeInTheDocument(),
        );
        await expect(
            canvas.getByRole('region', { name: 'Calculator' }),
        ).toBeInTheDocument();
    },
};

export const DesktopMenu: Story = {
    play: async ({ canvasElement }) => {
        const area = canvasElement.querySelector('.ui-desktop-icons');
        if (!area) throw new Error('no desktop');
        const box = area.getBoundingClientRect();
        await userEvent.pointer({
            keys: '[MouseRight]',
            target: area,
            coords: { clientX: box.right - 200, clientY: box.bottom - 200 },
        });
        const page = within(canvasElement.ownerDocument.body);
        const menu = await page.findByRole('menu', { name: 'Desktop' });
        await expect(
            within(menu).getByRole('menuitem', { name: 'Refresh' }),
        ).toBeInTheDocument();
        await userEvent.click(
            within(menu).getByRole('menuitem', { name: 'Properties' }),
        );
        await waitFor(() =>
            expect(
                within(canvasElement).getByRole('region', {
                    name: 'My Computer',
                }),
            ).not.toHaveAttribute('data-inactive'),
        );
    },
};

export const Noir: Story = { ...noir };

export const ReducedMotion: Story = { ...reducedMotion };

export const ReducedTransparency: Story = {
    ...reducedTransparency,
    args: { initial: SnappedSideBySide.args?.initial },
};
