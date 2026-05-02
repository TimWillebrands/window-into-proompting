import type { Meta, StoryObj } from '@storybook/react-vite';
import { useState } from 'react';
import { Rnd } from 'react-rnd';
import { fn } from 'storybook/test';
import WindowFrame from './WindowFrame';

const DRAG_HANDLE = 'window-drag-handle';

const meta = {
    title: 'Desktop/WindowFrame',
    component: WindowFrame,
    parameters: { layout: 'centered', backgrounds: { default: 'xp-desktop' } },
    args: {
        id: 'demo',
        title: 'Untitled — Notepad',
        icon: '📝',
        width: 600,
        height: 400,
        zIndex: 1,
        onMinimize: fn(),
        onMaximize: fn(),
        onClose: fn(),
        focused: true,
        children: (
            <div style={{ padding: 12 }}>
                Window body content goes here. Chrome is Tailwind-only.
            </div>
        ),
    },
    decorators: [
        (Story) => (
            <div style={{ width: 600, height: 400 }}>
                <Story />
            </div>
        ),
    ],
} satisfies Meta<typeof WindowFrame>;

export default meta;
type Story = StoryObj<typeof meta>;

export const Default: Story = {};

export const NarrowWindow: Story = {
    args: {
        width: 320,
        height: 240,
        title: 'Calculator',
        icon: '🧮',
    },
    decorators: [
        (Story) => (
            <div style={{ width: 320, height: 240 }}>
                <Story />
            </div>
        ),
    ],
};

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
        backgrounds: { default: 'xp-desktop' },
        docs: {
            description: {
                story: 'Demonstrates how `WindowFrame` integrates with `react-rnd`. The title bar carries the `dragHandleClassName`, so grabbing it drags the window; edges and corners resize. Movement is constrained to the dashed canvas via `bounds="parent"`, mirroring how `DesktopExperience` wires it up.',
            },
        },
    },
    decorators: [
        (Story) => (
            <div
                style={{
                    position: 'relative',
                    width: '100%',
                    height: '100vh',
                    overflow: 'hidden',
                    border: '2px dashed rgba(255,255,255,0.4)',
                    boxSizing: 'border-box',
                }}
            >
                <div
                    style={{
                        position: 'absolute',
                        top: 8,
                        left: 12,
                        color: 'white',
                        fontFamily: 'sans-serif',
                        fontSize: 12,
                        textShadow: '0 1px 2px rgba(0,0,0,0.6)',
                        pointerEvents: 'none',
                    }}
                >
                    Drag the title bar · resize from any edge · bounded to this
                    canvas
                </div>
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
                    <div style={{ padding: 12, lineHeight: 1.5 }}>
                        <p>
                            <strong>Drag</strong> the title bar to move me.
                        </p>
                        <p>
                            <strong>Resize</strong> from any edge or corner.
                        </p>
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
