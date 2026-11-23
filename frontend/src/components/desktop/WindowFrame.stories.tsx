import type { Meta, StoryObj } from '@storybook/react-vite';
import { useState } from 'react';
import { Rnd } from 'react-rnd';
import { fn } from 'storybook/test';
import WindowFrame from './WindowFrame';

const DRAG_HANDLE = 'window-drag-handle';

const meta = {
    title: 'Desktop/WindowFrame',
    component: WindowFrame,
    parameters: { layout: 'centered' },
    args: {
        id: 'demo',
        title: 'Untitled - Notepad',
        icon: '/img/icons/notepad.ico',
        width: 600,
        height: 400,
        zIndex: 1,
        onMinimize: fn(),
        onMaximize: fn(),
        onClose: fn(),
        focused: true,
        children: <div className="p-3">Window body content goes here.</div>,
    },
    decorators: [
        (Story) => (
            <div className="h-[400px] w-[600px]">
                <Story />
            </div>
        ),
    ],
} satisfies Meta<typeof WindowFrame>;

export default meta;
type Story = StoryObj<typeof meta>;

export const Default: Story = {};

export const SuspenseFallback: Story = {
    name: 'Suspense fallback',
    args: {
        title: 'Loading…',
        children: <SuspendForever />,
    },
};

function SuspendForever(): never {
    throw new Promise<never>(() => {});
}

export const DragAndDrop: Story = {
    name: 'Drag & drop (with resize)',
    parameters: {
        layout: 'fullscreen',
        docs: {
            description: {
                story: 'How `WindowFrame` works with `react-rnd`: the title bar carries the `dragHandleClassName`, so grabbing it drags the window; edges and corners resize. Bounded to the canvas with `bounds="parent"`, as in `DesktopExperience`.',
            },
        },
    },
    decorators: [
        (Story) => (
            <div className="relative h-screen w-full overflow-hidden">
                <Story />
            </div>
        ),
    ],
    render: (args) => <DraggableWindowDemo {...args} />,
};

function DraggableWindowDemo(args: React.ComponentProps<typeof WindowFrame>) {
    const [size, setSize] = useState({ width: 480, height: 320 });
    const [pos, setPos] = useState({ x: 80, y: 60 });
    const [focused, setFocused] = useState(true);

    return (
        <Rnd
            size={size}
            position={pos}
            minWidth={280}
            minHeight={180}
            bounds="parent"
            dragHandleClassName={DRAG_HANDLE}
            style={{ zIndex: 1, pointerEvents: 'auto' }}
            onDragStart={() => setFocused(true)}
            onDragStop={(_e, data) => setPos({ x: data.x, y: data.y })}
            onResizeStart={() => setFocused(true)}
            onResizeStop={(_e, _dir, ref, _delta, position) => {
                setSize({
                    width: ref.offsetWidth,
                    height: ref.offsetHeight,
                });
                setPos(position);
            }}
        >
            <div
                className="h-full w-full"
                onPointerDown={() => setFocused(true)}
            >
                <WindowFrame
                    {...args}
                    width={size.width}
                    height={size.height}
                    focused={focused}
                    dragHandleClassName={DRAG_HANDLE}
                >
                    <div className="p-3">
                        <p>Drag the title bar to move me.</p>
                        <p>Resize from any edge or corner.</p>
                        <p>
                            Position: ({pos.x}, {pos.y}) · Size: {size.width}×
                            {size.height}
                        </p>
                    </div>
                </WindowFrame>
            </div>
        </Rnd>
    );
}
