import { isScheme, type Scheme, schemes, setScheme } from '@partytown/ui';
import { create } from 'zustand';
import { createJSONStorage, persist } from 'zustand/middleware';

type Mode = 'light' | 'dark';

type LightScheme = Exclude<Scheme, 'noir'>;

interface ThemeState {
    scheme: Scheme;
    lastLight: LightScheme;
    mode: Mode;
    choose: (scheme: Scheme) => void;
    toggle: () => void;
    set: (mode: Mode) => void;
}

const STORAGE_KEY = 'partytown-scheme';

export const schemeScript = `(()=>{try{const s=JSON.parse(localStorage.getItem(${JSON.stringify(
    STORAGE_KEY,
)}))?.state?.scheme;if(${JSON.stringify(schemes)}.includes(s)){const r=document.documentElement;r.setAttribute('data-ui-scheme',s);r.classList.toggle('dark',s==='noir')}}catch{}})()`;

const modeOf = (scheme: Scheme): Mode => (scheme === 'noir' ? 'dark' : 'light');

const apply = (scheme: Scheme) => {
    if (typeof document === 'undefined') return;
    const root = document.documentElement;
    if (scheme === 'noir') root.classList.add('dark');
    else root.classList.remove('dark');
    setScheme(scheme, root);
};

export const useThemeStore = create<ThemeState>()(
    persist(
        (set, get) => {
            const choose = (scheme: Scheme) => {
                apply(scheme);
                set({
                    scheme,
                    mode: modeOf(scheme),
                    lastLight: scheme === 'noir' ? get().lastLight : scheme,
                });
            };
            return {
                scheme: 'luna-blue',
                lastLight: 'luna-blue',
                mode: 'light',
                choose,
                toggle: () =>
                    choose(get().scheme === 'noir' ? get().lastLight : 'noir'),
                set: (mode) =>
                    choose(mode === 'dark' ? 'noir' : get().lastLight),
            };
        },
        {
            name: STORAGE_KEY,
            storage: createJSONStorage(() => localStorage),
            skipHydration: true,
            partialize: (state) => ({
                scheme: state.scheme,
                lastLight: state.lastLight,
            }),
            merge: (persisted, current) => {
                const saved = (persisted ?? {}) as Record<string, unknown>;
                const scheme =
                    typeof saved.scheme === 'string' && isScheme(saved.scheme)
                        ? saved.scheme
                        : current.scheme;
                const light = saved.lastLight;
                const lastLight =
                    typeof light === 'string' &&
                    isScheme(light) &&
                    light !== 'noir'
                        ? light
                        : current.lastLight;
                return {
                    ...current,
                    scheme,
                    lastLight,
                    mode: modeOf(scheme),
                };
            },
            onRehydrateStorage: () => (state) => {
                if (state) apply(state.scheme);
            },
        },
    ),
);
