import { useEffect, useId, useRef, useState } from 'react';
import WindowFrame from '../../../components/desktop/WindowFrame';

interface CreateRoomDialogProps {
    open: boolean;
    creating: boolean;
    errorMessage?: string | null;
    onClose: () => void;
    onSubmit: (name: string, scenario: string) => void;
}

const SCENARIO_MAX = 2000;
const SCENARIO_SOFT_WARN = 1800;

export default function CreateRoomDialog({
    open,
    creating,
    errorMessage,
    onClose,
    onSubmit,
}: CreateRoomDialogProps) {
    const [name, setName] = useState('');
    const [scenario, setScenario] = useState('');
    const nameId = useId();
    const scenarioId = useId();
    const frameId = useId();
    const nameRef = useRef<HTMLInputElement>(null);

    useEffect(() => {
        if (!open) {
            setName('');
            setScenario('');
            return;
        }
        const t = window.setTimeout(() => nameRef.current?.focus(), 0);
        return () => window.clearTimeout(t);
    }, [open]);

    useEffect(() => {
        if (!open) return;
        const onKey = (e: KeyboardEvent) => {
            if (e.key === 'Escape' && !creating) {
                e.stopPropagation();
                onClose();
            }
        };
        window.addEventListener('keydown', onKey);
        return () => window.removeEventListener('keydown', onKey);
    }, [open, creating, onClose]);

    if (!open) return null;

    const trimmedName = name.trim();
    const canSubmit = !creating && trimmedName.length > 0;

    const handleSubmit = (e: React.FormEvent) => {
        e.preventDefault();
        if (!canSubmit) return;
        onSubmit(trimmedName, scenario.trim());
    };

    const handleBackdrop = () => {
        if (creating) return;
        if (trimmedName || scenario.trim()) return;
        onClose();
    };

    return (
        <div
            style={{
                position: 'absolute',
                inset: 0,
                display: 'flex',
                alignItems: 'center',
                justifyContent: 'center',
                zIndex: 200,
            }}
        >
            <button
                type="button"
                onClick={handleBackdrop}
                aria-label="Close dialog"
                tabIndex={-1}
                className="absolute inset-0 cursor-default bg-black/50"
            />
            <div
                role="dialog"
                aria-label="Create a Room"
                className="relative w-[440px] max-w-[92%] flex flex-col app-surface"
            >
                <WindowFrame
                    id={frameId}
                    title="Create a Room"
                    icon="🏠"
                    width={440}
                    height={0}
                    zIndex={200}
                    focused
                    onMinimize={onClose}
                    onClose={onClose}
                >
                    <form
                        onSubmit={handleSubmit}
                        className="flex flex-col gap-3 p-4"
                    >
                        <div className="flex flex-col gap-1">
                            <label
                                htmlFor={nameId}
                                className="text-[11px] font-bold"
                            >
                                Title
                            </label>
                            <input
                                ref={nameRef}
                                id={nameId}
                                type="text"
                                value={name}
                                onChange={(e) => setName(e.currentTarget.value)}
                                disabled={creating}
                                maxLength={120}
                                placeholder="e.g. Stealth Horticulture HQ"
                                className="w-full"
                            />
                        </div>

                        <div className="flex flex-col gap-1">
                            <div className="flex items-baseline justify-between">
                                <label
                                    htmlFor={scenarioId}
                                    className="text-[11px] font-bold"
                                >
                                    Scenario{' '}
                                    <span className="font-normal text-slate-500">
                                        (optional)
                                    </span>
                                </label>
                                {scenario.length > SCENARIO_SOFT_WARN && (
                                    <span
                                        className={`text-[10px] tabular-nums ${
                                            scenario.length >= SCENARIO_MAX
                                                ? 'text-red-600'
                                                : 'text-amber-600'
                                        }`}
                                    >
                                        {scenario.length}/{SCENARIO_MAX}
                                    </span>
                                )}
                            </div>
                            <textarea
                                id={scenarioId}
                                value={scenario}
                                onChange={(e) =>
                                    setScenario(e.currentTarget.value)
                                }
                                disabled={creating}
                                rows={5}
                                maxLength={SCENARIO_MAX}
                                placeholder="Set the scene — e.g. 'Office of a stealth horticulture startup, late at night.'"
                                className="w-full resize-none"
                            />
                        </div>

                        {errorMessage && (
                            <p
                                role="alert"
                                className="text-[11px] text-red-700 dark:text-red-300"
                            >
                                {errorMessage}
                            </p>
                        )}

                        <div className="flex justify-end gap-2 pt-1">
                            <button
                                type="button"
                                onClick={onClose}
                                disabled={creating}
                            >
                                Cancel
                            </button>
                            <button type="submit" disabled={!canSubmit}>
                                {creating ? 'Creating…' : 'Create Room'}
                            </button>
                        </div>
                    </form>
                </WindowFrame>
            </div>
        </div>
    );
}
