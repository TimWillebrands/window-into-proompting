import { flushSync } from 'react-dom';

type Target = Element | null | undefined;

export type MorphOptions = {
    from: Target;
    to: () => Target;
    update: () => void;
    name?: string;
};

const morphingFlag = 'data-ui-morphing';

const setName = (element: Target, name: string) => {
    if (element instanceof HTMLElement) element.style.viewTransitionName = name;
};

export const canMorph = () =>
    typeof document !== 'undefined' &&
    typeof document.startViewTransition === 'function';

export function morph({ from, to, update, name = 'ui-morph' }: MorphOptions) {
    if (!canMorph() || !(from instanceof HTMLElement)) {
        update();
        return;
    }
    const root = document.documentElement;
    let target: Target = null;
    setName(from, name);
    root.setAttribute(morphingFlag, '');
    const transition = document.startViewTransition(() => {
        setName(from, '');
        flushSync(update);
        target = to();
        setName(target, name);
    });
    const done = () => {
        setName(target, '');
        setName(from, '');
        root.removeAttribute(morphingFlag);
    };
    transition.finished.then(done, done);
}

const byData = (attribute: string, value: string): Target =>
    typeof document === 'undefined'
        ? null
        : document.querySelector(`[${attribute}="${CSS.escape(value)}"]`);

export const windowElement = (windowId: string) =>
    byData('data-ui-window-id', windowId);

export const taskbarButtonElement = (taskId: string) =>
    byData('data-ui-task-id', taskId);

export const startButtonElement = (): Target =>
    typeof document === 'undefined'
        ? null
        : document.querySelector('[data-ui-start-button]');

export const startMenuElement = (): Target =>
    typeof document === 'undefined'
        ? null
        : document.querySelector('.ui-start-menu');

export function minimiseWithTransition(windowId: string, update: () => void) {
    morph({
        from: windowElement(windowId),
        to: () => taskbarButtonElement(windowId),
        update,
        name: 'ui-window-morph',
    });
}

export function restoreWithTransition(windowId: string, update: () => void) {
    morph({
        from: taskbarButtonElement(windowId),
        to: () => windowElement(windowId),
        update,
        name: 'ui-window-morph',
    });
}

export function openStartMenuWithTransition(update: () => void) {
    morph({
        from: startButtonElement(),
        to: startMenuElement,
        update,
        name: 'ui-start-morph',
    });
}

export function closeStartMenuWithTransition(update: () => void) {
    morph({
        from: startMenuElement(),
        to: startButtonElement,
        update,
        name: 'ui-start-morph',
    });
}
