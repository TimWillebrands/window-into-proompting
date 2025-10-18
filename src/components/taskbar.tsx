import type { PropsWithChildren } from "hono/jsx";

export function Taskbar({ children }: PropsWithChildren<unknown>) {
    return (
        <section
            id="taskbar"
            className="fixed bottom-0 left-0 right-0 h-8 bg-gradient-to-b
            from-blue-600 to-blue-800 border-t-2 border-t-blue-400 flex
            items-center z-50 shadow-[0_-2px_8px_rgba(0,0,0,0.3)]"
        >
            <StartButton />
            <div className="h-full w-px bg-blue-900 mx-1"></div>
            <UserButton />
            <div className="h-full w-px bg-blue-900 mx-1"></div>
            <div className="flex-1 flex items-center px-1 gap-1 h-full overflow-x-auto">
                <template x-for="w in Object.values(windows).filter(Boolean)">
                    <button
                        type="button"
                        className="h-6 px-2.5 text-[11px] border flex items-center gap-1.5 rounded-sm transition-all duration-150 flex-shrink-0 min-w-[120px] max-w-[200px]"
                        x-bind:class="focusedApp === w.id 
                            ? '!bg-gradient-to-b from-orange-300 via-orange-200 to-orange-100 border-orange-700 text-gray-900 font-semibold shadow-[inset_-1px_-1px_0_rgba(255,255,255,0.9),inset_1px_1px_2px_rgba(0,0,0,0.25),0_0_0_1px_rgba(255,165,0,0.3)]'
                            : '!bg-gradient-to-b from-blue-100 via-blue-50 to-white border-blue-500 text-gray-900 hover:from-blue-200 hover:via-blue-100 hover:to-blue-50 active:from-blue-200 active:to-blue-100 shadow-[inset_-1px_-1px_0_rgba(255,255,255,0.9),inset_1px_1px_0_rgba(0,0,0,0.15)]'"
                        x-on:click="(() => { const desktopData = Alpine.$data(document.querySelector('main')); const el = document.getElementById(w.id); if (desktopData) { desktopData.focusedApp = w.id; const z = ++desktopData.zCounter; if (el) el.style.zIndex = z; } })()"
                        x-bind:title="w.title"
                        style="font-family: 'Tahoma', 'MS Sans Serif', sans-serif;"
                    >
                        <span className="text-sm flex-shrink-0" x-text="(w.title.match(/[\p{Emoji_Presentation}\p{Extended_Pictographic}]/u) || ['📄'])[0]"></span>
                        <span className="truncate font-medium" x-text="w.title.replace(/[\p{Emoji_Presentation}\p{Extended_Pictographic}]/gu, '').trim()"></span>
                    </button>
                </template>
            </div>
            <SystemTray />
        </section>
    );
}

function StartButton() {
    return (
        <button
            type="button"
            className="h-full m-0 px-4 !bg-gradient-to-b from-green-500 via-green-600 to-green-700
            border-r border-green-900 hover:from-green-400 hover:via-green-500 hover:to-green-600
            active:from-green-700 active:to-green-500 flex items-center gap-2
            text-white font-bold text-[13px] shadow-[inset_-1px_0_0_rgba(255,255,255,0.3),inset_1px_0_0_rgba(0,0,0,0.2)]
            transition-all duration-150"
            style="font-family: 'Tahoma', 'MS Sans Serif', sans-serif; text-shadow: 0 1px 2px rgba(0,0,0,0.4);"
        >
            <span className="text-base">🪟</span>
            <span className="tracking-tight">start</span>
        </button>
    );
}

function UserButton() {
    return (
        <button
            type="button"
            className="h-full m-0 px-3 !bg-gradient-to-b from-orange-600 via-orange-700 to-orange-800
            border-r border-orange-900 hover:from-orange-500 hover:via-orange-600 hover:to-orange-700
            active:from-orange-800 active:to-orange-600 flex items-center gap-1.5
            text-white font-semibold text-[11px] shadow-[inset_-1px_0_0_rgba(255,255,255,0.2),inset_1px_0_0_rgba(0,0,0,0.2)]
            transition-all duration-150"
            style="font-family: 'Tahoma', 'MS Sans Serif', sans-serif; text-shadow: 0 1px 1px rgba(0,0,0,0.3);"
            x-on:click="if(Clerk.isSignedIn) { Clerk.openUserProfile() } else { Clerk.openSignIn() }"
        >
            <span className="text-sm">👤</span>
            <span className="max-w-[100px] truncate" x-text="(user && (user.firstName || user.fullName)) || 'user'"></span>
        </button>
    );
}

function SystemTray() {
    return (
        <div
            className="flex items-center h-full px-3 bg-gradient-to-b from-cyan-500
            to-cyan-700 border-l-2 border-l-cyan-900 gap-2"
        >
            <span
                className="cursor-pointer hover:bg-cyan-400/50 p-1 rounded transition-colors"
                title="Analytics debug"
                x-data="{ dbg: false }"
                x-on:click="dbg = !dbg; if (window.posthog) posthog.debug(dbg);"
                x-text="dbg ? '🐞' : '📈'"
            ></span>
            <div className="flex items-center gap-2 text-white text-[11px] font-medium">
                <span 
                    className="bg-purple-600/60 px-1.5 py-0.5 rounded border border-purple-800/70 shadow-sm" 
                    title="Session duration"
                    style="text-shadow: 0 1px 1px rgba(0,0,0,0.3);"
                    x-text="Math.floor((analytics?.getSessionDuration?.()||0)/60000) + 'm'"
                ></span>
                <span 
                    className="bg-emerald-600/60 px-1.5 py-0.5 rounded border border-emerald-800/70 max-w-[60px] truncate shadow-sm" 
                    title="Current model"
                    style="text-shadow: 0 1px 1px rgba(0,0,0,0.3);"
                    x-text="window.currentModel || 'auto'"
                ></span>
            </div>
            <div className="border-l border-cyan-900 pl-2 h-5 flex items-center">
                <span 
                    className="font-mono text-[11px] text-white font-semibold bg-cyan-800/40 px-2 py-0.5 rounded border border-cyan-900/60 shadow-sm"
                    style="text-shadow: 0 1px 1px rgba(0,0,0,0.3);"
                >
                    {new Date().toLocaleTimeString([], {
                        hour: "2-digit",
                        minute: "2-digit",
                    })}
                </span>
            </div>
        </div>
    );
}

export function TaskbarButton({
    children,
    active = false,
    onClick,
}: PropsWithChildren<{
    active?: boolean;
    onClick?: () => void;
}>) {
    return (
        <button
            type="button"
            onClick={onClick}
            className={`
                h-5 px-2 text-xs border border-gray-400 truncate max-w-[180px] text-left
                bg-gradient-to-b
                ${
                    active
                        ? "from-blue-300 to-blue-500 border-blue-600 text-white shadow-inner"
                        : "from-gray-100 to-gray-300 hover:from-gray-200 hover:to-gray-400 active:from-gray-300 active:to-gray-100 text-black shadow-sm"
                }
            `}
            style="font-family: 'MS Sans Serif', sans-serif;"
        >
            {children}
        </button>
    );
}
