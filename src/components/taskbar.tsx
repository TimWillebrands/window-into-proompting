import type { PropsWithChildren } from "hono/jsx";

export function Taskbar({ children }: PropsWithChildren<unknown>) {
    return (
        <section
            id="taskbar"
            className="fixed bottom-0 left-0 right-0 h-7 bg-gradient-to-b
            from-blue-600 to-blue-800 border-t border-blue-900 flex
            items-center z-50 shadow-lg"
        >
            <StartButton />
            <div className="flex-1 flex items-center px-1 space-x-1 h-full">
                {children}
            </div>
            <SystemTray />
        </section>
    );
}

function StartButton() {
    return (
        <div
            className="h-full m-0 px-3 bg-gradient-to-b from-green-400 to-green-600
            border border-green-700 hover:from-green-300 hover:to-green-500
            active:from-green-600 active:to-green-400 flex items-center space-x-1
            text-white font-bold text-sm rounded-sm shadow-sm"
            style="font-family: 'MS Sans Serif', sans-serif;"
        >
            <span className="text-xs">🪟</span>
            <span>start</span>
        </div>
    );
}

function SystemTray() {
    return (
        <div
            className="flex items-center h-full px-2 bg-gradient-to-b from-blue-500
            to-blue-700 border-l border-blue-900 min-w-[120px]"
        >
            <div className="flex items-center space-x-2 text-white text-xs">
                <div className="flex items-center space-x-1">
                    <span
                        title="Network"
                        className="cursor-pointer hover:bg-blue-600 p-1 rounded"
                    >
                        📶
                    </span>
                    <span
                        title="Sound"
                        className="cursor-pointer hover:bg-blue-600 p-1 rounded"
                    >
                        🔊
                    </span>
                </div>
                <div className="border-l border-blue-900 pl-2 h-5 flex items-center">
                    <span className="font-mono text-xs text-white">
                        {new Date().toLocaleTimeString([], {
                            hour: "2-digit",
                            minute: "2-digit",
                        })}
                    </span>
                </div>
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
