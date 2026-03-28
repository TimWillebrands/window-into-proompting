import { create } from 'zustand';

export type NavEntry =
    | { kind: 'window'; windowId: string }
    | { kind: 'config'; section: string };

interface NavHistoryState {
    stack: NavEntry[];
    index: number;
    isNavigating: boolean;
}

interface NavHistoryActions {
    push: (entry: NavEntry) => void;
    back: () => NavEntry | undefined;
    forward: () => NavEntry | undefined;
    clearNavigating: () => void;
}

export const useNavHistoryStore = create<NavHistoryState & NavHistoryActions>(
    (set, get) => ({
        stack: [],
        index: -1,
        isNavigating: false,

        push: (entry) => {
            if (get().isNavigating) return;
            const { stack, index } = get();

            // Skip if same as current
            const current = stack[index];
            if (current) {
                if (
                    entry.kind === 'window' &&
                    current.kind === 'window' &&
                    current.windowId === entry.windowId
                )
                    return;
                if (
                    entry.kind === 'config' &&
                    current.kind === 'config' &&
                    current.section === entry.section
                )
                    return;
            }

            // Truncate forward history on new push
            const newStack = stack.slice(0, index + 1).concat(entry);
            set({ stack: newStack, index: newStack.length - 1 });
        },

        back: () => {
            const { stack, index } = get();
            if (index <= 0) return undefined;
            const newIndex = index - 1;
            const entry = stack[newIndex];
            set({ index: newIndex, isNavigating: true });
            return entry;
        },

        forward: () => {
            const { stack, index } = get();
            if (index >= stack.length - 1) return undefined;
            const newIndex = index + 1;
            const entry = stack[newIndex];
            set({ index: newIndex, isNavigating: true });
            return entry;
        },

        clearNavigating: () => set({ isNavigating: false }),
    }),
);
