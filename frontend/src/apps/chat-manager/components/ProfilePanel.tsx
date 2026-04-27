import { NEW_MEMBERS, PROFILE_STUB, RECENT_ACTIVITY } from './stub-data';
import WaveDecoration from './WaveDecoration';

export default function ProfilePanel() {
    return (
        <aside className="relative hidden @[1000px]:flex h-full w-[300px] shrink-0 flex-col gap-2 overflow-hidden bg-gradient-to-b from-blue-600 via-indigo-600 to-fuchsia-700 dark:from-sky-950 dark:via-indigo-950 dark:to-purple-950 p-2 text-slate-800 dark:text-slate-100">
            <WaveDecoration variant="panel" />

            {/* Profile hero card */}
            <section className="relative overflow-hidden rounded-3xl bg-gradient-to-br from-sky-300 via-blue-300 to-indigo-400 dark:from-sky-800 dark:via-blue-900 dark:to-indigo-900 p-5 shadow-[0_10px_30px_-8px_rgba(31,55,148,0.45)] ring-1 ring-white/60">
                <span
                    aria-hidden
                    className="pointer-events-none absolute inset-x-0 top-0 h-1/2 rounded-t-3xl bg-gradient-to-b from-white/40 to-transparent"
                />
                <span
                    aria-hidden
                    className="pointer-events-none absolute -right-10 -top-12 h-32 w-32 rounded-full bg-white/40 blur-2xl"
                />
                <WaveDecoration variant="profile" />
                <div className="relative flex flex-col items-center text-center">
                    <div className="relative">
                        <span
                            aria-hidden
                            className="absolute inset-0 -m-2 rounded-full bg-white/30 blur-xl"
                        />
                        <img
                            alt={PROFILE_STUB.name}
                            src={`https://robohash.org/${PROFILE_STUB.avatarSeed}.png?size=160x160`}
                            className="relative h-24 w-24 rounded-full ring-4 ring-white/90 dark:ring-slate-700 bg-white shadow-2xl"
                        />
                        <span
                            className={`absolute right-1 bottom-1 h-3.5 w-3.5 rounded-full ring-2 ring-white ${
                                PROFILE_STUB.online
                                    ? 'bg-emerald-500 shadow-[0_0_8px_rgba(16,185,129,0.9)]'
                                    : 'bg-slate-400'
                            }`}
                        />
                    </div>
                    <div className="mt-3 text-lg font-bold text-slate-900 dark:text-slate-50 drop-shadow-sm">
                        {PROFILE_STUB.name}
                    </div>
                    <div className="text-xs text-slate-700 dark:text-slate-300">
                        {PROFILE_STUB.handle}
                    </div>
                </div>
            </section>

            {/* Members + activity floating plate */}
            <div className="relative flex flex-1 min-h-0 flex-col gap-4 overflow-hidden rounded-3xl bg-gradient-to-b from-white/90 to-white/75 dark:from-slate-800/80 dark:to-slate-900/70 shadow-[0_10px_30px_-8px_rgba(31,55,148,0.4)] ring-1 ring-white/70 backdrop-blur-xl">
                <span
                    aria-hidden
                    className="pointer-events-none absolute inset-x-0 top-0 h-20 rounded-t-3xl bg-gradient-to-b from-white/60 to-transparent"
                />
                <div className="relative flex flex-1 min-h-0 flex-col gap-4 overflow-y-auto px-3 py-4">
                    <section>
                        <ListHeader title="New Members" />
                        <ul className="flex flex-col gap-2">
                            {NEW_MEMBERS.map((m) => (
                                <li
                                    key={m.id}
                                    className="flex items-center gap-3 rounded-2xl bg-white/85 dark:bg-slate-800/60 p-2 shadow-[0_2px_6px_-2px_rgba(31,55,148,0.18)] ring-1 ring-white/80 dark:ring-slate-700"
                                >
                                    <div className="relative shrink-0">
                                        <img
                                            alt={m.name}
                                            src={`https://robohash.org/${m.avatarSeed}.png?size=80x80`}
                                            className="h-10 w-10 rounded-full bg-white ring-1 ring-white"
                                        />
                                        <span
                                            className={`absolute -right-0 -bottom-0 h-2.5 w-2.5 rounded-full ring-2 ring-white dark:ring-slate-800 ${
                                                m.online
                                                    ? 'bg-emerald-500 shadow-[0_0_4px_rgba(16,185,129,0.7)]'
                                                    : 'bg-slate-400'
                                            }`}
                                        />
                                    </div>
                                    <div className="min-w-0 flex-1">
                                        <div className="truncate text-[13px] font-semibold text-slate-900 dark:text-slate-100">
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

                    <section>
                        <ListHeader title="Recent Activity" />
                        <ul className="flex flex-col gap-2">
                            {RECENT_ACTIVITY.map((a) => (
                                <li
                                    key={a.id}
                                    className="flex items-center gap-3 rounded-2xl bg-white/85 dark:bg-slate-800/60 p-2 shadow-[0_2px_6px_-2px_rgba(31,55,148,0.18)] ring-1 ring-white/80 dark:ring-slate-700"
                                >
                                    <img
                                        alt={a.name}
                                        src={`https://robohash.org/${a.avatarSeed}.png?size=80x80`}
                                        className="h-10 w-10 shrink-0 rounded-full bg-white ring-1 ring-white"
                                    />
                                    <div className="min-w-0 flex-1">
                                        <div className="truncate text-[13px] font-semibold text-slate-900 dark:text-slate-100">
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
                </div>
            </div>
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
