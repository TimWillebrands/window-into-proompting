import {
    ComboBox,
    CommandButton,
    FieldRow,
    type Scheme,
    SunkenField,
    schemes,
    Tabs,
    Text,
    Window,
} from '@partytown/ui';
import { useState } from 'react';
import { useDesktopContext } from '#lib/desktop-context';
import { useWindowInstance } from '../../components/desktop/window-instance-context';
import { useThemeStore } from '../../lib/theme-store';

const schemeLabels: Record<Scheme, string> = {
    'luna-blue': 'Default (blue)',
    'olive-green': 'Olive Green',
    silver: 'Silver',
    noir: 'Noir',
};

const schemeOptions = schemes.map((scheme) => ({
    value: scheme,
    label: schemeLabels[scheme],
}));

const ignore = () => {};

function SchemePreview({ scheme }: { scheme: Scheme }) {
    return (
        <SunkenField
            data-ui-scheme={scheme}
            role="img"
            aria-label={`Preview of the ${schemeLabels[scheme]} colour scheme`}
            className="relative h-[176px] overflow-hidden"
        >
            <div inert className="absolute inset-0">
                <Window
                    title="Inactive Window"
                    active={false}
                    onMinimise={ignore}
                    onMaximise={ignore}
                    onClose={ignore}
                    className="absolute top-2 left-2 w-[230px]"
                >
                    <div className="h-[48px]" />
                </Window>
                <Window
                    title="Active Window"
                    active
                    onMinimise={ignore}
                    onMaximise={ignore}
                    onClose={ignore}
                    className="absolute top-8 left-5 w-[250px]"
                >
                    <div className="flex h-[72px] flex-col gap-1 p-2">
                        <Text>Window Text</Text>
                        <Text tone="muted">Muted text</Text>
                    </div>
                </Window>
                <Window
                    title="Message Box"
                    active
                    onClose={ignore}
                    className="absolute top-[92px] right-2 w-[160px]"
                >
                    <div className="flex flex-col items-center gap-2 p-2">
                        <Text>Message Text</Text>
                        <CommandButton isDefault size="small">
                            OK
                        </CommandButton>
                    </div>
                </Window>
            </div>
        </SunkenField>
    );
}

export function DisplayProperties({ onClose }: { onClose: () => void }) {
    const scheme = useThemeStore((s) => s.scheme);
    const choose = useThemeStore((s) => s.choose);
    const [pending, setPending] = useState<Scheme>(scheme);
    const dirty = pending !== scheme;

    const appearance = (
        <div className="flex flex-col gap-3 p-3">
            <SchemePreview scheme={pending} />
            <FieldRow label="Windows and buttons:" layout="stacked">
                <ComboBox
                    value="xp"
                    onChange={ignore}
                    options={[{ value: 'xp', label: 'Windows XP style' }]}
                />
            </FieldRow>
            <FieldRow label="Color scheme:" layout="stacked">
                <ComboBox
                    value={pending}
                    onChange={(e) => setPending(e.target.value as Scheme)}
                    options={schemeOptions}
                />
            </FieldRow>
        </div>
    );

    return (
        <form
            className="flex h-full flex-col gap-2 p-2"
            onSubmit={(e) => {
                e.preventDefault();
                choose(pending);
                onClose();
            }}
        >
            <Tabs
                label="Display Properties"
                className="min-h-0 flex-1"
                items={[
                    {
                        value: 'appearance',
                        label: 'Appearance',
                        content: appearance,
                    },
                ]}
            />
            <div className="flex justify-end gap-1.5">
                <CommandButton type="submit" isDefault>
                    OK
                </CommandButton>
                <CommandButton type="button" onClick={onClose}>
                    Cancel
                </CommandButton>
                <CommandButton
                    type="button"
                    disabled={!dirty}
                    onClick={() => choose(pending)}
                >
                    Apply
                </CommandButton>
            </div>
        </form>
    );
}

export default function DisplayApp() {
    const { windowId } = useWindowInstance();
    const { closeWindow } = useDesktopContext();
    return <DisplayProperties onClose={() => closeWindow(windowId)} />;
}
