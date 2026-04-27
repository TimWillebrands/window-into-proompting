import { useDesktopContext } from '#lib/desktop-context';
import { useNavHistoryStore } from '#lib/nav-history';
import ControlPanelHome from './components/ControlPanelHome';
import { SECTION_NAMES } from './components/constants';
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
            style={{
                display: 'flex',
                flexDirection: 'column',
                height: '100%',
                background: '#ECE9D8',
                overflow: 'hidden',
            }}
        >
            {/* Navigation toolbar */}
            <div
                style={{
                    display: 'flex',
                    alignItems: 'center',
                    gap: 4,
                    padding: '3px 6px',
                    borderBottom: '1px solid #ACA899',
                    background: '#ECE9D8',
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
                        padding: '2px 6px',
                        background: '#fff',
                        border: '1px solid #7F9DB9',
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
                    style={{
                        width: 170,
                        flexShrink: 0,
                        borderRight: '1px solid #ACA899',
                        display: 'flex',
                        flexDirection: 'column',
                        background: '#D6E4F7',
                        overflow: 'hidden',
                    }}
                >
                    <div
                        style={{
                            background:
                                'linear-gradient(to bottom, #1B5EAD, #3A85C7)',
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
                            padding: '8px 6px',
                            borderBottom: '1px solid #ACA899',
                        }}
                    >
                        <button
                            type="button"
                            onClick={navigateHome}
                            style={{
                                width: '100%',
                                textAlign: 'left',
                                background: 'none',
                                border: 'none',
                                cursor: 'pointer',
                                padding: '2px 4px',
                                fontSize: 12,
                                color: '#003399',
                                textDecoration: 'underline',
                            }}
                        >
                            🏠 Home
                        </button>
                    </div>

                    <div
                        style={{
                            padding: '6px 8px',
                            fontSize: 11,
                            fontWeight: 700,
                            color: '#1B5EAD',
                            borderBottom: '1px solid #ACA899',
                        }}
                    >
                        See Also
                    </div>
                    <div
                        style={{
                            padding: '6px 8px',
                            fontSize: 12,
                            color: '#555',
                        }}
                    >
                        More settings coming soon.
                    </div>
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
                        <div style={{ color: '#fff' }}>Unknown section.</div>
                    )}
                </div>
            </div>
        </div>
    );
}
