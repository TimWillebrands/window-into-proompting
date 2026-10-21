import BoardApp from '../apps/board/BoardApp';
import CalendarApp from '../apps/calendar/CalendarApp';
import ConfigPanelApp from '../apps/config-panel/ConfigPanelApp';
import DaccordApp from '../apps/daccord/DaccordApp';
import DiaryApp from '../apps/diary/DiaryApp';
import MemoryGraphApp from '../apps/memory-graph/MemoryGraphApp';
import MessengerApp from '../apps/messenger/MessengerApp';
import MessengerChatWindow from '../apps/messenger/MessengerChatWindow';
import PersonasApp from '../apps/personas/PersonasApp';
import PlacesApp from '../apps/places/PlacesApp';
import StanceFloorApp from '../apps/stance-floor/StanceFloorApp';
import WardrobeApp from '../apps/wardrobe/WardrobeApp';
import WorldApp from '../apps/world/WorldApp';
import type { WindowDescriptor } from './types';

export const WINDOW_PRESETS: WindowDescriptor[] = [
    {
        id: 'world',
        title: 'World Monitor',
        icon: '🌍',
        component: WorldApp,
        initialPosition: { x: 420, y: 80 },
        initialSize: { width: 820, height: 560 },
    },
    {
        id: 'places',
        title: 'Places',
        icon: '🏘️',
        component: PlacesApp,
        initialPosition: { x: 200, y: 90 },
        initialSize: { width: 820, height: 560 },
    },
    {
        id: 'messenger',
        title: 'Messenger',
        icon: '🦋',
        component: MessengerApp,
        initialPosition: { x: 1180, y: 60 },
        initialSize: { width: 300, height: 560 },
    },
    {
        id: 'messenger-chat',
        title: 'Conversation',
        icon: '💬',
        component: MessengerChatWindow,
        initialPosition: { x: 760, y: 220 },
        initialSize: { width: 440, height: 480 },
    },
    {
        id: 'board',
        title: 'Partyboard',
        icon: '📌',
        component: BoardApp,
        initialPosition: { x: 240, y: 120 },
        initialSize: { width: 860, height: 600 },
    },
    {
        id: 'calendar',
        title: 'Calendar',
        icon: '📅',
        component: CalendarApp,
        initialPosition: { x: 360, y: 140 },
        initialSize: { width: 620, height: 520 },
    },
    {
        id: 'diary',
        title: 'Diaries — Notepad',
        icon: '📓',
        component: DiaryApp,
        initialPosition: { x: 300, y: 160 },
        initialSize: { width: 640, height: 500 },
    },
    {
        id: 'wardrobe',
        title: 'Wardrobe',
        icon: '👗',
        component: WardrobeApp,
        initialPosition: { x: 320, y: 140 },
        initialSize: { width: 680, height: 520 },
    },
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
    {
        id: 'stance-floor',
        title: 'Stance Floor',
        icon: '🧭',
        component: StanceFloorApp,
        initialPosition: { x: 340, y: 200 },
        initialSize: { width: 760, height: 520 },
    },
];

export const DESKTOP_ICONS = [
    {
        id: 'world',
        label: 'World Monitor',
        icon: '/img/842.ico',
        windowId: 'world',
    },
    {
        id: 'places',
        label: 'Places',
        icon: '/img/842.ico',
        windowId: 'places',
    },
    {
        id: 'messenger',
        label: 'Messenger',
        icon: '/img/1012.ico',
        windowId: 'messenger',
    },
    {
        id: 'board',
        label: 'Partyboard',
        icon: '/img/842.ico',
        windowId: 'board',
    },
    {
        id: 'calendar',
        label: 'Calendar',
        icon: '/img/842.ico',
        windowId: 'calendar',
    },
    {
        id: 'diary',
        label: 'Diaries',
        icon: '/img/842.ico',
        windowId: 'diary',
    },
    {
        id: 'wardrobe',
        label: 'Wardrobe',
        icon: '/img/842.ico',
        windowId: 'wardrobe',
    },
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
    {
        id: 'stance-floor',
        label: 'Stance Floor',
        icon: '/img/842.ico',
        windowId: 'stance-floor',
    },
];
