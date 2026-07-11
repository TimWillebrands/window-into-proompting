import { useMemo, useState } from 'react';
import { useGetPersonaSuspense } from '#api/party-zone';
import GeneratePersonaDialog from './components/GeneratePersonaDialog';
import NewPersonaButton from './components/NewPersonaButton';
import PersonaEditor from './components/PersonaEditor';
import TemplateButton from './components/TemplateButton';

export default function PersonasApp() {
    const personasQuery = useGetPersonaSuspense();
    const personas = useMemo(() => {
        const data = personasQuery.data?.data;
        return Array.isArray(data) ? data : [];
    }, [personasQuery.data]);
    const [selectedId, setSelectedId] = useState<string | null>(null);
    const [showGenerateDialog, setShowGenerateDialog] = useState(false);

    const selectedPersona = useMemo(
        () => personas.find((p) => p.id === selectedId) ?? null,
        [personas, selectedId],
    );

    return (
        <div
            className="app-surface flex h-full"
            style={{ background: 'transparent' }}
        >
            {/* Sidebar */}
            <div
                className="xp-glass-panel flex flex-col"
                style={{
                    width: '210px',
                    borderRight: '1px solid rgba(255,255,255,0.7)',
                }}
            >
                <div className="bg-gradient-to-b from-xp-section-from to-xp-section-to text-white text-[11px] font-semibold px-2 py-[3px] flex items-center justify-between shadow-[inset_0_1px_0_rgba(255,255,255,0.35)]">
                    <span>Personas</span>
                    <span
                        className="xp-glass-chip"
                        style={{ color: '#003399', fontWeight: 700 }}
                    >
                        {personas.length}
                    </span>
                </div>
                <div className="flex-1 overflow-y-auto py-1">
                    {personasQuery.isError ? (
                        <p style={{ color: '#c00', padding: '8px' }}>
                            Could not load personas. Check that the backend is
                            running, then reopen this window.
                        </p>
                    ) : null}
                    {personas.length === 0 && !personasQuery.isError ? (
                        <p style={{ color: '#666', padding: '10px 12px' }}>
                            No personas yet. Create one below — it can then join
                            chat rooms and speak on its own.
                        </p>
                    ) : null}
                    {personas.map((persona) => {
                        const active = selectedId === persona.id;
                        return (
                            <button
                                key={persona.id}
                                type="button"
                                className={`xp-bare xp-glass-row ${active ? 'active' : ''}`}
                                onClick={() =>
                                    setSelectedId(persona.id ?? null)
                                }
                            >
                                <img
                                    src={`https://robohash.org/${persona.id}.png?size=24x24`}
                                    alt=""
                                    style={{
                                        width: 24,
                                        height: 24,
                                        borderRadius: '50%',
                                        background: '#fff',
                                        boxShadow:
                                            '0 0 0 1px rgba(255,255,255,0.9), 0 1px 3px rgba(31,55,148,0.25)',
                                        flexShrink: 0,
                                    }}
                                />
                                <div style={{ minWidth: 0 }}>
                                    <div
                                        style={{
                                            fontWeight: 600,
                                            overflow: 'hidden',
                                            textOverflow: 'ellipsis',
                                            whiteSpace: 'nowrap',
                                        }}
                                    >
                                        {persona.name}
                                    </div>
                                </div>
                            </button>
                        );
                    })}
                </div>
                <div
                    style={{
                        borderTop: '1px solid rgba(255,255,255,0.7)',
                        boxShadow: 'inset 0 1px 0 rgba(31,55,148,0.08)',
                        padding: '6px',
                        display: 'flex',
                        flexDirection: 'column',
                        gap: '3px',
                    }}
                >
                    <div
                        style={{
                            fontSize: 10,
                            fontWeight: 700,
                            color: '#003399',
                            textTransform: 'uppercase',
                            letterSpacing: '0.06em',
                            padding: '0 2px 2px',
                        }}
                    >
                        Create a persona
                    </div>
                    <NewPersonaButton onCreated={(id) => setSelectedId(id)} />
                    <TemplateButton onCreated={(id) => setSelectedId(id)} />
                    <button
                        type="button"
                        className="w-full"
                        title="Describe the persona in a sentence and let the AI write the name, bio and system prompt"
                        onClick={() => setShowGenerateDialog(true)}
                    >
                        Generate with AI...
                    </button>
                </div>
            </div>

            {/* Editor pane */}
            <div className="flex-1 overflow-y-auto p-3">
                {selectedPersona ? (
                    <PersonaEditor
                        key={selectedPersona.id}
                        persona={selectedPersona}
                        onDeleted={() => setSelectedId(null)}
                    />
                ) : (
                    <div className="h-full flex flex-col items-center justify-center">
                        <div
                            className="xp-glass-card"
                            style={{
                                maxWidth: 340,
                                padding: '20px 24px',
                                textAlign: 'center',
                            }}
                        >
                            <div style={{ fontSize: 28, marginBottom: 6 }}>
                                🎭
                            </div>
                            <p
                                style={{
                                    fontWeight: 700,
                                    fontSize: 12,
                                    margin: '0 0 4px',
                                }}
                            >
                                Personas are the AI characters of this party
                            </p>
                            <p style={{ color: '#555', margin: 0 }}>
                                Each one has a name, a bio, and a system prompt
                                that shapes how it speaks in chat rooms. Select
                                one on the left to edit it, or create a new one
                                below the list.
                            </p>
                        </div>
                    </div>
                )}
            </div>

            {showGenerateDialog && (
                <GeneratePersonaDialog
                    onClose={() => setShowGenerateDialog(false)}
                    onCreated={(id) => {
                        setSelectedId(id);
                        setShowGenerateDialog(false);
                    }}
                />
            )}
        </div>
    );
}
