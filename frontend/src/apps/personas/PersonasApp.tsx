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
            style={{ background: '#ECE9D8' }}
        >
            {/* Sidebar */}
            <div
                className="flex flex-col"
                style={{ width: '200px', borderRight: '1px solid #ACA899' }}
            >
                <div className="xp-section-header flex items-center justify-between">
                    <span>Personas</span>
                    <span
                        style={{
                            background: '#fff',
                            color: '#003399',
                            borderRadius: '8px',
                            padding: '0 5px',
                            fontSize: '10px',
                            fontWeight: 700,
                        }}
                    >
                        {personas.length}
                    </span>
                </div>
                <div className="flex-1 overflow-y-auto">
                    {personasQuery.isLoading ? (
                        <p style={{ color: '#808080', padding: '8px' }}>
                            Loading...
                        </p>
                    ) : null}
                    {personasQuery.isError ? (
                        <p style={{ color: '#c00', padding: '8px' }}>
                            Failed to load.
                        </p>
                    ) : null}
                    {personas.map((persona) => (
                        <button
                            key={persona.id}
                            type="button"
                            className="w-full text-left flex items-center gap-2 p-2"
                            style={{
                                background:
                                    selectedId === persona.id
                                        ? '#316AC5'
                                        : 'transparent',
                                color:
                                    selectedId === persona.id ? '#fff' : '#000',
                                border: 'none',
                                borderBottom: '1px solid #D6D2C2',
                            }}
                            onClick={() => setSelectedId(persona.id ?? null)}
                        >
                            <img
                                src={`https://robohash.org/${persona.id}.png?size=24x24`}
                                alt=""
                                style={{
                                    width: 24,
                                    height: 24,
                                    borderRadius: '50%',
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
                    ))}
                </div>
                <div
                    style={{
                        borderTop: '1px solid #ACA899',
                        padding: '4px',
                        display: 'flex',
                        flexDirection: 'column',
                        gap: '3px',
                    }}
                >
                    <NewPersonaButton onCreated={(id) => setSelectedId(id)} />
                    <TemplateButton onCreated={(id) => setSelectedId(id)} />
                    <button
                        type="button"
                        className="w-full"
                        onClick={() => setShowGenerateDialog(true)}
                    >
                        Generate Persona...
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
                    <div
                        className="h-full flex flex-col items-center justify-center"
                        style={{ color: '#808080' }}
                    >
                        <p>Select a persona to edit, or create a new one.</p>
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
