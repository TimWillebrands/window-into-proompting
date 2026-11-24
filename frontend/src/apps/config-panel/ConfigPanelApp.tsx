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
    ToolbarSeparator,
    WindowLayout,
} from '@partytown/ui';
import { useState } from 'react';
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
    const models = section === 'llm-providers' || section === 'persona-models';
    const [adding, setAdding] = useState(false);

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
        <WindowLayout
            toolbar={
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
                    {models && (
                        <>
                            <ToolbarSeparator />
                            <ToolbarButton
                                icon={<Icon name="add" />}
                                onClick={() => setAdding(true)}
                            >
                                Add Provider
                            </ToolbarButton>
                        </>
                    )}
                </Toolbar>
            }
            addressBar={
                <FieldRow label="Address" labelWidth="auto">
                    <TextBox
                        readOnly
                        value={SECTION_NAMES[section] ?? section}
                    />
                </FieldRow>
            }
            taskPaneLabel="Control Panel"
            taskPane={
                <TaskPane>
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
            }
            contentLabel={SECTION_NAMES[section] ?? 'Control Panel'}
        >
            {section === 'home' ? (
                <ControlPanelHome onNavigate={navigateTo} />
            ) : models ? (
                <ModelsSection
                    adding={adding}
                    onAddClosed={() => setAdding(false)}
                />
            ) : (
                <Text as="p" tone="muted" className="p-4">
                    This section no longer exists. Go Back or pick a category
                    from the sidebar.
                </Text>
            )}
        </WindowLayout>
    );
}
