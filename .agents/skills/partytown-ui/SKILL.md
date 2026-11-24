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

## Layout

Apps do layout, but they don't hand-roll it. Reach for these in order:

1. **`WindowLayout`** is a window body: XP's program anatomy as slots that fill it. `menuBar`, `toolbar` (an array
   gives one band each), `addressBar`, `taskPane` (left, a `nav`; `taskPaneWidth="regular|wide"` or a length),
   the content (`children`, named by `contentLabel`), `detailsPane` (`detailsPlacement="end|bottom"`, `detailsWidth`),
   `footer` (a composer) and `statusBar`. Below `narrowBelow` (520 by default) the task pane stacks above the content
   or hides (`taskPaneNarrow="hide"`); below `detailsNarrowBelow` the details pane goes under it or hides
   (`detailsNarrow="hide"`). Never measure the window yourself. The content is the only thing that scrolls:
   `scroll="content"` (default), `both` for wide content, `none` when the content scrolls itself (a `ListView`,
   `ChatLog`, `Tabs` or `Pane`). Everything in it has a zero minimum size, so nothing props the window open.
2. **`Pane`** is any other region that scrolls. It grows in a flex parent, shrinks to zero and scrolls (`surface`,
   `pad`, `text`). It replaces `flex-1 min-h-0 overflow-y-auto`. A `GroupBox` with `fill` does the same for a titled
   box: it fills its parent and its list or log scrolls.
3. **`Stack`** is a row or a column: `direction`, `gap`, `rowGap`, `pad`, `align`, `justify`, `wrap`, `grow`,
   `inline`, `as` (typed per element: `as="form"` takes `noValidate`, `as="dl"` works). `Stack.Item` with `grow`
   takes the rest of the row; with `truncate` it ends in an ellipsis. A zero minimum width is built in. Spaces are
   steps: `xxs`, `xs`, `sm`, `md`, `lg`, `xl` are `--ui-space-1` to `6`, which are Tailwind's `0.5`, `1`, `1.5`, `2`,
   `3`, `4` (so `gap-2` is `gap="md"`).
4. **`Columns`** is fluid columns inside content (a fitting room beside a catalogue): `Columns.Column` with a `weight`
   and a `minWidth`; below `narrowBelow` they stack, or with `narrow="one"` show the column `show` under the app's
   `switcher`. **`Tiles`** is a grid of cards that fits as many columns as `minWidth` allows. Neither is a side pane:
   that's `WindowLayout`.
5. Tailwind layout utilities are for one-offs: a width, a margin, `self-center`. The `layout-soup` lint fails a
   className with more than 6 utilities, an arbitrary grid template or `calc()`. No `flex-[n]` weights or container
   query classes: that's `Columns`, `Tiles` or a `WindowLayout` breakpoint.

The Control Panel (`frontend/src/apps/config-panel/`) and the Board (`frontend/src/apps/board/`) are the reference.

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

- Four schemes, as in XP plus a dark one: **Luna Blue** (default, the reference), **Olive Green** (Homestead: olive
  chrome, a greener beige face), **Silver** (Metallic: silver chrome with dark chrome text, a slate-blue selection,
  the green Start button) and **Noir** (dark, after Royale Noir: graphite chrome with a blue sheen, charcoal face,
  smoky glass). The severity code and the blue Welcome screen are the same in every light scheme.
- A scheme is **one block of primitives** (`--ui-p-*`). Every semantic token is defined once from them, for all
  schemes. Parts and apps read semantic tokens only, never a primitive.
- The scheme is `data-ui-scheme` on the root (`setScheme`; the app's choice lives in `theme-store`, set from Display
  Properties). Dark mode is **only** a scheme. Parts read tokens and get every scheme for free. Never write `dark:`,
  `.dark .x` or `[data-ui-scheme=…]` in a part.
- A new scheme must be one primitives block with no part changes. If it needs a part change or a per-scheme
  override, a token isn't derived well enough. Fix the derivation. The few allowed overrides are listed in
  [references/tokens.md](references/tokens.md).
- Check visual work in **Luna Blue and Noir** at least; anything touching chrome colour in all four.

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

## Desktop and programs

The kit is a small OS. `Desktop` draws the windows, the taskbar and its buttons, the Start button, and handles focus,
stacking, drag, resize, snapping, minimise and maximise; `windowReducer` is its pure state, which the app keeps (and
persists) and feeds back through `state` and `onAction`. An app is a **program** in the registry (`id`, `title`,
`icon`, `render`, `defaultBounds`, `minSize`, `singleton`), opened with `launchProgram`. Programs never position,
stack or animate windows themselves and never call the transition helpers; they render their body (a `WindowLayout`)
and may put something in their title bar with `useTitleBarAccessory`. The app supplies only its domain bits through
the slots: `wallpaper`, `icons`, `startMenu`, `tray`.

A minimised window stays mounted (hidden and inert), so it keeps its state and comes back without replaying its
entry. A program that runs a render loop or polls (a 3D scene, a canvas, a live feed) pauses while
`useWindowMinimised()` is true: `frameloop="never"`, no `requestAnimationFrame`, `refetchInterval: false`.

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
| alert, callout, banner in a pane | `StatusText`, `variant="bar"` across a pane |
| app shell, page layout, Explorer window | `WindowLayout` |
| scroll area, scroll container | `Pane` |
| flex row or column, HStack, VStack | `Stack` (`Stack.Item` to grow or truncate) |
| two-column layout, split view (inside content) | `Columns` |
| card grid, auto-fill grid | `Tiles` |

The full inventory and the status of each part is in [references/parts.md](references/parts.md). A web pattern with
no XP equivalent gets a new part with an XP-style name, decided in the kit, or isn't used.

## Skins

An app with a strong identity (Messenger, a media player) may replace Luna chrome for its windows with a **Skin**,
designer-made per app, never improvised to get around the rules. A Skin is a folder in `frontend/ui/skins/<name>/`,
the only place raw values and bitmaps are allowed (the boundary lint forbids `[data-ui-skin="…"]` anywhere else):

- `<name>.ts`: the manifest, `defineSkin({ name, label, titleBar: 'standard' | 'tall', maximise, minSize?, regions? })`.
  Register it in `skins/index.ts`, which gives the `SkinName` type, and import its CSS from `skins/index.css`.
- `<name>.css`, in `@layer ui`, every selector under `.ui-window[data-ui-skin="<name>"]`. The main lever is
  **tokens**: the semantic tokens are re-derived on a skinned window, so set primitives first (`--ui-p-face`,
  `--ui-p-ink`, `--ui-p-select`, `--ui-p-chrome-1` to `5`…) and override a semantic token (`--ui-control*`,
  `--ui-titlebar`, `--ui-frame`, `--ui-radius-window`…) only where the Skin genuinely differs from the derivation.
  Every part inside keeps working with the new values. Then reshape the
  frame, title bar and captions with selectors, and paint `regions` (aria-hidden spans, `data-region`, under the title
  bar's text or the frame's content) for decoration.
- A Skin decides how it follows the scheme: it may keep one look in the light schemes, but Noir gets a dark variant
  (`[data-ui-scheme="noir"] .ui-window[data-ui-skin="<name>"]`), and it re-applies Increase Contrast's edges, since
  its own tokens override the scheme's.

Opt in with `<Window skin="name">`, or `skin` on a program entry, which `Desktop` passes on (its `minSize` and
`maximise` too). A Skin must keep focus rings, Reduce Motion and Reduce Transparency, hit targets of at least 24px,
window behaviour (drag by the title bar, minimise to the taskbar, close, snap, maximise unless the manifest says no)
and a Luna taskbar button. Stories go next to it (`skins/<name>/<Name>.stories.tsx`, titled `Skins/<Name>`), in all
four schemes. MSN (`msn`) is the reference.

## Departures from XP

Every deliberate difference from real XP is in [references/deviations.md](references/deviations.md), with what it
is, why and its bounds. When you compare against a period XP reference, a difference covered by the register is
fine and any other difference is a bug. A new departure needs a register entry in the same change.

## Before you finish

- [ ] Every surface is a kit part. Apps hold layout only, with no colour, font, radius, shadow or duration.
- [ ] No hex or rgb literals, `dark:`, emoji icons or deep `@partytown/ui/...` imports outside the kit.
- [ ] Glass appears only on floating chrome; content is opaque.
- [ ] Reading text uses the reading size. Nothing tiny.
- [ ] Checked in **Luna Blue and Noir** (all four schemes for chrome colour), with **Reduce Motion** and
      **Reduce Transparency** on.
- [ ] Keyboard: every control is reachable and has a visible focus ring.
- [ ] Any new part has stories for every state (default, hover, focus, active, disabled, Noir, reduced) and
      `run-story-tests` passes.
- [ ] Visual: open the story or screen, take a screenshot and compare it with `frontend/ui/reference/kit/` (exact
      target) and `frontend/ui/reference/xp/` (vibe). Name the differences, and fix or register each one.
- [ ] `pnpm check` passes in `frontend/`.

## Where things are

- Tokens: `frontend/ui/src/tokens.css`. Token families and names: [references/tokens.md](references/tokens.md).
- Parts: `frontend/ui/src/parts/<group>/<Part>/`, one folder per part (`<Part>.tsx`, `<Part>.css`,
  `<Part>.stories.tsx`), grouped by role: `chrome` (desktop, frame, floating), `controls` (atoms you operate or read),
  `views` (data displays), `surfaces` (containers) and `icons`. A part is exported from its group's `index.ts`, and its
  CSS is imported by the group's `index.css` next to that barrel, never from TSX. Part CSS sits in `@layer ui { }` and its classes
  start with `ui-`, which gives them the kit's focus ring and native-widget scheme. The layer order is `theme, base,
  components, ui, utilities`, so an app's Tailwind layout utilities still win over a part.
- The styleguide itself (swatches, type, shape, glass, motion, schemes): the kit Storybook, under **Styleguide**.
  Run it with `pnpm --filter @partytown/ui storybook`. Part stories are titled `<Group>/<Part>`; their shared helpers
  (`stage`, `States`, `Desk`, `noir`, `reducedMotion`, `reducedTransparency`, `preview`) are in
  `src/story-kit/story-kit.tsx`.
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
