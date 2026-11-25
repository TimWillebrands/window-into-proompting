# Token families

This is the contract between the kit and everything that reads it. Values live only in `frontend/ui/src/tokens.css`.
Every token is `--ui-*`. Parts read semantic tokens only; `--ui-p-*` primitives are the schemes' raw material.

## Two tiers

Colour is set in two tiers, both in `tokens.css`:

1. **Primitives** (`--ui-p-*`): a scheme is one block of them and nothing else. Parts and apps never read them.
2. **Semantic tokens** (everything below): defined **once**, for every scheme, from the primitives with `var()`,
   `color-mix()` and relative colour (`oklch(from var(--ui-p-chrome-3) calc(l + 0.05) c h)`). A semantic token says
   what it's for, never which colour it is.

Adding a scheme is one primitives block: add its name to `schemes` in `scheme.ts`, copy a block, change the values,
check the Colour/Contrast story. If it needs anything else, a semantic token isn't derived well enough: fix the
derivation for every scheme, don't override it per scheme.

### Primitives

| Primitive | What it is |
| --- | --- |
| `--ui-p-chrome-1` to `--ui-p-chrome-5` | The chrome hue ramp, light to dark: 1 the pale tint (task pane bodies, the Start menu's places column, drop buttons), 2 the sheen (title bar gloss, button tops), 3 the chrome itself (title bars, taskbar), 4 deep chrome (frames, pressed task buttons), 5 the shade (edges, dark bands, chrome shadows) |
| `--ui-p-chrome-ink` | Text and glyphs on chrome: white on dark chrome, dark on Silver's light chrome |
| `--ui-p-desktop` | The desktop's own colour, behind the wallpaper; the Welcome screen is drawn from it (XP's blue in every light scheme) |
| `--ui-p-face`, `--ui-p-face-lite`, `--ui-p-face-edge`, `--ui-p-content` | The XP face, its lighter step, its edge, and white content |
| `--ui-p-ink` | Text on the face and content |
| `--ui-p-ink-l` | A number: the OKLCH lightness of coloured text on the face (links, tone inks, group titles, muted text). About 0.5 in a light scheme, 0.78 in a dark one; it keeps them AA |
| `--ui-p-select` | The selection colour; group titles and links take their hue from it |
| `--ui-p-hot` | The orange hot-track |
| `--ui-p-neutral`, `--ui-p-go`, `--ui-p-caution`, `--ui-p-danger` | The four tone fills (the severity code, kept in every scheme) |
| `--ui-p-glass-alpha` | Glass and taskbar opacity |
| `--ui-p-gloss`, `--ui-p-shadow`, `--ui-p-lift` | Numbers: strength of white gloss and rims (1 in light schemes, less in dark ones), strength of shadows and dark edges (1, more in dark ones), and how far raised surfaces (controls, the inactive selection) lift above the face (0 in light schemes) |
| `--ui-p-color-scheme` | `light` or `dark` |

The schemes:

- **Luna Blue** (`luna-blue`, the default and the reference): the primitives are Luna's own colours, so every
  semantic token derives back to its XP value.
- **Olive Green** (`olive-green`, XP's Homestead): olive chrome, a slightly greener beige face, an olive selection,
  the tones and the blue Welcome screen unchanged.
- **Silver** (`silver`, XP's Metallic): silver-lavender chrome with dark chrome ink, a cool grey face, a slate-blue
  selection, the green Start button and the tones unchanged.
- **Noir** (`noir`, after Royale Noir): graphite chrome with a blue sheen, charcoal face, light ink, more shadow and
  less gloss, `--ui-p-lift` above zero.

Primitives are written as `oklch()`: Chrome drops the hue of a low-chroma colour converted from hex (it reads as
`none`, then as 0 in a relative colour), which would turn Silver and Noir derivations pink.

### Allowed exceptions

A scheme block holds primitives only, except for these few semantic overrides, set after the semantic block, for
things that differ in kind:

| Scheme | Token | Why |
| --- | --- | --- |
| Silver | `--ui-chrome-text-shadow` | Dark text on light chrome gets a light emboss, not a dark drop shadow |
| Noir | `--ui-series-*` | The data palette has a dark variant, validated on the dark face; light schemes share Luna's |

### Applying a scheme

`data-ui-scheme="luna-blue" | "olive-green" | "silver" | "noir"` on `<html>` (`setScheme` from `@partytown/ui`;
`schemes`, `defaultScheme`, `getScheme` and `isScheme` are exported too); without it the root is Luna Blue. Any element
can carry `data-ui-scheme` to render its subtree in another scheme: the semantic block is declared on `:root`, on
every `[data-ui-scheme]` and on every `[data-ui-skin]`, so it resolves against the nearest primitives. A Skin can
therefore override just primitives (`--ui-p-face`, `--ui-p-chrome-3`…) on its window and get every semantic token
re-derived; the allowed exceptions and the preference fallbacks are repeated for skinned windows so they still win.
In the app the saved scheme reaches `<html>` before the first paint through an inline script in the document head
(`schemeScript` in `theme-store`), so nothing flashes and React's hydration doesn't trip over it. `--ui-color-scheme` (`light` or
`dark`) is applied by the kit's base as `color-scheme` to elements with a `ui-` class, so native scrollbars and fields
follow. Type, space, radius and motion are shared across schemes.

The system preferences can be forced for stories and tests with `data-ui-reduce-motion`,
`data-ui-reduce-transparency` and `data-ui-contrast="more"`, on `<html>` or on a subtree that also carries
`data-ui-scheme`.

## Colour

| Group | Tokens |
| --- | --- |
| Face (content) | `--ui-face`, `--ui-face-lite`, `--ui-face-edge`, `--ui-field`, `--ui-field-edge`, `--ui-content` |
| Text | `--ui-text`, `--ui-text-muted`, `--ui-text-disabled`, `--ui-text-on-chrome` (text on the scheme's chrome: taskbar, tray; dark in Silver), `--ui-desktop-text` (light text on `--ui-desktop` and the wallpaper: desktop icon labels, the Welcome screen), `--ui-text-error` (an error message on the face or a field), `--ui-link`, `--ui-link-hover` |
| Selection and focus | `--ui-select`, `--ui-select-text`, `--ui-select-inactive` and `--ui-select-inactive-text` (the grey selection of an unfocused list or tree), `--ui-focus`, `--ui-focus-on-chrome` (the focus ring on chrome: title bars, taskbar, tray, Start) |
| Chrome solids | `--ui-chrome-tint`, `--ui-chrome-sheen`, `--ui-chrome`, `--ui-chrome-deep`, `--ui-chrome-shade` (the chrome ramp, for parts that derive their own shades), `--ui-desktop` |
| Frame | `--ui-frame`, `--ui-frame-inactive` (the solid ring around a window, continuing the title bar down the sides), `--ui-frame-edge`, `--ui-frame-edge-inactive` (its darker bottom-right edge) |
| Chrome | `--ui-titlebar`, `--ui-titlebar-inactive`, `--ui-titlebar-text`, `--ui-taskbar`, `--ui-taskbar-button`, `--ui-taskbar-button-hover`, `--ui-taskbar-button-active`, `--ui-close`, `--ui-chrome-edge` (the dark line around chrome buttons), `--ui-control`, `--ui-control-hover`, `--ui-control-active`, `--ui-control-edge`, `--ui-control-hot` (the orange hot-track: hover glow, the Tabs hot edge), `--ui-group-title`, `--ui-check-mark` (check mark and radio dot), `--ui-glint` (the specular white of glints) |
| Tones | `--ui-tone-neutral`, `--ui-tone-go`, `--ui-tone-caution`, `--ui-tone-danger`, each with `-text` for text set on it and `-ink` for the tone as text on the face, a field or content (AA in every scheme: signed numbers, status words). `--ui-tone-gloss`, `--ui-tone-gloss-hover`, `--ui-tone-gloss-active` are translucent overlays that make a tone fill a glossy button face: `background: var(--ui-tone-gloss), var(--ui-tone-go)` |

Gradients are tokens too (`--ui-titlebar` is a full `linear-gradient(...)`), each stop derived from a primitive.
Chrome that XP drew in hard bands keeps them: `--ui-taskbar` uses pixel stops (a 1px top edge, a light band, a flat
body, a dark bottom band), not a smooth gloss.

### Part-local colours

What only one part paints isn't a token. The part derives it in its own CSS from semantic tokens, as a local custom
property (`--ui-logon-band` on `.ui-logon`) or inline: the Welcome screen (from `--ui-desktop`), the Start menu's
header, footer and places column (from the chrome solids), the task pane's background, headings and bodies (from
`--ui-chrome` and `--ui-chrome-tint`), the tray (from `--ui-chrome` and `--ui-chrome-sheen`), the ComboBox drop button
(from `--ui-chrome-tint` and `--ui-field-edge`) and the ProgressBar chunk (from `--ui-tone-go`). A colour two parts
share becomes a semantic token.

## Series

`--ui-series-1` up to `--ui-series-8`, then `--ui-series-other`: a categorical palette for data, in a light variant
(Luna Blue, Olive Green, Silver) and a dark one (Noir). It's
for marks whose colour *is* data: provider shares, a person's colour, graph node kinds. Apps pass a series token as a
data colour (a part's colour prop such as `ShareBar` `colour` or `Swatch` `colour`, or a `--ui-*` custom property); parts
never pick one for emphasis, and text, chrome and status never use them (status is a tone).

- Assign in fixed order and keep a colour with its entity, never its rank. A ninth category folds into
  `--ui-series-other`; never generate a hue.
- The order is tuned for colour-vision deficiency on neighbouring marks (stacked bars, lines) and checked on
  `--ui-face` and `--ui-content` in the light and the dark variant. Where every pair can touch (scatter, graphs), only the first three
  stay apart for every reader: past three, label the marks.
- Identity is never colour alone: keep a legend or labels next to them. They aren't text colours and aren't held to
  text contrast.

## Chrome finish

Shadow and text-shadow values for glossy Luna chrome, derived per scheme through `--ui-p-gloss` and `--ui-p-shadow`: `--ui-chrome-gloss` (the light top and left
inner edge, a `box-shadow` list), `--ui-chrome-press` (the inset shadow of a pressed chrome button),
`--ui-chrome-glow` (the soft hover light-up), `--ui-chrome-text-shadow` (white text on chrome).

## Glass

`--ui-glass-tint-regular`, `--ui-glass-tint-clear`, `--ui-glass-blur-regular`, `--ui-glass-blur-clear`,
`--ui-glass-blur-canvas`, `--ui-glass-rim` (the specular top-left inner highlight, a `box-shadow` layer),
`--ui-glass-edge` (the 1px outline), `--ui-glass-shadow`. The fallback (solid fill under Reduce Transparency, without
`backdrop-filter`, and stronger edges under Increase Contrast) is applied in `tokens.css` through `@media` and
`@supports`.

- A blur token is a whole `backdrop-filter` value (`none` under the fallback). Parts write it to both
  `backdrop-filter` and `-webkit-backdrop-filter`.
- `--ui-glass-alpha-regular` and `--ui-glass-alpha-clear` are the fallback's lever: every translucent chrome value
  (the tints, `--ui-taskbar`, the Start menu bands, the tray) takes its alpha from them, and the fallback sets them
  to 1. Parts may use `--ui-glass-alpha-regular` only to give a part-local chrome colour the same alpha.

## Type

`--ui-font-ui`, `--ui-font-title`, `--ui-font-mono`, `--ui-font-display`, `--ui-font-emoji`. Sizes:
`--ui-text-chrome` (11px, XP's 8pt Tahoma), `--ui-text-reading` (13px), `--ui-text-caption` (13px, window and dialog
title bars, XP's 10pt Trebuchet), `--ui-text-title` (15px), `--ui-text-display` (18px, welcome screens and the Start
button) and `--ui-text-logon` (the Welcome screen's greeting only). Leading: `--ui-leading-chrome`,
`--ui-leading-reading`.

## Space

`--ui-space-1` up to `--ui-space-6`: 2, 4, 6, 8, 12, 16px. Apps keep Tailwind's 4px-base spacing, where the kit
scale is steps `0.5`, `1`, `1.5`, `2`, `3`, `4`. Use those steps; others (`gap-5`, `p-7`) are off-scale.

## Shape

`--ui-radius-control` (3px), `--ui-radius-control-max` (5px), `--ui-radius-pane`, `--ui-radius-float`, `--ui-radius-window`,
`--ui-radius-pill`, and `--ui-frame-width`, the window frame ring. Task pane columns are `--ui-taskpane-width` (XP's
narrow pane) and `--ui-taskpane-width-wide`, a details pane is `--ui-details-width` (or the wide task pane width), and
`--ui-tile-width` is `Tiles`' default minimum tile width; they are shared, not per scheme. A window's inset is its frame, so its body corners
are the window radius minus the frame width and stay concentric at any frame width. Concentric radii: a container sets `--ui-radius-here` and `--ui-inset`, and a child uses
`--ui-radius-inner`, which is `max(var(--ui-radius-control), var(--ui-radius-here) - var(--ui-inset))`, recomputed on
every element. A container nested in another (a pane in a window) can't take its own radius from `--ui-radius-inner`
and also set `--ui-radius-here` (that's a cycle), so it uses its own token (`--ui-radius-pane`, a step inside the
window body's radius) and sets `--ui-radius-here` to that. Only leaves use `--ui-radius-inner`. Controls use `--ui-radius-control-inner`, the same radius clamped to `--ui-radius-control-max`, so a button in a big rounded dialog never turns into a capsule.

## Elevation

`--ui-shadow-control`, `--ui-shadow-window`, `--ui-shadow-window-inactive`, `--ui-shadow-float`.

## Motion

`--ui-dur-instant`, `--ui-dur-quick`, `--ui-dur-move`, `--ui-dur-loop` (one pass of the only loop, the ProgressBar
marquee), `--ui-ease-out`, `--ui-ease-spring` (a CSS `linear()` spring with a slight overshoot), `--ui-ease-step` (a
crisp on/off switch, for XP's Dialog title flash), `--ui-motion-travel` (1, or 0 under Reduce Motion: multiply every offset and scale
change by it). Under `prefers-reduced-motion` the durations shrink to a short crossfade, the spring becomes
`--ui-ease-out` and travel drops to 0, and View Transition groups stop moving. Parts never check reduced motion
themselves.
