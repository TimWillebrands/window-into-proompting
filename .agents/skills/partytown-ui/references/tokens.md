# Token families

This is the contract between the kit and everything that reads it. Values live only in `frontend/ui/src/tokens.css`.
Every token is `--ui-*` and every one is semantic: it says what it's for, never which colour it is.

## Scheme

`data-ui-scheme="luna-blue" | "noir"` on `<html>` (`setScheme` from `@partytown/ui`); without it the root is Luna
Blue. Every colour, glass and shadow token is set per scheme, plus `--ui-color-scheme` (`light` or `dark`, which the
kit's base applies as `color-scheme` to elements with a `ui-` class, so native scrollbars and fields follow). Type,
space, radius and motion are shared across schemes. Any element can carry `data-ui-scheme` to render its subtree in
another scheme.

The system preferences can be forced for stories and tests with `data-ui-reduce-motion`,
`data-ui-reduce-transparency` and `data-ui-contrast="more"`, on `<html>` or on a subtree that also carries
`data-ui-scheme`.

## Colour

| Group | Tokens |
| --- | --- |
| Face (content) | `--ui-face`, `--ui-face-lite`, `--ui-face-edge`, `--ui-field`, `--ui-field-edge`, `--ui-content` |
| Text | `--ui-text`, `--ui-text-muted`, `--ui-text-disabled`, `--ui-text-on-chrome`, `--ui-text-error` (an error message on the face or a field), `--ui-link`, `--ui-link-hover` |
| Selection and focus | `--ui-select`, `--ui-select-text`, `--ui-select-inactive` and `--ui-select-inactive-text` (the grey selection of an unfocused list or tree), `--ui-focus`, `--ui-focus-on-chrome` (the focus ring on Luna-blue chrome: title bars, taskbar, tray, Start) |
| Frame | `--ui-frame`, `--ui-frame-inactive` (the solid ring around a window, continuing the title bar down the sides), `--ui-frame-edge`, `--ui-frame-edge-inactive` (its darker bottom-right edge) |
| Chrome | `--ui-titlebar`, `--ui-titlebar-inactive`, `--ui-titlebar-text`, `--ui-taskbar`, `--ui-taskbar-button`, `--ui-taskbar-button-hover`, `--ui-taskbar-button-active`, `--ui-tray` (the lighter notification area, its bevelled left edge included), `--ui-start`, `--ui-start-menu-header`, `--ui-start-menu-footer`, `--ui-start-menu-places` and `--ui-start-menu-places-text` (the Start menu's blue bands and its light-blue right column), `--ui-close`, `--ui-chrome-edge` (the dark line around chrome buttons), `--ui-control`, `--ui-control-hover`, `--ui-control-active`, `--ui-control-edge`, `--ui-control-hot` (the orange hot-track: hover glow, the Tabs hot edge), `--ui-taskpane`, `--ui-taskpane-section`, `--ui-taskpane-body`, `--ui-taskpane-body-special`, `--ui-group-title` |
| Controls | `--ui-combo-button`, `--ui-combo-button-edge`, `--ui-combo-arrow` (the ComboBox drop button), `--ui-check-mark` (check mark and radio dot), `--ui-progress-chunk` (a ProgressBar segment, gloss included) |
| Tones | `--ui-tone-neutral`, `--ui-tone-go`, `--ui-tone-caution`, `--ui-tone-danger`, each with `-text` for text set on it and `-ink` for the tone as text on the face, a field or content (AA in every scheme: signed numbers, status words). `--ui-tone-gloss`, `--ui-tone-gloss-hover`, `--ui-tone-gloss-active` are translucent overlays that make a tone fill a glossy button face: `background: var(--ui-tone-gloss), var(--ui-tone-go)` |

| Welcome screen | `--ui-logon-band` (the top band), `--ui-logon-band-bottom` (the bottom band, dark blue to violet), `--ui-logon-face` (the face with its small top-left glow), `--ui-logon-rule-top`, `--ui-logon-rule-bottom`, `--ui-logon-divider`, `--ui-logon-text`, `--ui-logon-text-muted`, `--ui-logon-tile-hover`, `--ui-logon-tile-selected`, `--ui-logon-picture-edge` |

Gradients are tokens too (`--ui-titlebar` is a full `linear-gradient(...)`), so a scheme can reshape them. Chrome
that XP drew in hard bands keeps them: `--ui-taskbar` and `--ui-tray` use pixel stops (a 1px top edge, a light band,
a flat body, a dark bottom band), not a smooth gloss.

## Series

`--ui-series-1` up to `--ui-series-8`, then `--ui-series-other`: a categorical palette for data, set per scheme. It's
for marks whose colour *is* data: provider shares, a person's colour, graph node kinds. Apps pass a series token as a
data colour (a part's colour prop such as `ShareBar` `colour` or `Swatch` `colour`, or a `--ui-*` custom property); parts
never pick one for emphasis, and text, chrome and status never use them (status is a tone).

- Assign in fixed order and keep a colour with its entity, never its rank. A ninth category folds into
  `--ui-series-other`; never generate a hue.
- The order is tuned for colour-vision deficiency on neighbouring marks (stacked bars, lines) and checked on
  `--ui-face` and `--ui-content` in both schemes. Where every pair can touch (scatter, graphs), only the first three
  stay apart for every reader: past three, label the marks.
- Identity is never colour alone: keep a legend or labels next to them. They aren't text colours and aren't held to
  text contrast.

## Chrome finish

Shadow and text-shadow values for glossy Luna chrome, set per scheme: `--ui-chrome-gloss` (the light top and left
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
- `--ui-glass-alpha-regular` and `--ui-glass-alpha-clear` are the fallback's lever, not for parts: every translucent
  value in a scheme block (the tints, `--ui-taskbar`) takes its alpha from them, and the fallback sets them to 1.

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

`--ui-radius-control` (capped at 5px), `--ui-radius-pane`, `--ui-radius-float`, `--ui-radius-window`,
`--ui-radius-pill`, and `--ui-frame-width`, the window frame ring. A window's inset is its frame, so its body corners
are the window radius minus the frame width and stay concentric at any frame width. Concentric radii: a container sets `--ui-radius-here` and `--ui-inset`, and a child uses
`--ui-radius-inner`, which is `max(var(--ui-radius-control), var(--ui-radius-here) - var(--ui-inset))`, recomputed on
every element. A container nested in another (a pane in a window) can't take its own radius from `--ui-radius-inner`
and also set `--ui-radius-here` (that's a cycle), so it uses its own token (`--ui-radius-pane`, a step inside the
window body's radius) and sets `--ui-radius-here` to that. Only leaves use `--ui-radius-inner`.

## Elevation

`--ui-shadow-control`, `--ui-shadow-window`, `--ui-shadow-window-inactive`, `--ui-shadow-float`.

## Motion

`--ui-dur-instant`, `--ui-dur-quick`, `--ui-dur-move`, `--ui-dur-loop` (one pass of the only loop, the ProgressBar
marquee), `--ui-ease-out`, `--ui-ease-spring` (a CSS `linear()` spring with a slight overshoot), `--ui-ease-step` (a
crisp on/off switch, for XP's Dialog title flash), `--ui-motion-travel` (1, or 0 under Reduce Motion: multiply every offset and scale
change by it). Under `prefers-reduced-motion` the durations shrink to a short crossfade, the spring becomes
`--ui-ease-out` and travel drops to 0, and View Transition groups stop moving. Parts never check reduced motion
themselves.
