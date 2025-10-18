window.cookieStorage = {
    getItem(key) {
        const cookies = document.cookie.split(";");
        for (let i = 0; i < cookies.length; i++) {
            const cookie = cookies[i].split("=");
            if (key === cookie[0].trim()) {
                return decodeURIComponent(cookie[1]);
            }
        }
        return null;
    },
    setItem(key, value) {
        // biome-ignore lint/suspicious/noDocumentCookie: I do not care
        document.cookie = `${key} = ${encodeURIComponent(value)}`;
    },
};

// PostHog Analytics Module
// Based on PostHog event tracking guide: https://posthog.com/tutorials/event-tracking-guide

window.analytics = (function () {
    'use strict';

    let sessionId = `session_${Date.now()}_${Math.random().toString(36).substr(2, 9)}`;
    let startTime = Date.now();

    function getBaseProperties() {
        return {
            timestamp: Date.now(),
            session_id: sessionId,
            user_agent: navigator.userAgent,
            url: window.location.href,
            referrer: document.referrer || undefined,
        };
    }

    function capture(event, properties = {}) {
        if (typeof window !== 'undefined' && window.posthog) {
            const eventProperties = {
                ...getBaseProperties(),
                ...properties,
            };

            window.posthog.capture(event, eventProperties);
        } else {
            console.warn('[Analytics] PostHog not available - make sure the PostHog snippet is loaded');
        }
    }

    return {
        // App-level events
        trackAppOpened(properties) {
            capture('app_opened', properties);
        },

        trackWindowOpened(properties) {
            capture('window_opened', properties);
        },

        trackWindowClosed(windowId) {
            capture('window_closed', { window_id: windowId });
        },

        trackIconClicked(iconName, iconLabel) {
            capture('icon_clicked', {
                icon_name: iconName,
                icon_label: iconLabel
            });
        },

        // Party events
        trackPartyCreated(properties) {
            capture('party_created', properties);
        },

        trackPartyJoined(properties) {
            capture('party_joined', properties);
        },

        trackPartyLeft(partyId) {
            capture('party_left', { party_id: partyId });
        },

        trackMessageSent(properties) {
            capture('message_sent', properties);
        },

        trackMessageReceived(partyId, messageLength) {
            capture('message_received', {
                party_id: partyId,
                message_length: messageLength
            });
        },

        // Persona events
        trackPersonaCreated(properties) {
            capture('persona_created', properties);
        },

        trackPersonaEdited(properties) {
            capture('persona_edited', properties);
        },

        trackPersonaSelected(personaId, personaName) {
            capture('persona_selected', {
                persona_id: personaId,
                persona_name: personaName
            });
        },

        trackPersonaDeleted(personaId, personaName) {
            capture('persona_deleted', {
                persona_id: personaId,
                persona_name: personaName
            });
        },

        // Welcome tour events
        trackWelcomeStepCompleted(properties) {
            capture('welcome_step_completed', properties);
        },

        trackWelcomeTourStarted() {
            capture('welcome_tour_started');
        },

        trackWelcomeTourCompleted() {
            capture('welcome_tour_completed');
        },

        // Model selection
        trackModelSelected(model, source) {
            capture('model_selected', {
                model_name: model,
                selection_source: source
            });
        },

        // Error tracking
        trackError(properties) {
            capture('error_occurred', properties);
        },

        // Session events
        trackSessionStarted() {
            capture('session_started');
        },

        trackSessionEnded() {
            const sessionDuration = Date.now() - startTime;
            capture('session_ended', {
                session_duration_ms: sessionDuration
            });
        },

        // User identification
        identifyUser(userId, properties) {
            if (typeof window !== 'undefined' && window.posthog) {
                window.posthog.identify(userId, properties);
                console.log(`[Analytics] User identified: ${userId}`, properties);
            }
        },

        // Group tracking (for future multi-user features)
        trackGroup(groupType, groupKey, properties) {
            if (typeof window !== 'undefined' && window.posthog) {
                window.posthog.group(groupType, groupKey, properties);
                console.log(`[Analytics] Group tracked: ${groupType}:${groupKey}`, properties);
            }
        },

        // Utility methods
        getSessionId() {
            return sessionId;
        },

        getSessionDuration() {
            return Date.now() - startTime;
        }
    };
})();

window.addEventListener('load', async function () {
    await Clerk.load()

    console.log('ClerkJS is loaded')

    const event = new Event("clerkified");
    window.dispatchEvent(event);

    // const el = document.getElementById('taskbar-user')

    // if (Clerk.isSignedIn) {
    //     el.innerHTML = `
    //     <div id="user-button"></div>
    //   `

    //     const userButtonDiv = document.getElementById('user-button')

    //     Clerk.mountUserButton(userButtonDiv)
    // } else {
    // //     el.innerHTML = `
    // //     <div id="sign-in"></div>
    // //   `

    // //     const signInDiv = document.getElementById('sign-in')

    // //     Clerk.openSignIn() //.mountSignIn(signInDiv)
    // }
})