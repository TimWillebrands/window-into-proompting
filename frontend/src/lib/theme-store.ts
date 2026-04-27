import { create } from 'zustand';

type Mode = 'light' | 'dark';

interface ThemeState {
    mode: Mode;
    toggle: () => void;
    set: (mode: Mode) => void;
}

const applyDocClass = (mode: Mode) => {
    if (typeof document === 'undefined') return;
    const root = document.documentElement;
    if (mode === 'dark') root.classList.add('dark');
    else root.classList.remove('dark');
};

export const useThemeStore = create<ThemeState>((set, get) => ({
    mode: 'light',
    toggle: () => {
        const next: Mode = get().mode === 'dark' ? 'light' : 'dark';
        applyDocClass(next);
        set({ mode: next });
    },
    set: (mode) => {
        applyDocClass(mode);
        set({ mode });
    },
}));
