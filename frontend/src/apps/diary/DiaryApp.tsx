import { useEffect, useId, useState } from 'react';
import {
    useGetWorldsPartyIdDiaryPersonaId,
    useGetWorldsPartyIdLivesSuspense,
} from '#api/party-zone';
import { useDesktopContext } from '#lib/desktop-context';
import { useWindowInstance } from '../../components/desktop/window-instance-context';
import { WORLD_ID } from '../../lib/world';

const formatDate = (ts: number | string | undefined) =>
    ts == null
        ? ''
        : new Date(Number(ts)).toLocaleString([], {
              weekday: 'short',
              year: 'numeric',
              month: 'short',
              day: 'numeric',
              hour: '2-digit',
              minute: '2-digit',
          });

/** Diaries — Notepad: read-only view of one persona's private journal. */
export default function DiaryApp({ personaId }: { personaId?: string }) {
    const livesQuery = useGetWorldsPartyIdLivesSuspense(WORLD_ID);
    const personas = (livesQuery.data.data ?? []).filter(
        (l) => !l.isPlayer && l.driver === 'LLM',
    );
    const { setWindowProps } = useDesktopContext();
    const { windowId } = useWindowInstance();
    const pickerId = useId();
    const [selectedId, setSelectedId] = useState<string | null>(
        personaId ?? personas[0]?.personaId ?? null,
    );

    // Follow external opens (World Monitor → Diary) on an already-open window.
    useEffect(() => {
        if (personaId) setSelectedId(personaId);
    }, [personaId]);

    const select = (id: string) => {
        setSelectedId(id);
        setWindowProps(windowId, (p) => ({ ...p, personaId: id }));
    };

    const selected = personas.find((p) => p.personaId === selectedId);

    return (
        <div className="app-surface flex flex-col h-full">
            <div
                className="flex items-center"
                style={{
                    gap: 6,
                    padding: '3px 6px',
                    background: 'var(--color-xp-face)',
                    borderBottom: '1px solid #aca899',
                }}
            >
                <label htmlFor={pickerId}>Diary of:</label>
                <select
                    id={pickerId}
                    value={selectedId ?? ''}
                    onChange={(e) => select(e.currentTarget.value)}
                >
                    {selectedId ? null : (
                        <option value="">— pick a persona —</option>
                    )}
                    {personas.map((p) => (
                        <option key={p.personaId} value={p.personaId}>
                            {p.name}
                        </option>
                    ))}
                </select>
                <span style={{ marginLeft: 'auto', color: '#666' }}>
                    Read-only
                </span>
            </div>
            {selectedId ? (
                <DiaryPage
                    personaId={selectedId}
                    name={selected?.name ?? 'Unknown'}
                />
            ) : (
                <div
                    className="flex-1"
                    style={{ background: '#fff', padding: 12, color: '#666' }}
                >
                    No AI personas in this world yet.
                </div>
            )}
        </div>
    );
}

function DiaryPage({ personaId, name }: { personaId: string; name: string }) {
    const diaryQuery = useGetWorldsPartyIdDiaryPersonaId(WORLD_ID, personaId);
    const entries = diaryQuery.data?.data ?? [];

    return (
        <>
            <div
                className="flex-1 overflow-y-auto"
                style={{
                    background: '#fff',
                    border: '1px solid',
                    borderColor: '#7f9db9 #fff #fff #7f9db9',
                    padding: '6px 8px',
                    fontFamily: "'Lucida Console', 'Courier New', monospace",
                    fontSize: 12,
                    lineHeight: 1.45,
                    whiteSpace: 'pre-wrap',
                    wordBreak: 'break-word',
                }}
            >
                {diaryQuery.isPending ? 'Loading…' : null}
                {diaryQuery.isError ? 'Could not open this diary.' : null}
                {diaryQuery.isSuccess && entries.length === 0
                    ? `${name} hasn't written anything yet.`
                    : null}
                {entries.map((entry, i) => (
                    <div key={entry.id} style={{ marginBottom: 14 }}>
                        <div style={{ fontWeight: 700 }}>
                            {formatDate(entry.createdAt)}
                            {entry.mood ? `  —  mood: ${entry.mood}` : ''}
                        </div>
                        <div>{entry.body}</div>
                        {i < entries.length - 1 ? (
                            <div style={{ color: '#aaa', marginTop: 10 }}>
                                {'- '.repeat(30)}
                            </div>
                        ) : null}
                    </div>
                ))}
            </div>
            <div
                className="flex"
                style={{
                    gap: 2,
                    padding: 2,
                    background: 'var(--color-xp-face)',
                }}
            >
                <div
                    style={{
                        padding: '1px 6px',
                        border: '1px solid',
                        borderColor: '#aca899 #fff #fff #aca899',
                        flex: 1,
                    }}
                >
                    {name}.txt
                </div>
                <div
                    style={{
                        padding: '1px 6px',
                        border: '1px solid',
                        borderColor: '#aca899 #fff #fff #aca899',
                    }}
                >
                    {entries.length}{' '}
                    {entries.length === 1 ? 'entry' : 'entries'}
                </div>
            </div>
        </>
    );
}
