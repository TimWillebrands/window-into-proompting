import type { IconName } from '@partytown/ui';
import AutoApp from '../apps/auto/AutoApp';
import BoardApp from '../apps/board/BoardApp';
import CalendarApp from '../apps/calendar/CalendarApp';
import CduApp from '../apps/cdu/CduApp';
import ConfigPanelApp from '../apps/config-panel/ConfigPanelApp';
import DaccordApp from '../apps/daccord/DaccordApp';
import DiaryApp from '../apps/diary/DiaryApp';
import JobsApp from '../apps/jobs/JobsApp';
import MemoryGraphApp from '../apps/memory-graph/MemoryGraphApp';
import MessengerApp from '../apps/messenger/MessengerApp';
import MessengerChatWindow from '../apps/messenger/MessengerChatWindow';
import PersonasApp from '../apps/personas/PersonasApp';
import PlacesApp from '../apps/places/PlacesApp';
import RelationsApp from '../apps/relations/RelationsApp';
import StanceFloorApp from '../apps/stance-floor/StanceFloorApp';
import WardrobeApp from '../apps/wardrobe/WardrobeApp';
import WorldApp from '../apps/world/WorldApp';
import type { WindowDescriptor } from './types';

export const WINDOW_PRESETS: (WindowDescriptor & { icon: IconName })[] = [
    {
        id: 'world',
        title: 'World Monitor',
        icon: 'world',
        component: WorldApp,
        initialPosition: { x: 420, y: 80 },
        initialSize: { width: 820, height: 560 },
    },
    {
        id: 'places',
        title: 'Places',
        icon: 'places',
        component: PlacesApp,
        initialPosition: { x: 200, y: 90 },
        initialSize: { width: 820, height: 560 },
    },
    {
        id: 'messenger',
        title: 'Messenger',
        icon: 'messenger',
        component: MessengerApp,
        initialPosition: { x: 1180, y: 60 },
        initialSize: { width: 300, height: 560 },
    },
    {
        id: 'messenger-chat',
        title: 'Conversation',
        icon: 'chat',
        component: MessengerChatWindow,
        initialPosition: { x: 760, y: 220 },
        initialSize: { width: 440, height: 480 },
    },
    {
        id: 'board',
        title: 'Partyboard',
        icon: 'board',
        component: BoardApp,
        initialPosition: { x: 240, y: 120 },
        initialSize: { width: 860, height: 600 },
    },
    {
        id: 'calendar',
        title: 'Calendar',
        icon: 'calendar',
        component: CalendarApp,
        initialPosition: { x: 360, y: 140 },
        initialSize: { width: 620, height: 520 },
    },
    {
        id: 'diary',
        title: 'Diaries — Notepad',
        icon: 'diary',
        component: DiaryApp,
        initialPosition: { x: 300, y: 160 },
        initialSize: { width: 640, height: 500 },
    },
    {
        id: 'auto',
        title: 'Auto Mode',
        icon: 'auto',
        component: AutoApp,
        initialPosition: { x: 340, y: 150 },
        initialSize: { width: 420, height: 280 },
    },
    {
        id: 'wardrobe',
        title: 'Wardrobe',
        icon: 'wardrobe',
        component: WardrobeApp,
        initialPosition: { x: 320, y: 140 },
        initialSize: { width: 680, height: 520 },
    },
    {
        id: 'jobs',
        title: 'Work & Wants',
        icon: 'jobs',
        component: JobsApp,
        initialPosition: { x: 300, y: 110 },
        initialSize: { width: 780, height: 540 },
    },
    {
        id: 'cdu',
        title: 'C:\\CDU\\',
        icon: 'cdu',
        component: CduApp,
        initialPosition: { x: 260, y: 90 },
        initialSize: { width: 820, height: 560 },
    },
    {
        id: 'personas',
        title: 'Persona Manager',
        icon: 'personas',
        component: PersonasApp,
        initialPosition: { x: 280, y: 200 },
    },
    {
        id: 'open-party',
        title: "D'Accord — Chat Rooms",
        icon: 'chat-rooms',
        component: DaccordApp,
        initialPosition: { x: 360, y: 260 },
    },
    {
        id: 'config-panel',
        title: 'Control Panel',
        icon: 'control-panel',
        component: ConfigPanelApp,
        initialPosition: { x: 120, y: 60 },
        initialSize: { width: 900, height: 640 },
    },
    {
        id: 'memory-graph',
        title: 'Memory Graph',
        icon: 'memory-graph',
        component: MemoryGraphApp,
        initialPosition: { x: 320, y: 180 },
        initialSize: { width: 960, height: 640 },
    },
    {
        id: 'stance-floor',
        title: 'Stance Floor',
        icon: 'stance-floor',
        component: StanceFloorApp,
        initialPosition: { x: 340, y: 200 },
        initialSize: { width: 760, height: 520 },
    },
    {
        id: 'relations',
        title: 'Relations',
        icon: 'relations',
        component: RelationsApp,
        initialPosition: { x: 360, y: 220 },
        initialSize: { width: 720, height: 460 },
    },
];

interface DesktopIconEntry {
    id: string;
    label: string;
    icon: string;
    windowId: string;
    access?: 'member' | 'admin';
}

export const DESKTOP_ICONS: DesktopIconEntry[] = [
    {
        id: 'world',
        label: 'World Monitor',
        icon: '/img/icons/world.ico',
        windowId: 'world',
    },
    {
        id: 'places',
        label: 'Places',
        icon: '/img/icons/places.ico',
        windowId: 'places',
    },
    {
        id: 'messenger',
        label: 'Messenger',
        icon: '/img/icons/messenger.ico',
        windowId: 'messenger',
        access: 'member',
    },
    {
        id: 'board',
        label: 'Partyboard',
        icon: '/img/icons/board.ico',
        windowId: 'board',
    },
    {
        id: 'calendar',
        label: 'Calendar',
        icon: '/img/icons/calendar.ico',
        windowId: 'calendar',
    },
    {
        id: 'diary',
        label: 'Diaries',
        icon: '/img/icons/diary.ico',
        windowId: 'diary',
        access: 'member',
    },
    {
        id: 'wardrobe',
        label: 'Wardrobe',
        icon: '/img/icons/wardrobe.ico',
        windowId: 'wardrobe',
    },
    {
        id: 'jobs',
        label: 'Work & Wants',
        icon: '/img/icons/jobs.ico',
        windowId: 'jobs',
    },
    {
        id: 'cdu',
        label: 'The CDU',
        icon: '/img/icons/jobs.ico',
        windowId: 'cdu',
    },
    {
        id: 'personas',
        label: 'Personas',
        icon: '/img/icons/personas.ico',
        windowId: 'personas',
    },
    {
        id: 'open-party',
        label: 'Chat Rooms',
        icon: '/img/icons/chat-rooms.ico',
        windowId: 'open-party',
    },
    {
        id: 'config-panel',
        label: 'Control Panel',
        icon: '/img/icons/control-panel.ico',
        windowId: 'config-panel',
        access: 'admin',
    },
    {
        id: 'memory-graph',
        label: 'Memory Graph',
        icon: '/img/icons/memory-graph.ico',
        windowId: 'memory-graph',
        access: 'member',
    },
    {
        id: 'stance-floor',
        label: 'Stance Floor',
        icon: '/img/icons/stance-floor.ico',
        windowId: 'stance-floor',
        access: 'admin',
    },
    {
        id: 'relations',
        label: 'Relations',
        icon: '/img/icons/personas.ico',
        windowId: 'relations',
        access: 'admin',
    },
];
