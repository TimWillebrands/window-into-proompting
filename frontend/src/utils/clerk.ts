import { type ClerkClient, createClerkClient } from '@clerk/backend';
import type { Context } from 'hono';

let _clerkClient: ClerkClient | null = null;
export function clerkClient(c: Context<{ Bindings: Cloudflare.Env }>) {
    if (_clerkClient === null) {
        _clerkClient = createClerkClient({ secretKey: c.env.CLERK_SECRET_KEY });
    }
    return _clerkClient;
}
