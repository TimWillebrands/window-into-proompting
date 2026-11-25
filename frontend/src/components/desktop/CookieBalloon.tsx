import { Balloon, CommandButton, Icon } from '@partytown/ui';
import { useEffect } from 'react';
import {
    analyticsAvailable,
    askConsent,
    consentDecided,
    decideConsent,
    useConsentPrompt,
} from '../../lib/analytics';

export default function CookieBalloon() {
    const open = useConsentPrompt((s) => s.open);

    useEffect(() => {
        if (analyticsAvailable && !consentDecided()) askConsent();
    }, []);

    if (!open) return null;

    return (
        <Balloon
            className="fixed right-2 bottom-[40px] z-[100000]"
            title="Cookies"
            icon={<Icon name="cookie" />}
            placement="above"
            tailAlign="end"
            onClose={() => useConsentPrompt.setState({ open: false })}
        >
            <div className="flex flex-wrap items-center gap-2">
                <span>
                    Cookies to see how Partytown gets used, incl. recordings?
                </span>
                <CommandButton
                    size="small"
                    tone="go"
                    onClick={() => decideConsent(true)}
                >
                    Allow
                </CommandButton>
                <CommandButton
                    size="small"
                    onClick={() => decideConsent(false)}
                >
                    No thanks
                </CommandButton>
            </div>
        </Balloon>
    );
}
