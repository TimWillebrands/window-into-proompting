import { flushSync } from 'react-dom';

type Target = Element | null | undefined;

export type MorphOptions = {
    from: Target;
    to: () => Target;
    update: () => void;
    name?: string;
};

const morphedFlag = 'data-ui-morphed';

const setName = (element: Target, name: string) => {
    if (element instanceof HTMLElement) element.style.viewTransitionName = name;
};

const markMorphed = (element: Target) => {
    if (element instanceof HTMLElement) element.setAttribute(morphedFlag, '');
};

export const canMorph = () =>
    typeof document !== 'undefined' &&
    typeof document.startViewTransition === 'function';

export function morph({
    from,
    to,
    update,
    name = 'ui-morph',
}: MorphOptions): Promise<void> {
    if (!canMorph() || !(from instanceof HTMLElement)) {
        update();
        return Promise.resolve();
    }
    let target: Target = null;
    markMorphed(from);
    setName(from, name);
    const transition = document.startViewTransition(() => {
        setName(from, '');
        flushSync(update);
        target = to();
        markMorphed(target);
        setName(target, name);
    });
    const done = () => {
        setName(target, '');
        setName(from, '');
    };
    transition.ready.catch(() => {});
    transition.finished.then(done, done);
    return transition.updateCallbackDone.catch(() => {});
}

const byData = (attribute: string, value: string): Target =>
    typeof document === 'undefined'
        ? null
        : document.querySelector(`[${attribute}="${CSS.escape(value)}"]`);

export const windowElement = (windowId: string) =>
    byData('data-ui-window-id', windowId);

export const taskbarButtonElement = (taskId: string) =>
    byData('data-ui-task-id', taskId);
