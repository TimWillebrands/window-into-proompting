import { describe, expect, it } from 'vitest';
import {
    windowActions as act,
    cascadeStep,
    emptyDesktop,
    snapBounds,
    type WindowAction,
    type WindowManagerState,
    windowReducer,
} from './WindowManager';

const area = { width: 1000, height: 700 };

const run = (...actions: WindowAction[]) =>
    actions.reduce(windowReducer, emptyDesktop);

const notepad = act.open({
    appId: 'notepad',
    title: 'Notepad',
    icon: 'notepad',
    bounds: { x: 100, y: 80, width: 400, height: 300 },
    area,
});

const paint = act.open({
    appId: 'paint',
    title: 'Paint',
    icon: 'paint',
    bounds: { x: 600, y: 400, width: 300, height: 200 },
    area,
});

const at = (state: WindowManagerState, id: string) => {
    const window = state.windows[id];
    if (!window) throw new Error(`no window ${id}`);
    return window;
};

describe('open', () => {
    it('places the first window where its program asks, in front', () => {
        const state = run(notepad);
        expect(at(state, 'notepad')).toMatchObject({
            x: 100,
            y: 80,
            width: 400,
            height: 300,
            zIndex: 1,
            minimized: false,
            maximized: false,
            restoreBounds: null,
            props: {},
        });
        expect(state.focusedId).toBe('notepad');
        expect(state.order).toEqual(['notepad']);
        expect(state.zCounter).toBe(1);
    });

    it('cascades each new window down and right from the last', () => {
        const state = run(notepad, paint);
        expect(at(state, 'paint')).toMatchObject({
            x: 100 + cascadeStep,
            y: 80 + cascadeStep,
        });
        expect(state.focusedId).toBe('paint');
    });

    it('wraps the cascade inside the desktop', () => {
        const tall = act.open({
            appId: 'tall',
            title: 'Tall',
            icon: 'document',
            bounds: { width: 300, height: 650 },
            area,
        });
        const state = run(notepad, tall);
        expect(at(state, 'tall').y).toBe(cascadeStep);
        const wide = act.open({
            appId: 'wide',
            title: 'Wide',
            icon: 'document',
            bounds: { width: 900, height: 200 },
            area,
        });
        expect(at(run(notepad, wide), 'wide').x).toBe(cascadeStep);
    });

    it('shrinks a window to fit a small desktop', () => {
        const state = windowReducer(emptyDesktop, {
            ...notepad,
            area: { width: 320, height: 240 },
        } as WindowAction);
        expect(at(state, 'notepad')).toMatchObject({
            x: 0,
            y: 0,
            width: 320,
            height: 240,
        });
    });

    it('reopening a singleton brings it back and merges its props', () => {
        const first = act.open({
            appId: 'board',
            title: 'Board',
            icon: 'board',
            props: { tab: 'new' },
        });
        const state = run(
            first,
            paint,
            act.minimise('board'),
            act.open({
                appId: 'board',
                title: 'Board',
                icon: 'board',
                props: { threadId: 't1' },
            }),
        );
        expect(Object.keys(state.windows)).toEqual(['board', 'paint']);
        expect(at(state, 'board')).toMatchObject({
            minimized: false,
            props: { tab: 'new', threadId: 't1' },
        });
        expect(state.focusedId).toBe('board');
        expect(state.order.at(-1)).toBe('board');
    });

    it('opens another instance when the program is not a singleton', () => {
        const chat = act.open({
            appId: 'chat',
            title: 'Chat',
            icon: 'chat',
            singleton: false,
        });
        const state = run(chat, chat);
        expect(Object.keys(state.windows)).toEqual(['chat-1', 'chat-2']);
    });

    it('uses an explicit instance id', () => {
        const state = run(
            act.open({
                appId: 'chat',
                id: 'dm-1',
                title: 'Denise',
                icon: 'chat',
            }),
        );
        expect(at(state, 'dm-1').appId).toBe('chat');
    });

    it('closes the Start menu', () => {
        const state = run(act.startMenu(true), notepad);
        expect(state.startMenuOpen).toBe(false);
    });
});

describe('focus', () => {
    it('raises a window to the front', () => {
        const state = run(notepad, paint, act.focus('notepad'));
        expect(at(state, 'notepad').zIndex).toBeGreaterThan(
            at(state, 'paint').zIndex,
        );
        expect(state.focusedId).toBe('notepad');
        expect(state.order).toEqual(['paint', 'notepad']);
    });

    it('is a no-op on the front window', () => {
        const state = run(notepad, paint);
        expect(windowReducer(state, act.focus('paint'))).toBe(state);
    });

    it('ignores unknown windows', () => {
        const state = run(notepad);
        for (const action of [
            act.focus('nope'),
            act.close('nope'),
            act.minimise('nope'),
            act.restore('nope'),
            act.maximise('nope'),
            act.toggleMaximise('nope'),
            act.move('nope', { x: 1, y: 1 }),
            act.setProps('nope', {}),
            act.snap('nope', 'left', area),
        ])
            expect(windowReducer(state, action)).toBe(state);
    });
});

describe('close', () => {
    it('removes the window and focuses the next one down', () => {
        const state = run(notepad, paint, act.close('paint'));
        expect(state.windows.paint).toBeUndefined();
        expect(state.order).toEqual(['notepad']);
        expect(state.focusedId).toBe('notepad');
    });

    it('leaves nothing focused when the last window closes', () => {
        expect(run(notepad, act.close('notepad')).focusedId).toBeNull();
    });

    it('keeps the focus when a background window closes', () => {
        expect(run(notepad, paint, act.close('notepad')).focusedId).toBe(
            'paint',
        );
    });
});

describe('minimise and restore', () => {
    it('minimises and passes focus to the next visible window', () => {
        const state = run(notepad, paint, act.minimise('paint'));
        expect(at(state, 'paint').minimized).toBe(true);
        expect(state.focusedId).toBe('notepad');
        expect(windowReducer(state, act.minimise('paint'))).toBe(state);
    });

    it('focuses nothing when every window is minimised', () => {
        const state = run(
            notepad,
            paint,
            act.minimise('paint'),
            act.minimise('notepad'),
        );
        expect(state.focusedId).toBeNull();
    });

    it('restores a minimised window to the front', () => {
        const state = run(notepad, paint, act.minimise('notepad'));
        const restored = windowReducer(state, act.restore('notepad'));
        expect(at(restored, 'notepad').minimized).toBe(false);
        expect(restored.focusedId).toBe('notepad');
        expect(at(restored, 'notepad').zIndex).toBe(restored.zCounter);
    });

    it('restores a minimised maximised window still maximised', () => {
        const state = run(
            notepad,
            act.maximise('notepad'),
            act.minimise('notepad'),
            act.restore('notepad'),
        );
        expect(at(state, 'notepad')).toMatchObject({
            minimized: false,
            maximized: true,
        });
    });

    it('restore un-maximises a visible maximised window', () => {
        const state = run(
            notepad,
            act.maximise('notepad'),
            act.restore('notepad'),
        );
        expect(at(state, 'notepad').maximized).toBe(false);
    });

    it('focusing a minimised window restores it', () => {
        const state = run(
            notepad,
            paint,
            act.minimise('notepad'),
            act.focus('notepad'),
        );
        expect(at(state, 'notepad').minimized).toBe(false);
    });
});

describe('maximise', () => {
    it('keeps the normal bounds to come back to', () => {
        const state = run(notepad, act.maximise('notepad'));
        expect(at(state, 'notepad')).toMatchObject({
            maximized: true,
            x: 100,
            y: 80,
            width: 400,
            height: 300,
        });
        const back = windowReducer(state, act.toggleMaximise('notepad'));
        expect(at(back, 'notepad')).toMatchObject({
            maximized: false,
            x: 100,
            y: 80,
        });
    });

    it('toggles', () => {
        const state = run(notepad, act.toggleMaximise('notepad'));
        expect(at(state, 'notepad').maximized).toBe(true);
        expect(windowReducer(state, act.maximise('notepad'))).toBe(state);
    });
});

describe('move and resize', () => {
    it('moves inside the desktop', () => {
        const state = run(
            notepad,
            act.move('notepad', { x: 300, y: 200 }, area),
        );
        expect(at(state, 'notepad')).toMatchObject({ x: 300, y: 200 });
    });

    it('clamps a move to the desktop', () => {
        const state = run(
            notepad,
            act.move('notepad', { x: -50, y: 900 }, area),
        );
        expect(at(state, 'notepad')).toMatchObject({ x: 0, y: 400 });
    });

    it('is a no-op when nothing changes', () => {
        const state = run(notepad);
        expect(
            windowReducer(state, act.move('notepad', { x: 100, y: 80 })),
        ).toBe(state);
    });

    it('does not move a maximised window', () => {
        const state = run(notepad, act.maximise('notepad'));
        expect(windowReducer(state, act.move('notepad', { x: 5, y: 5 }))).toBe(
            state,
        );
    });

    it('resizes with a minimum size and inside the desktop', () => {
        const state = run(
            notepad,
            act.resize(
                'notepad',
                { x: 700, y: 80, width: 100, height: 900 },
                { minSize: { width: 240, height: 160 }, area },
            ),
        );
        expect(at(state, 'notepad')).toMatchObject({
            x: 700,
            y: 0,
            width: 240,
            height: 700,
        });
    });
});

describe('snap', () => {
    it('snaps to the left and right halves', () => {
        const left = run(notepad, act.snap('notepad', 'left', area));
        expect(at(left, 'notepad')).toMatchObject({
            x: 0,
            y: 0,
            width: 500,
            height: 700,
            restoreBounds: { x: 100, y: 80, width: 400, height: 300 },
        });
        const right = windowReducer(left, act.snap('notepad', 'right', area));
        expect(at(right, 'notepad')).toMatchObject({
            x: 500,
            width: 500,
            restoreBounds: { x: 100, y: 80, width: 400, height: 300 },
        });
    });

    it('snapping to the top maximises', () => {
        const state = run(notepad, act.snap('notepad', 'top', area));
        expect(at(state, 'notepad')).toMatchObject({
            maximized: true,
            x: 100,
            width: 400,
        });
    });

    it('dragging a snapped window away gives it its size back', () => {
        const state = run(
            notepad,
            act.snap('notepad', 'left', area),
            act.move('notepad', { x: 200, y: 120 }, area),
        );
        expect(at(state, 'notepad')).toMatchObject({
            x: 200,
            y: 120,
            width: 400,
            height: 300,
            restoreBounds: null,
        });
    });

    it('resizing a snapped window keeps the new size', () => {
        const state = run(
            notepad,
            act.snap('notepad', 'left', area),
            act.resize('notepad', { x: 0, y: 0, width: 420, height: 700 }),
        );
        expect(at(state, 'notepad')).toMatchObject({
            width: 420,
            restoreBounds: null,
        });
    });

    it('splits odd widths without a gap', () => {
        const odd = { width: 1001, height: 700 };
        const left = snapBounds('left', odd);
        const right = snapBounds('right', odd);
        expect(left.width + right.width).toBe(1001);
        expect(right.x).toBe(left.width);
    });
});

describe('props and the Start menu', () => {
    it('replaces props', () => {
        const props = { roomId: 'r1' };
        const state = run(notepad, act.setProps('notepad', props));
        expect(at(state, 'notepad').props).toBe(props);
        expect(windowReducer(state, act.setProps('notepad', props))).toBe(
            state,
        );
    });

    it('opens, closes and toggles the Start menu', () => {
        const opened = run(act.startMenu());
        expect(opened.startMenuOpen).toBe(true);
        expect(windowReducer(opened, act.startMenu(true))).toBe(opened);
        expect(windowReducer(opened, act.startMenu()).startMenuOpen).toBe(
            false,
        );
    });

    it('never mutates the state it was given', () => {
        const state = run(notepad, paint);
        const frozen = JSON.stringify(state);
        for (const action of [
            act.focus('notepad'),
            act.minimise('paint'),
            act.snap('notepad', 'left', area),
            act.close('notepad'),
            act.setProps('paint', { a: 1 }),
        ])
            windowReducer(state, action);
        expect(JSON.stringify(state)).toBe(frozen);
    });
});
