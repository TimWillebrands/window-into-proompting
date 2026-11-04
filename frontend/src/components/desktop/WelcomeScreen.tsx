import type { ReactNode } from 'react';
import { redirectToSignIn } from '../../lib/clerk-session';
import { usePlayerSession } from '../../lib/world';

const CHOICE =
    'flex w-72 items-center gap-3 rounded-md border-0 bg-transparent p-2 text-left text-lg text-white hover:bg-[#2c5bd3]';

interface LoginShellProps {
    prompt: string;
    blurb: string;
    children: ReactNode;
}

/** The XP login frame: greeting on the left, choices on the right. */
export function LoginShell({ prompt, blurb, children }: LoginShellProps) {
    return (
        <div
            className="fixed inset-0 flex flex-col text-white"
            style={{ background: '#5a7edc', fontFamily: 'Tahoma, sans-serif' }}
        >
            <div style={{ height: 80, background: '#00309c' }} />
            <div
                style={{
                    height: 2,
                    background:
                        'linear-gradient(90deg, #00309c, #ffffff 50%, #00309c)',
                }}
            />
            <main className="flex flex-1 items-center justify-center">
                <section className="flex w-1/2 flex-col items-end gap-3 pr-10 text-right">
                    <div className="text-4xl font-bold italic">Partytown</div>
                    <p className="text-lg">{prompt}</p>
                    <p className="max-w-xs text-xs opacity-80">{blurb}</p>
                </section>
                <div
                    className="h-80 w-px"
                    style={{
                        background:
                            'linear-gradient(transparent, #fff, transparent)',
                    }}
                />
                <section className="flex w-1/2 flex-col gap-2 pl-10">
                    {children}
                </section>
            </main>
            <div
                style={{
                    height: 2,
                    background:
                        'linear-gradient(90deg, #00309c, #f99736 50%, #00309c)',
                }}
            />
            <footer
                className="px-6 py-4 text-sm"
                style={{ height: 80, background: '#00309c' }}
            >
                After you sign in, the personas may message you on their own.
            </footer>
        </div>
    );
}

/** XP-style login: sign in to live here, or look around as a visitor. */
export default function WelcomeScreen() {
    const visit = usePlayerSession((s) => s.visit);

    return (
        <LoginShell
            prompt="To begin, sign in"
            blurb="Everyone who signs in lives in the same world. The personas will remember you."
        >
            <button type="button" onClick={redirectToSignIn} className={CHOICE}>
                Sign in
            </button>
            <button type="button" onClick={visit} className={CHOICE}>
                Just look around
            </button>
        </LoginShell>
    );
}
