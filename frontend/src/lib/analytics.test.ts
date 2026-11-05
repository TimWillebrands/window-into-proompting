import { beforeEach, describe, expect, it, vi } from 'vitest';

const posthog = vi.hoisted(() => ({
    init: vi.fn(),
    register: vi.fn(),
    capture: vi.fn(),
    identify: vi.fn(),
    reset: vi.fn(),
    captureException: vi.fn(),
    get_property: vi.fn(),
    opt_in_capturing: vi.fn(),
    opt_out_capturing: vi.fn(),
}));

vi.mock('posthog-js', () => ({ default: posthog }));

const load = async ({ configured = true, cookie = '' } = {}) => {
    vi.resetModules();
    vi.stubEnv('VITE_POSTHOG_PROJECT_TOKEN', configured ? 'phc_test' : '');
    vi.stubGlobal('window', {
        location: { hostname: 'proompting.party', protocol: 'https:' },
    });
    vi.stubGlobal('document', { cookie });
    return import('./analytics');
};

describe('analytics', () => {
    beforeEach(() => vi.resetAllMocks());

    it('loads nothing before a decision', async () => {
        const { syncAnalyticsUser, track, trackError, consentDecided } =
            await load();

        syncAnalyticsUser({ id: 'user_1' });
        track('window_opened');
        trackError(new Error('boom'));

        expect(consentDecided()).toBe(false);
        for (const fn of Object.values(posthog)) {
            expect(fn).not.toHaveBeenCalled();
        }
    });

    it('stays off after a refusal, even across reloads', async () => {
        const { decideConsent } = await load({ cookie: 'analytics_consent=0' });

        decideConsent(false);

        expect(posthog.init).not.toHaveBeenCalled();
    });

    it('stays off without a token', async () => {
        const { decideConsent, analyticsAvailable } = await load({
            configured: false,
        });

        decideConsent(true);

        expect(analyticsAvailable).toBe(false);
        expect(posthog.init).not.toHaveBeenCalled();
    });

    it('starts on the next load of someone who accepted', async () => {
        await load({ cookie: 'other=1; analytics_consent=1' });

        expect(posthog.init).toHaveBeenCalledOnce();
    });

    it('goes through our own domain', async () => {
        await load({ cookie: 'analytics_consent=1' });

        const options = posthog.init.mock.calls[0][1];
        expect(options.api_host).toBe('/party-line');
        expect(options.tracing_headers).toEqual(['proompting.party']);
    });

    it('accepting writes the cookie the backend reads', async () => {
        const { decideConsent, useConsentPrompt, askConsent } = await load();
        askConsent();

        decideConsent(true);

        expect(document.cookie).toContain('analytics_consent=1; path=/');
        expect(useConsentPrompt.getState().open).toBe(false);
    });

    it('counts every visit, not just the first opt-in', async () => {
        await load({ cookie: 'analytics_consent=1' });

        expect(posthog.capture).toHaveBeenCalledWith('app_opened');
    });

    it('keeps a signed-in identity across a reload, before Clerk loads', async () => {
        posthog.get_property.mockReturnValue('identified');

        await load({ cookie: 'analytics_consent=1' });

        expect(posthog.reset).not.toHaveBeenCalled();
    });

    it('identifies a user who signed in before accepting', async () => {
        const { decideConsent, syncAnalyticsUser } = await load();
        syncAnalyticsUser({ id: 'user_1', email: 'a@b.c', name: 'A' });
        expect(posthog.identify).not.toHaveBeenCalled();

        decideConsent(true);

        expect(posthog.identify).toHaveBeenCalledWith('user_1', {
            email: 'a@b.c',
            name: 'A',
        });
    });

    it('withdrawing stops capture and forgets the person', async () => {
        const { decideConsent, track } = await load({
            cookie: 'analytics_consent=1',
        });
        posthog.capture.mockClear();

        decideConsent(false);
        track('window_opened');

        expect(document.cookie).toContain('analytics_consent=0');
        expect(posthog.reset).toHaveBeenCalledOnce();
        expect(posthog.opt_out_capturing).toHaveBeenCalledOnce();
        expect(posthog.capture).not.toHaveBeenCalled();
    });

    it('resets on sign-out', async () => {
        const { syncAnalyticsUser } = await load({
            cookie: 'analytics_consent=1',
        });
        posthog.get_property.mockReturnValue('identified');

        syncAnalyticsUser(null);

        expect(posthog.reset).toHaveBeenCalledOnce();
    });

    it('keeps an anonymous visitor on the same id', async () => {
        const { syncAnalyticsUser } = await load({
            cookie: 'analytics_consent=1',
        });
        posthog.get_property.mockReturnValue('anonymous');

        syncAnalyticsUser(null);

        expect(posthog.reset).not.toHaveBeenCalled();
    });
});
