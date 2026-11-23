# Parts

The closed set of kit parts. Each lives in `frontend/ui/src/parts/<group>/<Part>/` with its CSS and stories, and is
exported from `@partytown/ui`. Apps import only from the root, so a part's group is a kit detail. The status column is
kept up to date as parts land: `planned`, `built`.

## Chrome (the desktop and floating layer)

| Part | What | Material | Status |
| --- | --- | --- | --- |
| `Window` | An app window: frame, title bar, caption buttons, optional menu bar, status bar, body. Takes `skin` | Glass frame (clear), Luna title bar, XP face body | built |
| `Dialog` | A modal Window with a button row (OK/Cancel), always centred over its owner | Same as Window | built |
| `Taskbar` | The bottom bar: Start button, window buttons, tray | Glass (clear) over Luna blue | built |
| `StartButton` | The green pill | Luna | built |
| `StartMenu` | Two-column menu with user header and Log Off / Shut Down footer | Glass (regular) | built |
| `Tray` | The notification area: icons, clock, counts | Glass (clear) | built |
| `ContextMenu` | Right-click or action menu, with nested submenus | Glass (regular) | built |
| `MenuBar` | File / Edit / View … inside a Window | XP face | built |
| `Balloon` | A pointer balloon tied to an anchor (tray, field), with a title and close | Glass (regular) | built |
| `Tooltip` | Small hover hint | Glass (regular) | built |
| `DesktopIcon` | 48px icon plus label on the desktop, selectable | Luna | built |
| `LogonScreen` / `LogonTile` / `LogonAction` | XP's Welcome screen, full screen: dark bands with a light and an orange rule, a display `title` and `hint` left of a faint divider, a column of tiles right (framed `picture`, `name`, `subtitle`; `selected` shows its children, `footer` always shows; `dimmed`), `footerStart` (a `LogonAction` like "Turn off") and `footerEnd` (a hint). Stacks below 640px | Luna | built |

## Controls (inside windows, all XP face)

| Part | What | Status |
| --- | --- | --- |
| `CommandButton` | The plastic button. `tone`, `default` (the dark-ringed default button), `size` | built |
| `ToolbarButton` / `Toolbar` | Flat, hover-raised buttons with an icon, optional label. `variant="bare"` drops the face and padding for a Toolbar inside another surface; inside a `HudPanel` it's bare by default | built |
| `TextBox` / `TextArea` | Sunken fields; `TextArea` for reading-size input, `mono` for prompts, code and previews | built |
| `ComboBox` | Select with the blue drop arrow | built |
| `CheckBox` / `RadioButton` | Luna check and radio, with a label | built |
| `Slider` | Trackbar | built |
| `GroupBox` | Rounded frame with a blue title. `current` marks the one that is yours or active (a stronger `--ui-select` edge, bold title, `aria-current`) | built |
| `Tabs` | Property-sheet tabs with the orange hot edge | built |
| `ListView` | `view="details|tiles|icons|list"`, with selection and columns. Per item `tone` (text in the `-ink` tone), per column `wrap`, `activateOn="double|single"` (single: click or Enter opens, for launcher lists), `tileSize="regular|compact"` (compact tiles take 32px icons, for narrow panes) | built |
| `TreeView` | Expandable tree with plus and minus boxes | built |
| `TaskPane` | The blue sidebar with collapsible sections, links and details. A section's title wraps in narrow panes; `aside` puts a count in its heading | built |
| `ProgressBar` | Green segmented bar, plus a marquee variant. `tone`: no tone and `go` are XP's green, `neutral` blue for a level that isn't a task, `caution` and `danger` for busy and full | built |
| `StatusBar` | Window footer fields. Put it at the bottom of the whole window, not inside a column, so a `grow` field reaches the right edge | built |
| `SunkenField` | XP's white sunken field (the ListView's well) for content that isn't a ListView: a page to read, a list of `ContactRow`s, a log. `text="chrome|reading"`; layout (a column with gaps) is the app's | built |
| `FieldRow` | A label next to a control, aligned. `labelWidth`: `auto` (the label's own width, for inline rows like "Speak as"), `compact`, or a CSS length | built |
| `Hyperlink` | An inline link that's a link or a button, underlined in `--ui-link` | built |
| `Text` | THE text part: `size` (`chrome`, `reading`, `title`, `display`; `reading` keeps someone's line breaks and wraps long words), `tone` (a tone in its `-ink` colour, or `muted` for help text), `strong`, `italic`, `mono`, `as`. Help under a control is `<Text as="p" tone="muted">`; someone's words are `<Text size="reading">` | built |
| `StatusText` | A status or error line with its tone's icon (info, check, warning, error). Kept apart from `Text` because the icon is the point | built |
| `Icon` | `<Icon name size>`, real or pixel tier | built |

## Conversation (chat, contacts, presence)

| Part | What | Material | Status |
| --- | --- | --- | --- |
| `UserPicture` | A framed user picture at a fixed size, with an optional presence badge | XP face | built |
| `Presence` | Online, away, busy or offline, as a dot or a Messenger buddy | XP face | built |
| `ChatLog` | The scrolling log a conversation is read in: a reading-size `SunkenField` with `role="log"` and a keyboard stop. Messenger, Rooms and Places talk | XP face, sunken field | built |
| `ChatMessage` | One message: sender, picture, time, actions, details, error. `say` or `emote`, `row` or `transcript` layout, own, toned, live and continued states. `colour` puts the sender's data colour in a dot; `manner` (`plain`, `quiet`, `loud`, `faint`) is how it was said (whispered italic, shouted bold, out of earshot muted) | XP face | built |
| `RichText` | Rendered formatted text (paragraphs, headings, lists, code) at reading size | XP face | built |
| `ContactRow` | A contact as a button row: picture, name, meta, details, selected, dimmed and unread states | XP face | built |
| `Expander` | A collapsible group with a title, a count and extras, like Messenger's contact groups | XP face | built |
| `InfoBar` | A toned information bar across the top of a pane, with an icon and actions | XP face | built |

## Panels (settings, properties, data at a glance)

| Part | What | Material | Status |
| --- | --- | --- | --- |
| `CategoryView` / `CategoryItem` | Control Panel's category view: "Pick a category" with big icon links and descriptions | XP face, content | built |
| `PropertyList` | Label and value pairs, as on a Properties sheet, in columns or stacked | XP face | built |
| `ControlGrid` | A grid whose cells hold controls (a matrix of Personas by models), with row headers and muted or busy rows | XP face, content | built |
| `ShareBar` | A stacked bar of shares with labels and captions, each share in a data colour | XP face | built |
| `Meter` | A small inline level gauge, toned or diverging from the middle | XP face, sunken field | built |
| `Swatch` | A colour chip for a colour that is data, square or dot, with an optional ring | XP face | built |

## Feed (posts, output, pages)

| Part | What | Material | Status |
| --- | --- | --- | --- |
| `Post` / `PostMention` | A Board post: picture, title, meta, context, actions and replies, as a card or flat, regular or compact, unread and current states; a mention inside it | XP face, content | built |
| `Console` / `ConsoleLine` | Monospace output, a line at a time, with a prompt and a tone per line | XP face, sunken field | built |
| `PageHeader` | A page's title band: icon, title, description and an aside | Luna (a neutral-tone band) | built |

## Collections (pickers, logs, canvas overlays)

| Part | What | Material | Status |
| --- | --- | --- | --- |
| `HudPanel` | A small panel floating over a canvas, anchored to an edge or corner, with an optional title and close | Glass (regular, canvas blur) | built |
| `SwatchPicker` | A radio grid of colour swatches, with an optional "none" | XP face, content | built |
| `ItemPicker` | A grid of pictured items to choose from, with a detail, a badge and a tag | XP face, content | built |

## Merged away

- `Hint` is `<Text as="p" tone="muted">`; `ReadingText` is `<Text size="reading">`.
- `LogView` / `LogEntry` / `LogNote` are `ChatLog` with `ChatMessage layout="transcript"` (`colour`, `manner`; a group
  out of earshot is a faint `kind="emote"`), and a note is `<Text as="p" tone="muted" italic>` in the log.
- `ReadingPane` and `ContactList` are `SunkenField` (`text="reading"` for a page).
- `Console` keeps its own field: it's a terminal, always in Noir, monospace, a tone per line.

## Not parts

Layout belongs to apps (Tailwind layout utilities). 3D scenes, Figures and canvases are domain, not kit. Glass is
not a part.
