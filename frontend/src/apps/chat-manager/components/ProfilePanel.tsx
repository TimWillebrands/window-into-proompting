import { NEW_MEMBERS, PROFILE_STUB, RECENT_ACTIVITY } from './stub-data';

export default function ProfilePanel() {
    return (
        <aside className="hidden lg:flex h-full w-[300px] shrink-0 flex-col gap-5 overflow-y-auto bg-gradient-to-b from-sky-100 via-indigo-100 to-purple-100 dark:from-sky-950 dark:via-indigo-950 dark:to-purple-950 px-4 py-5 text-slate-800 dark:text-slate-100">
            {/* Profile card */}
            <section className="relative overflow-hidden rounded-3xl bg-gradient-to-br from-sky-200 via-blue-200 to-indigo-300 dark:from-sky-800 dark:via-blue-900 dark:to-indigo-900 p-5 shadow-md">
                <span
                    aria-hidden
                    className="pointer-events-none absolute -right-10 -top-12 h-32 w-32 rounded-full bg-white/40 blur-2xl"
                />
                <span
                    aria-hidden
                    className="pointer-events-none absolute right-3 top-3 text-xl"
                >
                    ✨
                </span>
                <div className="relative flex flex-col items-center text-center">
                    <img
                        alt={PROFILE_STUB.name}
                        src={`https://robohash.org/${PROFILE_STUB.avatarSeed}.png?size=120x120`}
                        className="h-24 w-24 rounded-full ring-4 ring-white dark:ring-slate-700 bg-white shadow-lg"
                    />
                    <div className="mt-3 text-lg font-bold text-slate-900 dark:text-slate-50">
                        {PROFILE_STUB.name}
                    </div>
                    <div className="text-xs text-slate-700 dark:text-slate-300">
                        {PROFILE_STUB.handle}
                    </div>
                </div>
            </section>

            {/* New Members */}
            <section>
                <ListHeader title="New Members" />
                <ul className="flex flex-col gap-2">
                    {NEW_MEMBERS.map((m) => (
                        <li
                            key={m.id}
                            className="flex items-center gap-3 rounded-xl bg-white/60 dark:bg-slate-800/60 p-2 shadow-sm"
                        >
                            <div className="relative shrink-0">
                                <img
                                    alt={m.name}
                                    src={`https://robohash.org/${m.avatarSeed}.png?size=40x40`}
                                    className="h-9 w-9 rounded-full bg-white"
                                />
                                <span
                                    className={`absolute -right-0 -bottom-0 h-2.5 w-2.5 rounded-full ring-2 ring-white dark:ring-slate-800 ${
                                        m.online
                                            ? 'bg-emerald-500'
                                            : 'bg-slate-400'
                                    }`}
                                />
                            </div>
                            <div className="min-w-0 flex-1">
                                <div className="truncate text-[13px] font-semibold">
                                    {m.name}
                                </div>
                                <div className="text-[11px] text-slate-500 dark:text-slate-400">
                                    {m.lastSeen}
                                </div>
                            </div>
                        </li>
                    ))}
                </ul>
            </section>

            {/* Recent Activity */}
            <section>
                <ListHeader title="Recent Activity" />
                <ul className="flex flex-col gap-2">
                    {RECENT_ACTIVITY.map((a) => (
                        <li
                            key={a.id}
                            className="flex items-center gap-3 rounded-xl bg-white/60 dark:bg-slate-800/60 p-2 shadow-sm"
                        >
                            <img
                                alt={a.name}
                                src={`https://robohash.org/${a.avatarSeed}.png?size=40x40`}
                                className="h-9 w-9 shrink-0 rounded-full bg-white"
                            />
                            <div className="min-w-0 flex-1">
                                <div className="truncate text-[13px] font-semibold">
                                    {a.name}
                                </div>
                                <div className="truncate text-[11px] text-slate-500 dark:text-slate-400">
                                    {a.action} · {a.when}
                                </div>
                            </div>
                        </li>
                    ))}
                </ul>
            </section>
        </aside>
    );
}

function ListHeader({ title }: { title: string }) {
    return (
        <div className="mb-2 flex items-center justify-between px-1">
            <h3 className="text-sm font-bold text-slate-900 dark:text-slate-100">
                {title}
            </h3>
            <button
                type="button"
                disabled
                className="text-xs font-semibold text-blue-600 dark:text-blue-400 cursor-not-allowed opacity-70"
            >
                See all
            </button>
        </div>
    );
}
