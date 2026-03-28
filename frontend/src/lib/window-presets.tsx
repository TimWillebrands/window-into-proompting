import OpenPartyApp from '../apps/chat-manager/ChatManagerApp';
import ConfigPanelApp from '../apps/config-panel/ConfigPanelApp';
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
        title: 'Group Chats',
        icon: '💬',
        component: OpenPartyApp,
        initialPosition: { x: 360, y: 260 },
    },
    {
        id: 'config-panel',
        title: 'Control Panel',
        icon: '⚙️',
        component: ConfigPanelApp,
        initialPosition: { x: 200, y: 150 },
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
        label: 'Group Chats',
        icon: '/img/842.ico',
        windowId: 'open-party',
    },
    {
        id: 'config-panel',
        label: 'Control Panel',
        icon: '/img/842.ico',
        windowId: 'config-panel',
    },
];
