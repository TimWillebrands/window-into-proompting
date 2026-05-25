import ConfigPanelApp from '../apps/config-panel/ConfigPanelApp';
import DaccordApp from '../apps/daccord/DaccordApp';
import ImportApp from '../apps/import/ImportApp';
import MemoryGraphApp from '../apps/memory-graph/MemoryGraphApp';
import PersonasApp from '../apps/personas/PersonasApp';
import type { WindowDescriptor } from './types';

export const WINDOW_PRESETS: WindowDescriptor[] = [
    {
        id: 'personas',
        title: 'Persona Manager',
        icon: '👤',
        component: PersonasApp,
        initialPosition: { x: 280, y: 200 },
    },
    {
        id: 'open-party',
        title: "D'Accord — Chat Rooms",
        icon: '💬',
        component: DaccordApp,
        initialPosition: { x: 360, y: 260 },
    },
    {
        id: 'import',
        title: 'Import Chat (Gemini JSON)',
        icon: '📥',
        component: ImportApp,
        initialPosition: { x: 320, y: 220 },
    },
    {
        id: 'config-panel',
        title: 'Control Panel',
        icon: '⚙️',
        component: ConfigPanelApp,
        initialPosition: { x: 200, y: 150 },
    },
    {
        id: 'memory-graph',
        title: 'Memory Graph',
        icon: '🧠',
        component: MemoryGraphApp,
        initialPosition: { x: 320, y: 180 },
        // Force-graph needs canvas room — bigger default than the standard 640x480.
        initialSize: { width: 960, height: 640 },
    },
];

export const DESKTOP_ICONS = [
    {
        id: 'personas',
        label: 'Personas',
        icon: '/img/1012.ico',
        windowId: 'personas',
    },
    {
        id: 'open-party',
        label: 'Chat Rooms',
        icon: '/img/842.ico',
        windowId: 'open-party',
    },
    {
        id: 'import',
        label: 'Import Chat',
        icon: '/img/842.ico',
        windowId: 'import',
    },
    {
        id: 'config-panel',
        label: 'Control Panel',
        icon: '/img/842.ico',
        windowId: 'config-panel',
    },
    {
        id: 'memory-graph',
        label: 'Memory Graph',
        icon: '/img/842.ico',
        windowId: 'memory-graph',
    },
];
