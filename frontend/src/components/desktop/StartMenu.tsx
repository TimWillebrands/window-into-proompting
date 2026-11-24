import {
    Icon,
    StartMenuAction,
    StartMenuItem,
    StartMenu as StartMenuPart,
} from '@partytown/ui';
import { analyticsAvailable, askConsent } from '../../lib/analytics';
import { redirectToSignIn, signOut } from '../../lib/clerk-session';
import { PROGRAMS } from '../../lib/window-presets';
import {
    useCanOpen,
    usePlayerName,
    usePlayerOnAuto,
    usePlayerSession,
    useWorldChoice,
} from '../../lib/world';
import AppIcon from './AppIcon';

interface StartMenuProps {
    onLaunch: (windowId: string) => void;
    onClose: () => void;
}

export default function StartMenu({ onLaunch, onClose }: StartMenuProps) {
    const playerName = usePlayerName();
    const onAuto = usePlayerOnAuto();
    const signedIn = usePlayerSession((s) => !!s.userId);
    const canOpen = useCanOpen();
    const leaveWorld = useWorldChoice((s) => s.leave);

    const closeThen = (action?: () => void) => {
        action?.();
        onClose();
    };

    const items = PROGRAMS.filter(
        (program) =>
            (program.shortcut || program.startMenu) && canOpen(program.access),
    );

    return (
        <StartMenuPart
            user={playerName ?? 'Visitor'}
            picture={<Icon name="user" size={32} />}
            onClose={() => closeThen()}
            programs={items.map((item) => (
                <StartMenuItem
                    key={item.id}
                    icon={<AppIcon icon={item.icon} size={32} />}
                    label={item.title}
                    description="Open"
                    onClick={() => closeThen(() => onLaunch(item.id))}
                />
            ))}
            places={
                <>
                    {playerName && (
                        <StartMenuItem
                            icon={<Icon name="auto" size={32} />}
                            label={`Auto Mode${onAuto ? ' (on)' : ''}`}
                            onClick={() => closeThen(() => onLaunch('auto'))}
                        />
                    )}
                    {analyticsAvailable && (
                        <StartMenuItem
                            icon={<Icon name="cookie" size={32} />}
                            label="Cookies"
                            onClick={() => closeThen(askConsent)}
                        />
                    )}
                    <StartMenuItem
                        icon={<Icon name="help" size={32} />}
                        label="Help and Support"
                        onClick={() => closeThen()}
                    />
                    <StartMenuItem
                        icon={<Icon name="search" size={32} />}
                        label="Search"
                        onClick={() => closeThen()}
                    />
                    <StartMenuItem
                        icon={<Icon name="run" size={32} />}
                        label="Run..."
                        onClick={() => closeThen()}
                    />
                </>
            }
            footer={
                <>
                    <StartMenuAction
                        tone={signedIn ? 'caution' : 'go'}
                        icon={signedIn ? undefined : <Icon name="key" />}
                        onClick={() => {
                            onClose();
                            if (signedIn) signOut();
                            else redirectToSignIn();
                        }}
                    >
                        {signedIn ? 'Log Off' : 'Sign In'}
                    </StartMenuAction>
                    <StartMenuAction
                        tone="danger"
                        onClick={() => {
                            onClose();
                            leaveWorld();
                        }}
                    >
                        Switch World
                    </StartMenuAction>
                </>
            }
        />
    );
}
