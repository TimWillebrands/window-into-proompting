import { useId, useState } from 'react';
import { ProblemDetailsError } from '../../api/custom-fetch';
import { usePlayerAccount, useSetAuto } from '../../lib/world';

const MAX_MISSION = 160;

/** Hands your character to the agent, who lives it like any Persona until you take it back. */
export default function AutoApp() {
    const { account } = usePlayerAccount();
    const setAuto = useSetAuto();
    const missionId = useId();
    const [mission, setMission] = useState(account?.mission ?? '');
    const [busy, setBusy] = useState(false);
    const [error, setError] = useState<string | null>(null);
    const on = account?.auto ?? false;
    const name = account?.name ?? 'Your character';

    const apply = async (nextOn: boolean) => {
        setBusy(true);
        setError(null);
        try {
            await setAuto(nextOn, mission.trim());
            if (!nextOn) setMission('');
        } catch (err) {
            setError(
                err instanceof ProblemDetailsError && err.detail
                    ? err.detail
                    : 'That did not work. Try again?',
            );
        }
        setBusy(false);
    };

    return (
        <form
            onSubmit={(e) => {
                e.preventDefault();
                void apply(true);
            }}
            className="flex h-full flex-col gap-2 p-3"
        >
            <p>
                {on
                    ? `${name} is living on their own. You can watch and read, but not act until you take over.`
                    : `Let ${name} live without you: they post, message, walk around and remember things, just like the Personas here.`}
            </p>
            <label htmlFor={missionId}>Give them a mission (optional)</label>
            <input
                id={missionId}
                value={mission}
                onChange={(e) => setMission(e.target.value)}
                maxLength={MAX_MISSION}
                autoComplete="off"
                placeholder="Find a job. Make Denise laugh."
            />
            <p className="text-xs opacity-70">
                They'll go after it their own way. Signed-in people can see it
                in Work & Wants. Taking over ends it.
            </p>
            {error && (
                <p role="alert" className="text-sm">
                    {error}
                </p>
            )}
            <div className="mt-auto flex justify-end gap-2">
                {on && (
                    <button
                        type="button"
                        disabled={busy}
                        onClick={() => void apply(false)}
                    >
                        Take over
                    </button>
                )}
                <button type="submit" disabled={busy}>
                    {busy
                        ? 'One moment…'
                        : on
                          ? 'Update mission'
                          : 'Go on auto'}
                </button>
            </div>
        </form>
    );
}
