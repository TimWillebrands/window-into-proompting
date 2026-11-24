---
name: partytown-ui
description: The Partytown look and its UI kit (@partytown/ui in frontend/ui). Use for ANY frontend visual work in this repo, such as building or changing a component, window, dialog, app screen, story, CSS, colours, icons, fonts, motion, dark mode or layout, and before writing className, style or CSS in frontend/.
---

# Partytown UI: Luna with a glass finish

**80% Windows XP Luna, 20% macOS Liquid Glass.** Luna is what it looks like. Glass is how it feels.

- A screenshot must read as Windows XP at a glance: blue title bars, green Start, red Close, the beige XP face,
  Tahoma, plastic buttons, task panes.
- Glass adds one material, used only on floating chrome, plus the comfort of a modern toolkit: motion, concentric
  shapes, visible focus, roomy targets, readable type.
- When they conflict, **Luna wins on looks and comfort wins on use.** If it's tiring to read or hard to hit, it's
  wrong, even if it's period-correct.

The why is in `docs/adr/0067-luna-with-a-glass-finish.md`. Values live only in `frontend/ui/src/tokens.css`. This
skill names tokens and never repeats their values.

## The boundary

- `frontend/ui/` (`@partytown/ui`) owns **every visual**: tokens, parts, icons, fonts, skins. It never imports app
  code (`#api`, `#lib`, `src/`). React is its only peer.
- Apps (`frontend/src/`) **compose parts and do layout**. They import from `@partytown/ui` only, never a deep path.
- Apps may use Tailwind **for layout only**: flex, grid, gap, padding, margin, size, overflow, position. The visual
  namespaces (colour, shadow, radius, font, text size) don't exist in the app's theme.
- No visual inline `style={{}}` in apps. Allowed: geometry that is data (a drag position, a width from data) and a
  colour that *is* data (a Persona's home colour), passed through a part's prop or a `--ui-*` custom property.
- No hex, rgb or hsl literals, font stacks, durations or `dark:` utilities outside `frontend/ui/`.
- If the part you need doesn't exist, **add it to the kit** (with stories). Don't style it in the app. If you can't
  add it now, use the nearest part and leave the gap visible, not hand-styled.

## The materials

| Material | What | Where |
| --- | --- | --- |
| **Luna chrome** | Saturated blue gradients, glossy plastic, light from the top left | Title bars, window frames (a solid ring, `--ui-frame-width`), taskbar and its buttons, Start button, task pane headers, CommandButton |
| **XP face** | Opaque beige face and white fields, crisp edges | Window bodies, dialogs, forms, lists, everything that is content |
| **Glass** | Backdrop blur, Luna-tinted, specular rim top-left, soft shadow | Floating chrome only: Start menu, ContextMenu, Balloon, Tooltip, notification banners, HUD panels over canvases. Window frames get only the glass *finish* (rim and shadow), never a see-through tint |

Glass rules:

- **Never on content.** Window bodies, lists, forms and text areas stay opaque.
- **Always tinted Luna** (smoky blue in Noir), never neutral frosted grey.
- Two strengths: `regular` (more tint, for surfaces with text on them: menus, banners) and `clear` (thin glass
  edges and strips only).
- No lensing or refraction. At most two glass layers stacked. Lighter blur over live canvases (Places, Figures).
- Reduce Transparency, missing `backdrop-filter` and Increase Contrast fall back to solid Luna. The tokens do this;
  parts don't handle it themselves.
- There is no `<Glass>` part. Glass belongs to the floating parts. If you want glass somewhere else, you want a
  floating part.

## Colour carries meaning

XP's severity code is kept in every scheme:

- **Blue** (`neutral`) is ordinary and reversible.
- **Green** (`go`) is start, go, create.
- **Amber** (`caution`) is softer high impact (Log Off, leave).
- **Red** (`danger`) is high impact and destructive (Close, delete, end).

Parts take `tone="neutral|go|caution|danger"`. Don't pick colours for emphasis; pick a tone or a part.

## Schemes and dark mode

- Schemes set the semantic tokens: **Luna Blue** (default, the reference) and **Noir** (dark, after Royale Noir:
  graphite chrome with a blue sheen, charcoal face, smoky glass, the severity code unchanged).
- The scheme is `data-ui-scheme` on the root. Dark mode is **only** a scheme. Parts read tokens and get dark mode
  for free. Never write `dark:` or `.dark .x`.
- A new scheme (Olive, Silver) must be one token block with no part changes. If it needs a part change, a token
  isn't semantic enough. Fix the token.

## Type

- Fonts come only from tokens: `--ui-font-ui` (Tahoma, then self-hosted Wine Tahoma), `--ui-font-title` (Trebuchet
  MS Bold), `--ui-font-mono`, `--ui-font-display`.
- **Chrome versus reading.** Chrome (menus, buttons, labels, list rows) uses `--ui-text-chrome`, XP's own 8pt
  Tahoma. Reading (chat messages, Board posts, Diary entries, CDU output, anything a Persona or Player wrote) uses
  `--ui-text-reading` with `--ui-leading-reading`. Comfort comes from the reading size and its line height, not from
  bigger chrome.
- Window and dialog title bars use `--ui-text-caption`. Section titles use `--ui-text-title`. `--ui-text-display` is
  for welcome screens and the Start button, and `--ui-text-logon` for the Welcome screen's greeting only.
- Nothing outside the ramp.

## Shape

- Liquid Glass shapes: generous radii, **concentric** from the window inward. A child's radius is its parent's radius
  minus the inset between them.
- Windows round all four corners (`--ui-radius-window`). Floating chrome is concentric with its anchor
  (`--ui-radius-float`). Panes and group boxes inside are concentric with the window.
- **Controls are capped** (`--ui-radius-control`, at most 5px). CommandButton, TextBox, ComboBox and checkboxes stay
  soft plastic rectangles and **never become capsules**.
- Pills (`--ui-radius-pill`) only where XP had them: the Start button and tray toggles.

## Motion

- Durations and easings only from tokens: `--ui-dur-instant|quick|move` and `--ui-ease-out|spring`.
- Only things that change **place or presence** move. That covers windows opening, minimising and restoring (a zoom
  to and from the taskbar button), floating chrome appearing (fade plus a small slide from its anchor) and morphs
  between related shapes (banner to notification center, taskbar button to window). Menus, the Start menu included, only
  float in.
  Hover gets a soft light-up glow.
- Content doesn't move: no animated messages, lists or layout, nothing looping except progress.
- Morphs use the View Transitions API through `morph()` in the kit, which names only the two morphing elements; the
  rest of the page never crossfades, and an element that morphed doesn't replay its own entry animation. Never call
  `document.startViewTransition` directly. There's no animation library.
- **Reduce Motion means very quick, not zero.** Movement and scale are dropped, and a short crossfade stays so the
  state change still reads. The tokens handle this.

## Icons

- Only through `<Icon name size>`, at 16 (menus, lists, toolbars), 32 (dialogs, task panes) or 48 (desktop, Start
  menu). Names are a closed set. Add a name to the kit; don't inline an image.
- Two tiers behind one name: **real** XP-style art, preferred, and **pixel**, an emoji rendered small, quantised and
  shadowed by `tools/icons/`. A real icon replaces a pixel one without changing any call site.
- **No emoji as chrome.** `icon="🏠"` is wrong; `<Icon name="home" />` is right. Emoji in content (what someone wrote,
  a Thing's glyph) is fine and renders in the kit's emoji font.

## Vocabulary

Use XP part names. When you reach for a web word, translate it:

| You were going to write… | Use |
| --- | --- |
| modal | `Dialog` |
| toast, snackbar | `Balloon`, or a notification banner |
| sidebar, drawer, side panel | `TaskPane` |
| card | `GroupBox`, or `ListView` with `view="tiles"` |
| dropdown, select | `ComboBox`, or `ContextMenu` for actions |
| button | `CommandButton`, or `ToolbarButton` in a Toolbar |
| input | `TextBox` |
| tabs | `Tabs` (a property sheet) |
| badge, chip | a tray count, or a `ListView` column |
| skeleton, spinner | `ProgressBar`, marquee |
| table, data grid | `ListView` with `view="details"` |
| accordion | `TaskPane` sections |
| tooltip, popover | `Tooltip`, or `Balloon` |
| login page, splash | `LogonScreen` |
| bordered scroll box, well | `SunkenField`, or `ListView` for items |
| caption, helper text, label text | `Text` (`tone="muted"` for help) |

The full inventory and the status of each part is in [references/parts.md](references/parts.md). A web pattern with
no XP equivalent gets a new part with an XP-style name, decided in the kit, or isn't used.

## Skins

An app with a strong identity (Winamp-like, a media player) may replace Luna chrome with a **Skin**: `<Window
skin="name">`, built in `frontend/ui/skins/<name>/`. That's the only place raw values and bitmaps are allowed. A Skin
may break Luna's colours, shapes and fonts. It must keep focus rings, Reduce Motion and Reduce Transparency, minimum
hit targets, window behaviour (drag, minimise to the taskbar, close), and a Luna taskbar button. Skins are
designer-made per app, never improvised to get around the rules.

## Departures from XP

Every deliberate difference from real XP is in [references/deviations.md](references/deviations.md), with what it
is, why and its bounds. When you compare against a period XP reference, a difference covered by the register is
fine and any other difference is a bug. A new departure needs a register entry in the same change.

## Before you finish

- [ ] Every surface is a kit part. Apps hold layout only, with no colour, font, radius, shadow or duration.
- [ ] No hex or rgb literals, `dark:`, emoji icons or deep `@partytown/ui/...` imports outside the kit.
- [ ] Glass appears only on floating chrome; content is opaque.
- [ ] Reading text uses the reading size. Nothing tiny.
- [ ] Checked in **Luna Blue and Noir**, with **Reduce Motion** and **Reduce Transparency** on.
- [ ] Keyboard: every control is reachable and has a visible focus ring.
- [ ] Any new part has stories for every state (default, hover, focus, active, disabled, Noir, reduced) and
      `run-story-tests` passes.
- [ ] Visual: open the story or screen, take a screenshot and compare it with `frontend/ui/reference/kit/` (exact
      target) and `frontend/ui/reference/xp/` (vibe). Name the differences, and fix or register each one.
- [ ] `pnpm check` passes in `frontend/`.

## Where things are

- Tokens: `frontend/ui/src/tokens.css`. Token families and names: [references/tokens.md](references/tokens.md).
- Parts: `frontend/ui/src/parts/<Part>/`, one folder per part (`<Part>.tsx`, `<Part>.css`, `<Part>.stories.tsx`),
  exported from its group barrel (`src/parts/chrome/index.ts` or `src/parts/controls/index.ts`). Its CSS is imported
  by the group's `index.css` next to that barrel, never from TSX. Part CSS sits in `@layer ui { }` and its classes
  start with `ui-`, which gives them the kit's focus ring and native-widget scheme. The layer order is `theme, base,
  components, ui, utilities`, so an app's Tailwind layout utilities still win over a part.
- The styleguide itself (swatches, type, shape, glass, motion, schemes): the kit Storybook, under **Styleguide**.
  Run it with `pnpm --filter @partytown/ui storybook`.
- Icons: `frontend/ui/icons/` (`icons.json` maps every name to its source emoji or art).
- Icons are regenerated with `python3 -I tools/icons/pixelate.py build`. Add a name to the script's set, never a
  loose image.
- Verify:
  - `pnpm check` from `frontend/` runs the boundary lint (`tools/lint/ui-boundary.mjs --help` lists the rules). Its
    baseline only shrinks: when you fix old violations, run it with `--update-baseline`.
  - `pnpm test:stories` runs every kit story as a test with axe a11y.
  - `pnpm --filter @partytown/ui snap <component-id>` refreshes golden references.
- Static states in stories: parts react to `data-ui-preview="hover|focus|active"` so stories can show a forced state
  without a pseudo-state addon. It's for stories only; apps never set it.
