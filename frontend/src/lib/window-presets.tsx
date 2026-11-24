import type { IconName, Program, SkinName, WindowBounds } from '@partytown/ui';
import type { ComponentType } from 'react';
import AutoApp from '../apps/auto/AutoApp';
import BoardApp from '../apps/board/BoardApp';
import CalendarApp from '../apps/calendar/CalendarApp';
import CduApp from '../apps/cdu/CduApp';
import ConfigPanelApp from '../apps/config-panel/ConfigPanelApp';
import DaccordApp from '../apps/daccord/DaccordApp';
import DiaryApp from '../apps/diary/DiaryApp';
import DisplayApp from '../apps/display/DisplayApp';
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
import ProgramHost from '../components/desktop/ProgramHost';

export interface AppProgram {
    id: string;
    title: string;
    icon: IconName;
    // biome-ignore lint/suspicious/noExplicitAny: each program takes its own props from the window's opaque record
    component: ComponentType<any>;
    defaultBounds?: Partial<WindowBounds>;
    singleton?: boolean;
    skin?: SkinName;
    shortcut?: string;
    startMenu?: boolean;
    access?: 'member' | 'admin';
}

export const PROGRAMS: AppProgram[] = [
    {
        id: 'world',
        title: 'World Monitor',
        icon: 'world',
        component: WorldApp,
        defaultBounds: { x: 420, y: 80, width: 820, height: 560 },
        shortcut: 'World Monitor',
    },
    {
        id: 'places',
        title: 'Places',
        icon: 'places',
        component: PlacesApp,
        defaultBounds: { x: 200, y: 90, width: 820, height: 560 },
        shortcut: 'Places',
    },
    {
        id: 'messenger',
        title: 'Messenger',
        icon: 'messenger',
        component: MessengerApp,
        skin: 'msn',
        defaultBounds: { x: 1180, y: 60, width: 300, height: 560 },
        shortcut: 'Messenger',
        access: 'member',
    },
    {
        id: 'messenger-chat',
        title: 'Conversation',
        icon: 'chat',
        component: MessengerChatWindow,
        skin: 'msn',
        defaultBounds: { x: 760, y: 220, width: 440, height: 480 },
    },
    {
        id: 'board',
        title: 'Partyboard',
        icon: 'board',
        component: BoardApp,
        defaultBounds: { x: 240, y: 120, width: 860, height: 600 },
        shortcut: 'Partyboard',
    },
    {
        id: 'calendar',
        title: 'Calendar',
        icon: 'calendar',
        component: CalendarApp,
        defaultBounds: { x: 360, y: 140, width: 620, height: 520 },
        shortcut: 'Calendar',
    },
    {
        id: 'diary',
        title: 'Diaries — Notepad',
        icon: 'diary',
        component: DiaryApp,
        defaultBounds: { x: 300, y: 160, width: 640, height: 500 },
        shortcut: 'Diaries',
        access: 'member',
    },
    {
        id: 'auto',
        title: 'Auto Mode',
        icon: 'auto',
        component: AutoApp,
        defaultBounds: { x: 340, y: 150, width: 420, height: 280 },
    },
    {
        id: 'wardrobe',
        title: 'Wardrobe',
        icon: 'wardrobe',
        component: WardrobeApp,
        defaultBounds: { x: 320, y: 140, width: 680, height: 520 },
        shortcut: 'Wardrobe',
    },
    {
        id: 'jobs',
        title: 'Work & Wants',
        icon: 'jobs',
        component: JobsApp,
        defaultBounds: { x: 300, y: 110, width: 780, height: 540 },
        shortcut: 'Work & Wants',
    },
    {
        id: 'cdu',
        title: 'C:\\CDU\\',
        icon: 'cdu',
        component: CduApp,
        defaultBounds: { x: 260, y: 90, width: 820, height: 560 },
        shortcut: 'The CDU',
    },
    {
        id: 'personas',
        title: 'Persona Manager',
        icon: 'personas',
        component: PersonasApp,
        defaultBounds: { x: 280, y: 200, width: 720, height: 480 },
        shortcut: 'Personas',
    },
    {
        id: 'open-party',
        title: "D'Accord — Chat Rooms",
        icon: 'chat-rooms',
        component: DaccordApp,
        defaultBounds: { x: 360, y: 260, width: 720, height: 480 },
        shortcut: 'Chat Rooms',
    },
    {
        id: 'config-panel',
        title: 'Control Panel',
        icon: 'control-panel',
        component: ConfigPanelApp,
        defaultBounds: { x: 120, y: 60, width: 900, height: 640 },
        shortcut: 'Control Panel',
        access: 'admin',
    },
    {
        id: 'memory-graph',
        title: 'Memory Graph',
        icon: 'memory-graph',
        component: MemoryGraphApp,
        defaultBounds: { x: 320, y: 180, width: 960, height: 640 },
        shortcut: 'Memory Graph',
        access: 'member',
    },
    {
        id: 'stance-floor',
        title: 'Stance Floor',
        icon: 'stance-floor',
        component: StanceFloorApp,
        defaultBounds: { x: 340, y: 200, width: 760, height: 520 },
        shortcut: 'Stance Floor',
        access: 'admin',
    },
    {
        id: 'relations',
        title: 'Relations',
        icon: 'relations',
        component: RelationsApp,
        defaultBounds: { x: 360, y: 220, width: 720, height: 460 },
        shortcut: 'Relations',
        access: 'admin',
    },
    {
        id: 'display',
        title: 'Display Properties',
        icon: 'palette',
        component: DisplayApp,
        defaultBounds: { x: 380, y: 120, width: 420, height: 500 },
        singleton: true,
        startMenu: true,
    },
];

export const findProgram = (id: string) =>
    PROGRAMS.find((program) => program.id === id);

export const desktopPrograms: Program[] = PROGRAMS.map(
    ({ component, ...program }) => ({
        ...program,
        render: (props, window) => (
            <ProgramHost
                id={window.id}
                title={window.title}
                component={component}
                props={props}
            />
        ),
    }),
);
