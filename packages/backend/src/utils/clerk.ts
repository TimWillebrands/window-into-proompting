import { type ClerkClient, createClerkClient } from "@clerk/backend";
import type { Context } from "hono";

let _clerkClient: ClerkClient | null = null;
/**
 * Provide a singleton Clerk client initialized from the Cloudflare environment.
 *
 * @param c - Hono Context whose `env` exposes `CLERK_SECRET_KEY`; used to initialize the Clerk client on first call
 * @returns The cached `ClerkClient` instance
 */
export function clerkClient(c: Context<{ Bindings: Cloudflare.Env }>) {
    if (_clerkClient === null) {
        _clerkClient = createClerkClient({ secretKey: c.env.CLERK_SECRET_KEY });
    }
    return _clerkClient;
}