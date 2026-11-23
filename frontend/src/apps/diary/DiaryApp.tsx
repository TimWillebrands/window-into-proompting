import {
    ComboBox,
    Post,
    StatusBar,
    StatusBarField,
    SunkenField,
    Text,
} from '@partytown/ui';
import { type ReactNode, useEffect, useId, useState } from 'react';
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

export default function DiaryApp({ personaId }: { personaId?: string }) {
    const livesQuery = useGetWorldsPartyIdLivesSuspense(WORLD_ID);
    const personas = (livesQuery.data.data ?? []).filter(
        (l) => l.driver === 'LLM',
    );
    const { setWindowProps } = useDesktopContext();
    const { windowId } = useWindowInstance();
    const pickerId = useId();
    const [selectedId, setSelectedId] = useState<string | null>(
        personaId ?? personas[0]?.personaId ?? null,
    );

    useEffect(() => {
        if (personaId) setSelectedId(personaId);
    }, [personaId]);

    const select = (id: string) => {
        setSelectedId(id);
        setWindowProps(windowId, (p) => ({ ...p, personaId: id }));
    };

    const selected = personas.find((p) => p.personaId === selectedId);

    return (
        <div className="flex h-full flex-col">
            <div className="flex items-center gap-3 px-2 py-1">
                <label htmlFor={pickerId}>Diary of:</label>
                <ComboBox
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
                </ComboBox>
                <span className="ml-auto">Read-only</span>
            </div>
            {selectedId ? (
                <DiaryPage
                    personaId={selectedId}
                    name={selected?.name ?? 'Unknown'}
                />
            ) : (
                <SunkenField text="reading" className="mx-1 mb-1 flex-1">
                    <Note>No AI personas in this world yet.</Note>
                </SunkenField>
            )}
        </div>
    );
}

function DiaryPage({ personaId, name }: { personaId: string; name: string }) {
    const diaryQuery = useGetWorldsPartyIdDiaryPersonaId(WORLD_ID, personaId);
    const entries = diaryQuery.data?.data ?? [];

    return (
        <>
            <SunkenField
                text="reading"
                role="region"
                aria-label={`Diary of ${name}`}
                tabIndex={0}
                className="mx-1 flex-1"
            >
                {diaryQuery.isPending ? <Note>Loading…</Note> : null}
                {diaryQuery.isError ? (
                    <Note>Could not open this diary.</Note>
                ) : null}
                {diaryQuery.isSuccess && entries.length === 0 ? (
                    <Note>{name} hasn't written anything yet.</Note>
                ) : null}
                {entries.map((entry) => (
                    <Post
                        key={entry.id}
                        variant="flat"
                        title={formatDate(entry.createdAt)}
                        meta={entry.mood ? `mood: ${entry.mood}` : undefined}
                    >
                        {entry.body}
                    </Post>
                ))}
            </SunkenField>
            <StatusBar>
                <StatusBarField grow>{name}.txt</StatusBarField>
                <StatusBarField>
                    {entries.length}{' '}
                    {entries.length === 1 ? 'entry' : 'entries'}
                </StatusBarField>
            </StatusBar>
        </>
    );
}

function Note({ children }: { children: ReactNode }) {
    return (
        <Text as="p" tone="muted" className="px-3 py-2">
            {children}
        </Text>
    );
}
