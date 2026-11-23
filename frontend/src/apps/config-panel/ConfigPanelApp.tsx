import {
    FieldRow,
    Icon,
    TaskPane,
    TaskPaneLink,
    TaskPaneSection,
    Text,
    TextBox,
    Toolbar,
    ToolbarButton,
} from '@partytown/ui';
import { useDesktopContext } from '#lib/desktop-context';
import { useNavHistoryStore } from '#lib/nav-history';
import ControlPanelHome from './components/ControlPanelHome';
import { SECTION_NAMES, SECTIONS } from './components/constants';
import ModelsSection from './components/ModelsSection';

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
        <div className="flex h-full flex-col overflow-hidden">
            <Toolbar aria-label="Navigation">
                <ToolbarButton
                    icon={<Icon name="back" />}
                    disabled={!canGoBack}
                    onClick={handleBack}
                >
                    Back
                </ToolbarButton>
                <ToolbarButton
                    icon={<Icon name="forward" />}
                    disabled={!canGoForward}
                    onClick={handleForward}
                >
                    Forward
                </ToolbarButton>
            </Toolbar>
            <FieldRow label="Address" labelWidth="auto" className="px-2 py-1">
                <TextBox readOnly value={SECTION_NAMES[section] ?? section} />
            </FieldRow>

            <div className="flex min-h-0 flex-1">
                <TaskPane className="w-[200px] flex-none">
                    <TaskPaneSection
                        title="Control Panel"
                        icon={<Icon name="control-panel" size={32} />}
                        special
                        collapsible={false}
                    >
                        <TaskPaneLink
                            icon={<Icon name="home" />}
                            aria-current={
                                section === 'home' ? 'page' : undefined
                            }
                            onClick={navigateHome}
                        >
                            Home
                        </TaskPaneLink>
                        {SECTIONS.map((s) => (
                            <TaskPaneLink
                                key={s.id}
                                icon={<Icon name={s.icon} />}
                                title={s.description}
                                aria-current={
                                    SECTION_NAMES[section] === s.label
                                        ? 'page'
                                        : undefined
                                }
                                onClick={() => navigateTo(s.id)}
                            >
                                {s.label}
                            </TaskPaneLink>
                        ))}
                    </TaskPaneSection>
                </TaskPane>

                <div className="min-w-0 flex-1 overflow-y-auto">
                    {section === 'home' ? (
                        <ControlPanelHome onNavigate={navigateTo} />
                    ) : section === 'llm-providers' ||
                      section === 'persona-models' ? (
                        <ModelsSection />
                    ) : (
                        <Text as="p" tone="muted" className="p-4">
                            This section no longer exists. Go Back or pick a
                            category from the sidebar.
                        </Text>
                    )}
                </div>
            </div>
        </div>
    );
}
