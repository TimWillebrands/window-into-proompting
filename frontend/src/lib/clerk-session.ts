interface ClerkGlobal {
    session?: { getToken: () => Promise<string | null> } | null;
    redirectToSignIn?: () => Promise<unknown>;
    signOut?: () => Promise<unknown>;
}

const clerk = () =>
    typeof window === 'undefined'
        ? undefined
        : (window as unknown as { Clerk?: ClerkGlobal }).Clerk;

/** A fresh session token, which also renews the cookie the WebSocket handshake carries. */
export const sessionToken = async (): Promise<string | null> => {
    try {
        return (await clerk()?.session?.getToken()) ?? null;
    } catch {
        return null;
    }
};

export const redirectToSignIn = () => void clerk()?.redirectToSignIn?.();

export const signOut = () => void clerk()?.signOut?.();
