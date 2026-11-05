import { useUser } from '@clerk/tanstack-react-start';
import { useEffect } from 'react';
import { syncAnalyticsUser } from '../../lib/analytics';
import { usePlayerSession } from '../../lib/world';

/** The one place Clerk hooks run, so the desktop reads a plain store and stories need no provider. */
export default function ClerkPlayerBridge() {
    const { isLoaded, user } = useUser();
    const sync = usePlayerSession((s) => s.sync);
    const userId = user?.id ?? null;
    const email = user?.primaryEmailAddress?.emailAddress;
    const name = user?.fullName ?? undefined;

    useEffect(() => {
        if (isLoaded) sync(userId);
    }, [isLoaded, userId, sync]);

    useEffect(() => {
        if (isLoaded) {
            syncAnalyticsUser(userId ? { id: userId, email, name } : null);
        }
    }, [isLoaded, userId, email, name]);

    return null;
}
