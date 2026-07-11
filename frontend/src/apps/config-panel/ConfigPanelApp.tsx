import { useDesktopContext } from '#lib/desktop-context';
import { useNavHistoryStore } from '#lib/nav-history';
import ControlPanelHome from './components/ControlPanelHome';
import { SECTION_NAMES, SECTIONS } from './components/constants';
import LlmProvidersSection from './components/LlmProvidersSection';

export default function ConfigPanelApp() {
    const { stack, index } = useNavHistoryStore();
    const { focusWindow } = useDesktopContext();

    const currentEntry = stack[index];
    const section: string =
        currentEntry?.kind === 'config' ? currentEntry.section : 'home';

    const canGoBack = index > 0;
    const canGoForward = index < stack.length - 1;

    const handleBack = () => {
        const store = useNavHistoryStore.getState();
        const entry = store.back();
        if (entry?.kind === 'window') {
            focusWindow(entry.windowId);
        }
        store.clearNavigating();
    };

    const handleForward = () => {
        const store = useNavHistoryStore.getState();
        const entry = store.forward();
        if (entry?.kind === 'window') {
            focusWindow(entry.windowId);
        }
        store.clearNavigating();
    };

    const navigateTo = (s: string) => {
        useNavHistoryStore.getState().push({ kind: 'config', section: s });
    };

    const navigateHome = () => {
        useNavHistoryStore
            .getState()
            .push({ kind: 'window', windowId: 'config-panel' });
    };

    return (
        <div
            className="app-surface"
            style={{
                display: 'flex',
                flexDirection: 'column',
                height: '100%',
                background: 'transparent',
                overflow: 'hidden',
            }}
        >
            {/* Navigation toolbar */}
            <div
                className="xp-glass-panel"
                style={{
                    display: 'flex',
                    alignItems: 'center',
                    gap: 4,
                    padding: '3px 6px',
                    borderBottom: '1px solid rgba(255,255,255,0.7)',
                }}
            >
                <button
                    type="button"
                    disabled={!canGoBack}
                    onClick={handleBack}
                    style={{
                        minWidth: 60,
                        display: 'flex',
                        alignItems: 'center',
                        gap: 3,
                    }}
                >
                    <span style={{ fontSize: 10 }}>◀</span> Back
                </button>
                <button
                    type="button"
                    disabled={!canGoForward}
                    onClick={handleForward}
                    style={{
                        minWidth: 72,
                        display: 'flex',
                        alignItems: 'center',
                        gap: 3,
                    }}
                >
                    Forward <span style={{ fontSize: 10 }}>▶</span>
                </button>
                <div
                    style={{
                        flex: 1,
                        marginLeft: 8,
                        padding: '2px 8px',
                        background: 'rgba(255,255,255,0.85)',
                        border: '1px solid rgba(255,255,255,0.9)',
                        borderRadius: 999,
                        boxShadow:
                            'inset 0 1px 2px rgba(31,55,148,0.12), 0 1px 0 rgba(255,255,255,0.6)',
                        fontSize: 12,
                        color: '#000',
                    }}
                >
                    {SECTION_NAMES[section] ?? section}
                </div>
            </div>

            {/* Body */}
            <div style={{ display: 'flex', flex: 1, overflow: 'hidden' }}>
                {/* Sidebar */}
                <div
                    className="xp-glass-panel"
                    style={{
                        width: 170,
                        flexShrink: 0,
                        borderRight: '1px solid rgba(255,255,255,0.7)',
                        display: 'flex',
                        flexDirection: 'column',
                        overflow: 'hidden',
                    }}
                >
                    <div
                        style={{
                            background:
                                'linear-gradient(to bottom, #1B5EAD, #3A85C7)',
                            boxShadow: 'inset 0 1px 0 rgba(255,255,255,0.35)',
                            color: '#fff',
                            padding: '8px 10px',
                            fontWeight: 700,
                            fontSize: 13,
                            display: 'flex',
                            alignItems: 'center',
                            gap: 6,
                        }}
                    >
                        <span style={{ fontSize: 18 }}>⚙️</span>
                        Control Panel
                    </div>

                    <div
                        style={{
                            padding: '6px 8px 2px',
                            fontSize: 10,
                            fontWeight: 700,
                            color: '#003399',
                            textTransform: 'uppercase',
                            letterSpacing: '0.06em',
                        }}
                    >
                        Categories
                    </div>
                    <nav style={{ padding: '2px 0 6px' }}>
                        <button
                            type="button"
                            className={`xp-bare xp-glass-row ${section === 'home' ? 'active' : ''}`}
                            onClick={navigateHome}
                        >
                            <span aria-hidden>🏠</span>
                            <span style={{ fontWeight: 600 }}>Home</span>
                        </button>
                        {SECTIONS.map((s) => (
                            <button
                                key={s.id}
                                type="button"
                                className={`xp-bare xp-glass-row ${section === s.id ? 'active' : ''}`}
                                title={s.description}
                                onClick={() => navigateTo(s.id)}
                            >
                                <span aria-hidden>{s.icon}</span>
                                <span style={{ fontWeight: 600 }}>
                                    {s.label}
                                </span>
                            </button>
                        ))}
                    </nav>
                </div>

                {/* Main content */}
                <div
                    style={{
                        flex: 1,
                        background:
                            'linear-gradient(135deg, #5C87C2 0%, #4A73B0 100%)',
                        padding: 20,
                        overflowY: 'auto',
                    }}
                >
                    {section === 'home' ? (
                        <ControlPanelHome onNavigate={navigateTo} />
                    ) : section === 'llm-providers' ? (
                        <LlmProvidersSection />
                    ) : (
                        <div style={{ color: '#fff' }}>
                            This section no longer exists. Go Back or pick a
                            category from the sidebar.
                        </div>
                    )}
                </div>
            </div>
        </div>
    );
}
