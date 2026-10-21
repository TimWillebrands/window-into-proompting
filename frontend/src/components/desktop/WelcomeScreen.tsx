import { useState } from 'react';
import { useGetWorldsPartyIdLives } from '#api/party-zone';
import { usePlayerSession, WORLD_ID } from '../../lib/world';

const avatar = (name: string) =>
    `https://robohash.org/${encodeURIComponent(name)}?size=64x64&set=set5`;

/** XP-style login: pick (or create) the human you are in this World. */
export default function WelcomeScreen() {
    const signIn = usePlayerSession((s) => s.signIn);
    const lives = useGetWorldsPartyIdLives(WORLD_ID);
    const humans = (lives.data?.data ?? []).filter((l) => l.driver === 'User');
    const [name, setName] = useState('');

    const submit = (e: React.FormEvent) => {
        e.preventDefault();
        if (name.trim()) signIn(name.trim());
    };

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
                    <p className="text-lg">To begin, click your user name</p>
                    <p className="max-w-xs text-xs opacity-80">
                        Everyone who signs in lives in the same world. The
                        personas will remember you.
                    </p>
                </section>
                <div
                    className="h-80 w-px"
                    style={{
                        background:
                            'linear-gradient(transparent, #fff, transparent)',
                    }}
                />
                <section className="flex w-1/2 flex-col gap-2 pl-10">
                    {humans.map((h) => (
                        <button
                            key={h.personaId}
                            type="button"
                            onClick={() => signIn(h.name)}
                            className="flex w-72 items-center gap-3 rounded-md border-0 bg-transparent p-2 text-left text-lg text-white hover:bg-[#2c5bd3]"
                        >
                            <img
                                src={avatar(h.name)}
                                alt=""
                                className="h-14 w-14 rounded border-2 border-white bg-white"
                            />
                            {h.name}
                        </button>
                    ))}
                    <form
                        onSubmit={submit}
                        className="flex w-72 items-center gap-2 p-2"
                    >
                        <input
                            value={name}
                            onChange={(e) => setName(e.target.value)}
                            placeholder="New here? Type your name"
                            maxLength={40}
                            className="flex-1 text-black"
                            aria-label="Your name"
                        />
                        <button type="submit" disabled={!name.trim()}>
                            Go
                        </button>
                    </form>
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
