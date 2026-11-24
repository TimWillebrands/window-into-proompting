# Deviations register

These are the deliberate departures from real Windows XP. Compared with a period reference, anything listed here is
intended and anything not listed is a bug. A new departure is added here in the same change that introduces it.

| What | Why | Bounds |
| --- | --- | --- |
| Glass material on floating chrome | The glass half: a separate layer for chrome that floats over content | Floating chrome only. Always Luna-tinted. No lensing. Solid fallback |
| Window frame: a solid Luna ring with a glass finish | Luna's frame for recognition, glass only as the specular top-left rim and the soft floating shadow | `--ui-frame-width` (thinner than XP's 4px), following `--ui-radius-window`. Never see-through. Maximised windows have no frame |
| Windows round all four corners, more generously than Luna's top corners | Liquid Glass shape language | `--ui-radius-window`. Maximised windows are square |
| Concentric radii throughout | Liquid Glass shape language, so nested shapes sit together | Controls capped at `--ui-radius-control`, never capsules |
| Larger floating-chrome radii | Glass softness on menus, balloons, banners | `--ui-radius-float`, concentric with the anchor |
| Bigger reading type: chrome is XP's 11px, reading 13px | Comfort through line-height and reading size, not bigger chrome. XP's 8pt stays for chrome, so it reads as XP; what people wrote gets 13px and `--ui-leading-reading` | The type ramp only |
| Close button nests in the window corner | Liquid Glass concentricity. A square 21px Close in a 14px window corner collides with the curve | The Close caption's top-right radius is `--ui-radius-window` minus its inset from the edge; its other corners and the other captions keep `--ui-radius-control`. Square again when maximised or skinned |
| Roomier hit targets and row padding | Comfort, touch and accessibility | Visual sizes stay XP-like, hit areas grow |
| Visible focus rings | Keyboard accessibility. XP's dotted focus rectangle is too faint | `--ui-focus`, on every interactive part |
| Taskbar buttons close from their window menu only | XP had no close button on a task either; its right-click menu is kept, trimmed to Restore, Minimise, Maximise and Close | Move and Size are left out: windows are dragged and resized directly |
| Contrast-tuned chrome blues | White chrome text meets AA: taskbar buttons and the tray are a shade deeper than XP's | `--ui-taskbar-button`, `--ui-tray` |
| Spring motion, morphs, zooms | The glass half: motion carries place and presence | Motion tokens only. Never on content |
| Reduce Motion keeps a very quick crossfade | A state change has to read, even at its most reduced | Tokens only |
| Noir dark scheme | Dark mode. After Royale Noir, an unreleased XP style | Keeps the severity code and Luna's shapes |
| Pixel icons from emoji | Enough icons, fast, until real art lands | Through `<Icon>` only. Real art replaces them by name |
| Self-hosted Wine Tahoma | Tahoma is missing on Linux and mobile | Fallback after a system Tahoma |
| Content emoji in one emoji font | The same look on every platform | Content only, never chrome |
| Light halo on dark pixel icons | Near-black glyphs (check, add, close…) vanished on the Noir face | Added by `tools/icons/` only to icons whose mean luminance is very low: a 1px light ring at low alpha, next to dark pixels only. Nearly invisible on Luna |
| Toned progress bars | A level that is busy or full needs the severity code, not just "progress" | `ProgressBar` `tone`: blue, amber or red chunks from the tone tokens. No tone stays XP's green |
| A current GroupBox | "Your spot" among several group boxes needs to stand out; XP had no such state | `GroupBox` `current`: a 2px `--ui-select`-tinted edge and a bold title. At most one per view |
| Task pane titles wrap, and a heading can carry a count | XP cut long titles off in narrow panes; a count in the heading saves a line | Titles wrap at words instead of ending in an ellipsis. The count (`aside`) is plain text, never a badge |
| A categorical data palette | XP had no palette for data; charts, shares and graphs need one that reads in both schemes | `--ui-series-*`, for data marks only. Never chrome, text or status |
