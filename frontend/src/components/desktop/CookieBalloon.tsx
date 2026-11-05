import { useEffect } from 'react';
import {
    analyticsAvailable,
    askConsent,
    consentDecided,
    decideConsent,
    useConsentPrompt,
} from '../../lib/analytics';

const CHOICE =
    'min-h-0 min-w-0 cursor-pointer border-0 bg-transparent p-0 text-[11px] text-[#0033cc] underline shadow-none';

/** XP tray balloon: both answers weigh the same, and it opens on the client only. */
export default function CookieBalloon() {
    const open = useConsentPrompt((s) => s.open);

    useEffect(() => {
        if (analyticsAvailable && !consentDecided()) askConsent();
    }, []);

    if (!open) return null;

    return (
        <aside
            aria-label="Cookies"
            className="fixed right-2 bottom-[36px] z-[100000] flex items-center gap-3 border border-black bg-[#ffffe1] px-2 py-1 text-[11px] text-black shadow-md"
            style={{ fontFamily: 'Tahoma, sans-serif' }}
        >
            <span>
                Cookies to see how Partytown gets used, incl. recordings?
            </span>
            <button
                type="button"
                className={CHOICE}
                onClick={() => decideConsent(true)}
            >
                Allow
            </button>
            <button
                type="button"
                className={CHOICE}
                onClick={() => decideConsent(false)}
            >
                No thanks
            </button>
        </aside>
    );
}
