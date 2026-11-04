# 0029. Clerk auth: public read, signed-in write, owned Players

Date: 2026-11-04

## Status

Accepted

## Context

Every endpoint was anonymous and a Player was whoever typed that name. The app spends real LLM credits, DMs and diaries are private, and an editable provider base URL could send the API key anywhere.

## Decision

Clerk identifies humans. The frontend uses `@clerk/tanstack-react-start`; the backend verifies session tokens with `Clerk.BackendAPI`, from the bearer header or the `__session` cookie. WebSockets rely on the cookie, since browsers can't set headers on them.

Authorization is closed by default: a fallback policy requires sign-in, and public reads opt out with `[AllowAnonymous]`.

| Access | What |
| --- | --- |
| Anyone | World, Board, Places, Calendar, Personas, the live feed (filtered) |
| Signed in | Own Player's actions, Messenger, Diary, Memory, Papertrail, LLM config reads |
| Admin | LLM providers, Persona library, Memory writes, world controls, Party cast, making Places, removing Players |

Admins are Clerk user ids in `Clerk:AdminUserIds`.

**Players.** A signed-in human with no Player makes one by choosing a character name, unique in the cast, recorded against their Clerk id on `WorldClockGrain`. Real names from Clerk are never used. `Actors` resolves who a request acts as; client-sent ids are claims it checks, not facts. Acting without a Player is a 409.

**Removal.** `PlayerRemoval` takes a Player's DMs, messages, Board posts, replies and Respect, activity lines, Body, Home and library entry. Personas keep their memories.

**Speaking.** World actions are your own Player only. In Rooms you may also speak as any Persona no human owns, which keeps Driver overrides working.

**Privacy.** A DM with a human in it reaches only its members, on every path: feed frames, the connect snapshot, `/chat-groups`, `/activities` and `/lives`. Admins get no exception. Persona-only DMs stay public, since they're spectator content with nobody to protect. Room checks use the party the Room reports, because the route's id is client input.

## Consequences

- New endpoints start private, and a test fails if an anonymous action isn't a GET.
- The token check happens at connect: an open socket outlives sign-out until the client closes it.
- Personas can still repeat a DM's content aloud in public; memory isn't partitioned.
- The SDK's own cookie parser mishandles commas and valueless cookies, so `ClerkAuthHandler` reads the token itself.
