# Parts

The closed set of kit parts, grouped by role. Each lives in `frontend/ui/src/parts/<group>/<Part>/` with its CSS and
stories (titled `<Group>/<Part>`), and is exported from `@partytown/ui`. Apps import only from the root, so a part's
group is a kit detail. Every part below is built.

## Chrome (the desktop, the window frame, everything floating)

| Part | What | Material |
| --- | --- | --- |
| `Window` | An app window: frame, title bar, caption buttons, body (lay the body out with `WindowLayout`). `useTitleBarAccessory(node)` from inside puts a control in its title bar. `skin` swaps Luna for a Skin (see below) | Glass frame (clear), Luna title bar, XP face body |
| `Dialog` | A modal Window with a button row (OK/Cancel), always centred over its owner | Same as Window |
| `Taskbar` / `TaskbarButton` | The bottom bar: Start button, window buttons (with XP's window menu), tray | Glass (clear) over Luna blue |
| `StartButton` | The green pill | Luna |
| `StartMenu` / `StartMenuItem` / `StartMenuAction` | Two-column menu with user header and Log Off / Shut Down footer | Glass (regular) |
| `Tray` / `TrayButton` | The notification area: icons, clock, counts | Glass (clear) |
| `ContextMenu` | Right-click or action menu, with nested submenus; `useContextMenu()` opens it | Glass (regular) |
| `MenuBar` | File / Edit / View … inside a Window | XP face |
| `Balloon` | A pointer balloon tied to an anchor (tray, field), with a title and close | Glass (regular) |
| `Tooltip` | Small hover hint | Glass (regular) |
| `HudPanel` | A small panel floating over a canvas, anchored to an edge or corner, with an optional title and close. A Toolbar inside it is bare. A `top` or `bottom` panel keeps a gutter clear on both sides for corner panels (a zoom toolbar), so a long title ends in an ellipsis instead of running under them | Glass (regular, canvas blur) |
| `Desktop` | The desktop as a controlled window manager: renders `state` with a `programs` registry, the `Taskbar` with a button per window (window menu included), the `StartButton`; focus on press, drag by the title bar, resize from edges and corners, snap to the left or right half or maximise against the edges, double-click to maximise, minimise and restore as a morph to and from the taskbar. A minimised window stays mounted, hidden and inert, so it keeps its state and doesn't replay its entry; `useWindowMinimised()` tells a program it's minimised. A program's `skin` reaches its `Window`, with the Skin's `minSize` and `maximise`. Slots: `wallpaper`, `icons`, `startMenu`, `tray`, `start`, overlays as children. Reports every change through `onAction` | Glass snap preview (clear); otherwise its parts' |
| `WindowManager` | Not drawn: `windowReducer`, `windowActions`, `emptyDesktop` and the serialisable `WindowManagerState` (windows by id with bounds, `zIndex`, `minimized`, `maximized`, `restoreBounds`, opaque `props`; `order`, `focusedId`, `zCounter`, `startMenuOpen`). New windows cascade 24px from the last one, inside the desktop. `launchProgram(program)` makes the open action | none |
| `DesktopIcon` | 48px icon plus label on the desktop, selectable | Luna |
| `LogonScreen` / `LogonTile` / `LogonAction` | XP's Welcome screen: a display `title` and `hint` left of a faint divider, a column of tiles right (framed `picture`, `name`, `subtitle`; `selected` shows its children, `footer` always shows; `dimmed`), `footerStart` (a `LogonAction` like "Turn off") and `footerEnd`. Stacks below 640px | Luna |

`morph()` is the kit's View Transition; `Desktop` uses it for minimise and restore, so apps never call it for windows.

## Skins

A Skin replaces Luna chrome for one program's windows (`Window skin`, a program's `skin`). Each is a folder in
`frontend/ui/skins/<name>/` with a manifest, CSS and its stories (`<Name>.stories.tsx`, titled `Skins/<Name>`);
`skins` and the `SkinName` type are exported. The semantic tokens are re-derived on every skinned window, so a Skin
sets primitives (`--ui-p-*`) where the derivation gives its look and overrides semantic tokens only where it differs.

| Skin | What | Schemes |
| --- | --- | --- |
| `msn` | Messenger 6/7: a soft blue-to-white frame with rounded corners, a tall glossy blue title bar with a 32px icon, a gloss and a swoosh, round pale-blue caption buttons (Close stays red), pale blue bands and status bar, white lists, rounded picture frames, a big glossy Send (`CommandButton size="large"`). Used by Messenger's contact list and conversations | One look in the light schemes; a dark navy variant in Noir |

## Controls (things you operate or read as atoms, all XP face)

| Part | What |
| --- | --- |
| `CommandButton` | The plastic button. `tone`, `isDefault` (the dark-ringed default button), `size`, `pressed` |
| `Toolbar` / `ToolbarButton` / `ToolbarSeparator` | Flat, hover-raised buttons with an icon, optional label. `variant="bare"` drops the face and padding for a Toolbar inside another surface |
| `TextBox` / `TextArea` | Sunken fields; `TextArea` for reading-size input, `mono` for prompts, code and previews |
| `ComboBox` | Select with the blue drop arrow |
| `CheckBox` / `RadioButton` / `RadioGroup` | Luna check and radio, with a label |
| `Slider` | Trackbar |
| `Tabs` | Property-sheet tabs with the orange hot edge |
| `Hyperlink` | An inline link that's a link or a button, underlined in `--ui-link` |
| `Text` | THE text part: `size` (`chrome`, `reading`, `title`, `display`; `reading` keeps someone's line breaks and wraps long words), `tone` (a tone in its `-ink` colour, or `muted` for help text), `strong`, `italic`, `mono`, `as`. Help under a control is `<Text as="p" tone="muted">`; someone's words are `<Text size="reading">`; a mention is `<Text as="strong" tone="neutral" strong>` |
| `StatusText` | A status or error with its tone's icon (info, check, warning, error). `variant="bar"` is a toned bar across a pane (IE's information bar) with optional `actions`; `icon={false}` drops the icon. Kept apart from `Text` because the icon is the point |
| `ProgressBar` | Green segmented bar, plus a marquee variant. `tone`: no tone and `go` are XP's green, `neutral` blue for a level that isn't a task, `caution` and `danger` for busy and full |
| `Meter` | A small inline level gauge (`role="meter"`, 0 to 1), toned or `diverging` from the middle (−1 to 1). A level, never a task's progress |
| `Swatch` | A colour chip for a colour that is data, square or dot, with an optional ring |
| `SwatchPicker` | A radio grid of colour swatches, with an optional "none" |
| `Presence` | Online, away, busy or offline, as a dot or a Messenger buddy |
| `UserPicture` | A framed user picture at a fixed size, with an optional presence badge |
| `PictureStack` | A few user pictures overlapping, the first on top, with a `+n` for the rest (`max`, `size` 16, 24 or 32, a required `label` naming everyone). A group conversation's picture |
| `FieldRow` | A label next to a control, aligned, with hint and error. `labelWidth`: `auto`, `compact`, or a CSS length |
| `Icon` | `<Icon name size>`, real or pixel tier |

## Views (data displays)

| Part | What |
| --- | --- |
| `ListView` | `view="details|tiles|icons|list"`, with selection, columns and sorting. Per item `tone`, per column `wrap`, `activateOn="double|single"`, `tileSize="regular|compact"` |
| `TreeView` | Expandable tree with plus and minus boxes |
| `ItemPicker` | A grid of pictured items toggled on and off, with a detail, a badge and a tag |
| `ControlGrid` | A grid whose cells hold controls, with row headers and muted or busy rows |
| `PropertyList` | Label and value pairs, as on a Properties sheet, in columns or stacked |
| `ShareBar` | A stacked bar of shares with labels and captions, each share in a data colour |
| `CategoryView` / `CategoryItem` | Control Panel's "Pick a category": big icon links with descriptions |
| `ChatLog` | The scrolling log a conversation is read in: a reading-size `SunkenField` with `role="log"` and a keyboard stop |
| `ChatMessage` | One message: sender, picture, time, actions, details, error. `say` or `emote`, `row` or `transcript` layout, own, toned, live and continued states, `colour` and `manner` |
| `Post` | A Board post: picture, title, meta, context, actions and replies, as a card or flat, regular or compact, unread and current states |
| `Console` / `ConsoleLine` | Monospace output, a line at a time, with a prompt and a tone per line, always in Noir |
| `ContactRow` | A person as a button row: picture, name, meta, details; selected, dimmed, unread and disabled. Toggle rows use `aria-pressed` |
| `BuddyList` | Messenger's contact list: `ContactRow`s as options in collapsible groups (an `Expander` and a listbox each, `empty` text when a group has nobody), one selection across all of them (`selectionMode` `single` or `multiple`: Ctrl and Shift click, the arrows, Space, Ctrl+A), `onActivate` on double-click or Enter, `onItemContextMenu` on right-click, the Menu key or Shift+F10 (it selects the row first), `collapsed` groups |

## Surfaces (containers)

| Part | What |
| --- | --- |
| `GroupBox` | Rounded frame with a blue title. `current` marks the one that is yours or active. `fill` grows it to fill a flex parent, and its scrolling part (a `ListView`, `ChatLog`, `SunkenField`, `Pane`, `TreeView`, `Console`, `Tabs` or `ItemPicker`) takes the rest of its height and scrolls; other children keep their height |
| `TaskPane` / `TaskPaneSection` / `TaskPaneLink` | The blue sidebar with collapsible sections, links and details. `special` for the dark-headed first section; a title wraps in narrow panes; `aside` puts a count in its heading |
| `SunkenField` | XP's white sunken field for content that isn't a ListView: a page to read, a list of `ContactRow`s, a log. `text="chrome|reading"` |
| `StatusBar` / `StatusBarField` | Window footer fields. Put it at the bottom of the whole window, so a `grow` field reaches the right edge |
| `PageHeader` | A page's title band across the top of an app: icon, title, description and an aside. Below 520px of window width the description is dropped |
| `Expander` | A collapsible group inside content, with a title and a count, like Messenger's contact groups |
| `WindowLayout` | A window body's anatomy as slots: menu bar, toolbar bands, address bar, task pane (a `nav`; `taskPaneWidth` `regular`, `wide` or a CSS length), content (the one scroll container, `scroll="content|both|none"`), details pane (`detailsPlacement` `end` or `bottom`, `detailsWidth` `regular`, `wide` or a CSS length), footer, status bar. Below `narrowBelow` (520, 640, 820 or 1000px of window width; 520 by default) the task pane stacks above the content or hides (`taskPaneNarrow="hide"`); below `detailsNarrowBelow` (the same by default) the details pane goes under the content or hides (`detailsNarrow="hide"`). CSS container queries, so an app never measures its window |
| `Pane` | A region that scrolls inside flex and grid parents: grows, shrinks to zero, `surface="plain|sunken"`, `pad`, `text`, `scroll="y|both"` |
| `Stack` / `Stack.Item` | A flex row or column on the space scale (`gap`, `rowGap`, `pad`: `xxs` to `xl`), with `align`, `justify`, `wrap`, `grow`, `inline` and `as`. `as` is typed: `as="form"` takes `noValidate` and `onSubmit`, `as="label"` `htmlFor`, `as="dl"` holds `dt`/`dd` (`Stack.Item as="dt"`). `Stack.Item` `grow` takes the rest, `truncate` ends in an ellipsis |
| `Columns` / `Columns.Column` | Fluid columns side by side inside content: each `Column` has a `weight` (its share of the width), an optional `minWidth` and its own `gap`; `Columns` has `gap`, `pad` and `grow`. Below `narrowBelow` (360, 520, 640 or 820px of its own width) they `stack` (sharing the height by weight) or show `one`: the column at index `show`, with the app's `switcher` (a `Toolbar`) above it, shown only then. A column is a flex column, so a `Pane` or a `GroupBox fill` in it fills it |
| `Tiles` | A grid of arbitrary children that fills its width with as many columns as fit, each at least `minWidth` (`--ui-tile-width` by default) and sharing the rest; `gap` on the space scale. For cards of mixed content; a list of items is a `ListView` |

## Removed and merged

- `InfoBar` is `<StatusText variant="bar">` (give the `tone`; its icon comes with the tone).
- `PostMention` is `<Text as="strong" tone="neutral" strong>`.
- `StartMenuSeparator` is gone (unused).
- `Hint` is `<Text as="p" tone="muted">`; `ReadingText` is `<Text size="reading">`.
- `LogView` / `LogEntry` / `LogNote` are `ChatLog` with `ChatMessage layout="transcript"`, and a note is
  `<Text as="p" tone="muted" italic>` in the log.
- `ReadingPane` and `ContactList` are `SunkenField` (`text="reading"` for a page); a list of people you pick from is a `BuddyList`.
- `RichText` went with D'Accord, its only user: someone's words are `<Text size="reading">`.
- Kept apart on purpose: `Meter` (a level, `role="meter"`) and `ProgressBar` (a task); `HudPanel` (docked to a canvas,
  no tail) and `Balloon` (points at an anchor); `Expander` (inside content) and `TaskPaneSection` (a blue pane box);
  `ContactRow` (toggle buttons) and `ListView` (a listbox); `BuddyList` (contact rows in groups, for Messenger) and
  `ListView` (items in views and columns).

## Not parts

Layout belongs to apps, through `WindowLayout`, `Pane`, `Stack`, `Columns` and `Tiles`; Tailwind layout utilities
only for one-offs. A fixed side pane is `WindowLayout`'s `taskPane` or `detailsPane`, never `Columns`. 3D scenes,
Figures and canvases are domain, not kit. Glass is not a part.
