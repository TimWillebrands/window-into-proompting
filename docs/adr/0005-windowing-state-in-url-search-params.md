# Windowing state lives in URL search params, not a server-side session

The XP desktop has exactly **one route** (`/`) and all window state — which apps are open, their positions, sizes, z-order, per-app props (which Room is open in a GroupChatWindow, which Persona is selected in PersonasApp, etc.) — is encoded into TanStack Router **search params**, validated by `desktopLayoutSchema`. There is no page-level routing; navigation between "screens" is opening/closing/focusing windows.

## Why search params and not local state

- **Shareable links.** A URL captures the entire desktop layout — including "this Room is open here, that Persona panel there." Send the URL, the recipient sees the same desktop.
- **Refresh-safe.** Reloading the page restores the open windows exactly. No localStorage juggling.
- **Browser back/forward** maps onto window-state history for free (paired with `useNavHistoryStore`).
- **No server-side session** for layout state — the URL *is* the state. Keeps the backend pure: it knows about Parties / Rooms / Personas, not "which window is on top in someone's browser."

## Why one route and not per-app routes

- The XP metaphor is a desktop, not a webpage. Routes like `/personas` and `/rooms` would imply navigation away — wrong mental model.
- Apps coexist; the user has multiple windows open at once. A route per app makes "Personas and a Room open side by side" awkward.
- Window props (`{ partyId, chatGroupId }` for a Room window, etc.) belong with the window instance, not in a route shape.

## Consequences

- URLs are long. Worth it for shareability; ugly to look at.
- Any new app on the desktop must register in `WINDOW_PRESETS` and round-trip through `desktopLayoutSchema`. Adding an app = adding a schema branch.
- "Where am I" in the app is answered by `desktop-context`, not by the router — search-params parsing happens once, into Zustand, then the UI reads Zustand.
- SSR / static deep-linking is harder: the route is always the same; only the search params differ. Cannot pre-render distinct pages.
