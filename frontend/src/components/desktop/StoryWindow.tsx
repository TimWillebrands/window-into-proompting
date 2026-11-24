import { type SkinName, Window } from '@partytown/ui';
import type { ReactNode } from 'react';
import { fn } from 'storybook/test';
import AppIcon from './AppIcon';
import { ProgramBody } from './ProgramHost';

const captions = { minimise: fn(), maximise: fn(), close: fn() };

export default function StoryWindow({
    id,
    title,
    icon,
    skin,
    children,
}: {
    id: string;
    title: string;
    icon?: string;
    skin?: SkinName;
    children: ReactNode;
}) {
    return (
        <Window
            windowId={id}
            title={title}
            icon={<AppIcon icon={icon} size={skin ? 32 : 16} />}
            skin={skin}
            onMinimise={captions.minimise}
            onMaximise={captions.maximise}
            onClose={captions.close}
            className="h-full w-full"
        >
            <ProgramBody title={title} onClose={captions.close}>
                {children}
            </ProgramBody>
        </Window>
    );
}
