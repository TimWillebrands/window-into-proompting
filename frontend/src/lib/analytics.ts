import posthog from 'posthog-js';
import { create } from 'zustand';
import { PROXY_PATH } from './posthog-proxy';

const token = import.meta.env.VITE_POSTHOG_PROJECT_TOKEN as string | undefined;

/** The backend reads this same cookie: without it, its events are anonymized. */
const CONSENT_COOKIE = 'analytics_consent';
const YEAR_IN_SECONDS = 60 * 60 * 24 * 365;

/** Browser only and opt-in, so SSR, stories and tests never touch PostHog. */
export const analyticsAvailable = typeof window !== 'undefined' && !!token;

export interface AnalyticsUser {
    id: string;
    email?: string;
    name?: string;
}

let loaded = false;
let capturing = false;
let currentUser: AnalyticsUser | null = null;

export const useConsentPrompt = create<{ open: boolean }>(() => ({
    open: false,
}));

export const askConsent = () => useConsentPrompt.setState({ open: true });

const readConsent = () =>
    document.cookie.match(new RegExp(`(?:^|; )${CONSENT_COOKIE}=([01])`))?.[1];

export const consentDecided = () => !!readConsent();

/** Same id the backend captures under; reset only on sign-out, or anonymous visitors churn ids. */
const applyUser = () => {
    if (currentUser) {
        posthog.identify(currentUser.id, {
            email: currentUser.email,
            name: currentUser.name,
        });
    } else if (posthog.get_property('$user_state') === 'identified') {
        posthog.reset();
    }
};

/** Nothing loads before this: no script, no cookies, no requests. */
const start = () => {
    if (!loaded) {
        posthog.init(token as string, {
            api_host: PROXY_PATH,
            ui_host: 'https://eu.posthog.com',
            defaults: '2026-05-30',
            capture_exceptions: true,
            opt_out_persistence_by_default: true,
            // Lets backend events join this browser's session and replay.
            tracing_headers: [window.location.hostname],
        });
        posthog.register({ environment: import.meta.env.MODE });
        loaded = true;
    }
    posthog.opt_in_capturing({ captureEventName: false });
    posthog.capture('app_opened');
    capturing = true;
    // Clerk isn't loaded on a reload yet, and null here would wipe a stored identity.
    if (currentUser) applyUser();
};

const stop = () => {
    posthog.reset();
    posthog.opt_out_capturing();
    capturing = false;
};

export const decideConsent = (granted: boolean) => {
    const secure = window.location.protocol === 'https:' ? '; secure' : '';
    // biome-ignore lint/suspicious/noDocumentCookie: Cookie Store API lacks Safari support
    document.cookie = `${CONSENT_COOKIE}=${granted ? 1 : 0}; path=/; max-age=${YEAR_IN_SECONDS}; samesite=lax${secure}`;
    useConsentPrompt.setState({ open: false });
    if (!analyticsAvailable) return;
    if (granted) start();
    else if (capturing) stop();
};

export const syncAnalyticsUser = (user: AnalyticsUser | null) => {
    currentUser = user;
    if (capturing) applyUser();
};

/** UI-only actions; anything that reaches the API is captured by the backend instead. */
export const track = (event: string, properties?: Record<string, unknown>) => {
    if (capturing) posthog.capture(event, properties);
};

export const trackError = (
    error: Error,
    properties?: Record<string, unknown>,
) => {
    if (capturing) posthog.captureException(error, properties);
};

if (analyticsAvailable && readConsent() === '1') start();
