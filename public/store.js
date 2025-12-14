
document.addEventListener('alpine:init', () => {
    Alpine.store('desktop', {
        windows: Alpine.$persist({}).as('desktop_windows'),
        hasSeenWelcome: Alpine.$persist(false).as('desktop_hasSeenWelcome'),
        focusedApp: Alpine.$persist(null).as('desktop_focusedApp'),
        user: null,
        zCounter: 1000,

        init() {
            window.addEventListener('clerkified', () => {
                this.user = Clerk.user;
                if (this.user) {
                    analytics.identifyUser(this.user.id, {
                        email: this.user.emailAddresses[0].emailAddress,
                        emailAddresses: this.user.emailAddresses,
                        name: this.user.fullName
                    });
                } else {
                    console.warn("Clerk user not found", Clerk, this.user);
                }
            });

            // Only show welcome on first visit
            if (!this.hasSeenWelcome) {
                this.openWindow({
                    id: 'welcome',
                    title: 'Welcome to Proompt.party',
                    url: '/welcome'
                });
                this.hasSeenWelcome = true;
            }
        },

        openWindow(w) {
            if (this.windows[w.id]) {
                this.focusedApp = w.id;
                return;
            }

            const newWindow = {
                id: w.id,
                title: w.title,
                url: w.url,
                x: '100px',
                y: '100px',
                z: this.zCounter++,
                ...w
            };

            this.windows[w.id] = newWindow;
            this.focusedApp = w.id;

            analytics.trackWindowOpened({
                window_id: w.id,
                window_title: w.title,
                window_type: w.id,
                source: 'icon_click' // Assume icon click for now
            });
        },

        closeWindow(id) {
            delete this.windows[id];
            if (this.focusedApp === id) {
                // Focus the next available window
                const windowIds = Object.keys(this.windows);
                this.focusedApp = windowIds.length > 0 ? windowIds[windowIds.length - 1] : null;
            }
            analytics.trackWindowClosed(id);
        },

        focusWindow(id) {
            this.focusedApp = id;
            this.windows[id].z = this.zCounter++;
        }
    });
});
