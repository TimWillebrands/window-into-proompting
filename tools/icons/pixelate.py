#!/usr/bin/env python3
import argparse
import io
import json
import math
import pathlib
import re
import shutil
import subprocess
import sys
import tempfile
import urllib.request

from PIL import Image, ImageFilter

ROOT = pathlib.Path(__file__).resolve().parents[2]
TOOL = ROOT / "tools/icons"
TWEMOJI_DIR = TOOL / "twemoji"
GLYPH_DIR = TOOL / "glyphs"
ICO_DIR = ROOT / "frontend/public/img/icons"
UI = ROOT / "frontend/ui"
OUT = UI / "icons"
REGISTRY_JSON = OUT / "icons.json"
REGISTRY_TS = UI / "src/parts/icons/Icon/registry.gen.ts"
TWEMOJI_VERSION = "15.1.0"
TWEMOJI_URL = "https://cdn.jsdelivr.net/gh/jdecked/twemoji@{version}/assets/svg/{code}.svg"
SIZES = (16, 32, 48)

USAGE = """
Builds the icons of @partytown/ui: the closed set of names, their art and the typed registry.

Two tiers stand behind one name. Real art is an XP-style .ico from frontend/public/img/icons/;
the best frame per size is taken, and a size the art lacks is scaled down from a larger frame.
Pixel art is an emoji, rendered from its Twemoji SVG (or a glyph in tools/icons/glyphs/ for
symbols Twemoji lacks), shrunk, hardened, quantised to a Luna palette, outlined, lit from the
top left and given XP's soft drop shadow. Real wins; pixel fills any size real art can't reach.

The name set lives in this script (pixel() and real() below). To add an icon, add a line, then:

  python3 -I tools/icons/pixelate.py fetch    download the Twemoji SVGs the set needs
  python3 -I tools/icons/pixelate.py build    render every PNG and write the registry
  python3 -I tools/icons/pixelate.py sheet    write a contact sheet to look at (default /tmp)

fetch downloads into a fresh temporary directory, accepts only plain SVG, then keeps exactly the
needed files in tools/icons/twemoji/ (Twemoji is CC-BY 4.0, see frontend/ui/icons/LICENSE.txt).
build is offline and deterministic: a rerun writes the same bytes. It writes
  frontend/ui/icons/pixel/<name>-<size>.png, frontend/ui/icons/real/<name>-<size>.png,
  frontend/ui/icons/icons.json and frontend/ui/src/parts/icons/Icon/registry.gen.ts
and removes PNGs that no longer belong to a name. Run it with -I: it reads downloaded files.
"""

ICONS = {}


def pixel(name, emoji, glyph=None):
    ICONS[name] = {"name": name, "emoji": emoji, "glyph": glyph, "art": None}


def real(name, art, emoji):
    ICONS[name] = {"name": name, "emoji": emoji, "glyph": None, "art": art}


real("board", "board.ico", "📌")
real("calendar", "calendar.ico", "📅")
real("chat-rooms", "chat-rooms.ico", "💬")
real("control-panel", "control-panel.ico", "⚙️")
real("diary", "diary.ico", "📓")
real("jobs", "jobs.ico", "💼")
real("memory-graph", "memory-graph.ico", "🧠")
real("messenger", "messenger.ico", "🦋")
real("personas", "personas.ico", "👤")
real("places", "places.ico", "🏘️")
real("stance-floor", "stance-floor.ico", "🧭")
real("wardrobe", "wardrobe.ico", "👗")
real("world", "world.ico", "🌍")

pixel("add", "➕")
pixel("back", "⬅️")
pixel("bell", "🔔")
pixel("books", "📚")
pixel("calculator", "🧮")
pixel("camera", "📷")
pixel("cancel", "🚫")
pixel("check", "✔️")
pixel("clipboard", "📋")
pixel("clock", "🕒")
pixel("close", "✖️")
pixel("computer", "🖥️")
pixel("delete", "🗑️")
pixel("document", "📄")
pixel("down", "⬇️")
pixel("edit", "✏️")
pixel("error", "❌")
pixel("folder", "📁")
pixel("folder-open", "📂")
pixel("forward", "➡️")
pixel("help", "❓")
pixel("info", "ℹ️")
pixel("internet", "🌐")
pixel("key", "🔑")
pixel("link", "🔗")
pixel("locked", "🔒")
pixel("mail", "✉️")
pixel("notepad", "📝")
pixel("picture", "🖼️")
pixel("power", "⏻", "power.svg")
pixel("print", "🖨️")
pixel("refresh", "🔄")
pixel("run", "🏃")
pixel("save", "💾")
pixel("search", "🔍")
pixel("zoom-in", "🔍", "zoom-in.svg")
pixel("zoom-out", "🔍", "zoom-out.svg")
pixel("locate", "🎯", "locate.svg")
pixel("settings", "⚙️")
pixel("shield", "🛡️")
pixel("solitaire", "🃏")
pixel("start", "🪟", "start.svg")
pixel("tools", "🛠️")
pixel("undo", "↩️")
pixel("redo", "↪️")
pixel("unknown", "❔")
pixel("unlocked", "🔓")
pixel("up", "⬆️")
pixel("user", "👤")
pixel("users", "👥")
pixel("warning", "⚠️")

pixel("play", "▶️")
pixel("pause", "⏸️")
pixel("stop", "⏹️")
pixel("fast-forward", "⏩")
pixel("hourglass", "⏳")
pixel("star", "⭐")
pixel("heart", "❤️")
pixel("sun", "☀️")
pixel("moon", "🌙")
pixel("sparkle", "✨")
pixel("new", "🆕")
pixel("gift", "🎁")
pixel("money", "💲")
pixel("receipt", "🧾")
pixel("shopping", "🛍️")
pixel("cookie", "🍪")
pixel("pin", "📌")
pixel("location", "📍")
pixel("compass", "🧭")
pixel("megaphone", "📣")
pixel("whisper", "🤫")
pixel("muted", "🔇")
pixel("watching", "👀")
pixel("emoticon", "🙂")
pixel("music", "🎵")
pixel("microphone", "🎤")
pixel("headphones", "🎧")
pixel("film", "🎬")
pixel("games", "🎮")
pixel("education", "🎓")
pixel("science", "⚛️")
pixel("planet", "🪐")
pixel("phone", "📱")
pixel("laptop", "💻")
pixel("vr-headset", "🥽")
pixel("coin", "🪙")
pixel("palette", "🎨")
pixel("galaxy", "🌌")
pixel("theatre", "🎭")

pixel("auto", "🤖")
pixel("models", "🤖")
pixel("cdu", "🜂", "cdu.svg")
pixel("chat", "💬")
pixel("relations", "💞")
pixel("company", "🏦")
pixel("life", "🌱")
pixel("scales", "⚖️")
pixel("local-server", "🦙")
pixel("claude", "✳️")

pixel("home", "🏠")
pixel("house", "🏡")
pixel("door", "🚪")
pixel("ruin", "🏚️")
pixel("city", "🏙️")
pixel("office", "🏢")
pixel("skyline", "🌆")
pixel("tree", "🌳")
pixel("mountain", "🏔️")
pixel("fire", "🔥")
pixel("coffee", "☕")
pixel("reception", "🛎️")
pixel("disc", "💿")
pixel("cocktail", "🍸")
pixel("dining", "🍽️")
pixel("swim", "🏊")
pixel("joystick", "🕹️")
pixel("chair", "🪑")
pixel("sofa", "🛋️")

pixel("walk", "🚶")
pixel("standing", "🧍")
pixel("sleep", "💤")
pixel("hand", "✋")
pixel("lean", "🫱")
pixel("dance", "💃")
pixel("meditate", "🧘")
pixel("lie-down", "🛌")
pixel("think", "🤔")
pixel("wave", "👋")
pixel("hug", "🤗")
pixel("hearts", "💞")
pixel("handshake", "🤝")

pixel("shirt", "👕")
pixel("trousers", "👖")
pixel("shoes", "👟")
pixel("hat", "🎩")
pixel("tie", "👔")
pixel("scissors", "✂️")


def srgb_to_linear(c):
    c /= 255
    return c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4


def linear_to_srgb(c):
    c = 12.92 * c if c <= 0.0031308 else 1.055 * c ** (1 / 2.4) - 0.055
    return c * 255


def to_oklab(rgb):
    r, g, b = (srgb_to_linear(float(v)) for v in rgb)
    l = (0.4122214708 * r + 0.5363325363 * g + 0.0514459929 * b) ** (1 / 3)
    m = (0.2119034982 * r + 0.6806995451 * g + 0.1073969566 * b) ** (1 / 3)
    s = (0.0883024619 * r + 0.2817188376 * g + 0.6299787005 * b) ** (1 / 3)
    return (
        0.2104542553 * l + 0.7936177850 * m - 0.0040720468 * s,
        1.9779984951 * l - 2.4285922050 * m + 0.4505937099 * s,
        0.0259040371 * l + 0.7827717662 * m - 0.8086757660 * s,
    )


def from_oklab(lab):
    L, a, b = lab
    l = (L + 0.3963377774 * a + 0.2158037573 * b) ** 3
    m = (L - 0.1055613458 * a - 0.0638541728 * b) ** 3
    s = (L - 0.0894841775 * a - 1.2914855480 * b) ** 3
    return (
        linear_to_srgb(4.0767416621 * l - 3.3077115913 * m + 0.2309699292 * s),
        linear_to_srgb(-1.2684380046 * l + 2.6097574011 * m - 0.3413193965 * s),
        linear_to_srgb(-0.0041960863 * l - 0.7034186147 * m + 1.7076147010 * s),
    )


def hex_rgb(h):
    return tuple(int(h[i:i + 2], 16) for i in (1, 3, 5))


def in_gamut(rgb):
    return all(-0.5 <= v <= 255.5 for v in rgb)


def ramp(base, levels):
    L0, a0, b0 = to_oklab(hex_rgb(base))
    chroma = math.hypot(a0, b0)
    hue = math.atan2(b0, a0)
    out = []
    for L in levels:
        c = chroma * max(0.25, 1 - abs(L - L0) * 1.1)
        while True:
            rgb = from_oklab((L, c * math.cos(hue), c * math.sin(hue)))
            if in_gamut(rgb) or c < 0.002:
                break
            c *= 0.92
        out.append(tuple(max(0, min(255, round(v))) for v in rgb))
    return out


HUES = [
    "#245edb", "#6ca0f0", "#1e9c9c", "#3a9a3a", "#98c83a", "#f8cc3c", "#f4900c",
    "#d8302a", "#ee6f9a", "#8455c8", "#9a5a2c", "#d8a878", "#f4c49a", "#5a6a78",
]
LEVELS = [0.30, 0.42, 0.54, 0.66, 0.77, 0.87, 0.95]
GREYS = [(28, 28, 32), (64, 64, 70), (104, 104, 110), (144, 144, 150), (184, 184, 188),
         (214, 214, 216), (236, 236, 236), (255, 255, 255)]
PALETTE = sorted({c for h in HUES for c in ramp(h, LEVELS)} | set(GREYS))
PALETTE_LAB = [to_oklab(c) for c in PALETTE]
SNAP = {}


def snap(rgb):
    key = tuple(rgb[:3])
    if key not in SNAP:
        lab = to_oklab(key)
        best = min(range(len(PALETTE)), key=lambda i: (
            (PALETTE_LAB[i][0] - lab[0]) ** 2 * 1.4
            + (PALETTE_LAB[i][1] - lab[1]) ** 2
            + (PALETTE_LAB[i][2] - lab[2]) ** 2, i))
        SNAP[key] = PALETTE[best]
    return SNAP[key]


def shade(rgb, factor):
    L, a, b = to_oklab(rgb)
    L = L * factor if factor < 1 else L + (1 - L) * (factor - 1)
    k = 1.15 if factor < 1 else 0.8
    out = from_oklab((L, a * k, b * k))
    return snap(tuple(max(0, min(255, round(v))) for v in out))


LOOK = {
    16: {"content": 15, "offset": 1, "blur": 0, "shadow": 0.34, "outline": 0.62, "light": 0.0, "alpha": 72},
    32: {"content": 30, "offset": 2, "blur": 0.8, "shadow": 0.34, "outline": 0.66, "light": 1.3, "alpha": 110},
    48: {"content": 45, "offset": 2, "blur": 1.1, "shadow": 0.34, "outline": 0.7, "light": 1.25, "alpha": 110},
}

HALO = {"colour": (236, 241, 250), "alpha": {16: 150, 32: 130, 48: 120}, "dim": 0.115, "dark": 0.08}


def luminance(rgb):
    r, g, b = (srgb_to_linear(float(v)) for v in rgb[:3])
    return 0.2126 * r + 0.7152 * g + 0.0722 * b


def dim(big):
    small = fitted(big).resize((60, 60), Image.BOX)
    total = weight = 0.0
    for r, g, b, a in small.getdata():
        if a < 8:
            continue
        total += luminance((r, g, b)) * a
        weight += a
    return weight > 0 and total / weight < HALO["dim"]


def twemoji_code(emoji):
    points = [f"{ord(c):x}" for c in emoji]
    if "200d" not in points:
        points = [p for p in points if p != "fe0f"]
    return "-".join(points)


def svg_for(icon):
    if icon["glyph"]:
        return GLYPH_DIR / icon["glyph"]
    return TWEMOJI_DIR / f"{twemoji_code(icon['emoji'])}.svg"


def render_svg(path, px):
    done = subprocess.run(
        ["rsvg-convert", "--width", str(px), "--height", str(px), "--keep-aspect-ratio", "--format", "png", str(path)],
        capture_output=True, check=True)
    return Image.open(io.BytesIO(done.stdout)).convert("RGBA")


def fitted(big):
    box = big.getchannel("A").point(lambda v: 255 if v > 8 else 0).getbbox()
    if not box:
        return big
    side = max(box[2] - box[0], box[3] - box[1])
    square = Image.new("RGBA", (side, side), (0, 0, 0, 0))
    square.paste(big.crop(box), ((side - (box[2] - box[0])) // 2, (side - (box[3] - box[1])) // 2))
    return square


def pixelate(big, size, light, halo=False):
    look = LOOK[size]
    c = look["content"] - (1 if halo else 0)
    small = fitted(big).resize((c, c), Image.BOX)
    px = small.load()
    solid = [[px[x, y][3] >= look["alpha"] for y in range(c)] for x in range(c)]

    def on(x, y):
        return 0 <= x < c and 0 <= y < c and solid[x][y]

    def edge(x, y):
        return on(x, y) and not (on(x - 1, y) and on(x + 1, y) and on(x, y - 1) and on(x, y + 1))

    art = Image.new("RGBA", (c, c), (0, 0, 0, 0))
    out = art.load()
    for x in range(c):
        for y in range(c):
            if not solid[x][y]:
                continue
            r, g, b, a = px[x, y]
            colour = snap((r, g, b))
            if edge(x, y):
                dark = not on(x + 1, y) or not on(x, y + 1)
                thin = (not on(x - 1, y) and not on(x + 1, y)) or (not on(x, y - 1) and not on(x, y + 1))
                depth = look["outline"] if dark and not thin else look["outline"] + (1 - look["outline"]) * 0.45
                colour = shade(colour, depth)
            elif light and look["light"] and (
                    (edge(x, y - 1) and not on(x, y - 2)) or (edge(x - 1, y) and not on(x - 2, y))):
                colour = shade(colour, look["light"])
            out[x, y] = colour + (255,)
    if not halo:
        return shadowed(art, size)
    placed = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    placed.paste(art, (1, 1))
    return shadowed(placed, size, haloed(placed, size))


def haloed(art, size):
    c = art.size[0]
    px = art.load()
    ring = Image.new("RGBA", (c, c), (0, 0, 0, 0))
    out = ring.load()
    alpha = HALO["alpha"][size]
    for x in range(c):
        for y in range(c):
            if px[x, y][3]:
                continue
            near = [px[x + dx, y + dy] for dx in (-1, 0, 1) for dy in (-1, 0, 1)
                    if (dx or dy) and 0 <= x + dx < c and 0 <= y + dy < c and px[x + dx, y + dy][3]]
            if any(luminance(p) < HALO["dark"] for p in near):
                out[x, y] = HALO["colour"] + (alpha,)
    return ring


def shadowed(art, size, halo=None):
    look = LOOK[size]
    canvas = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    mask = Image.new("L", (size, size), 0)
    mask.paste(art.getchannel("A"), (look["offset"], look["offset"]))
    if look["blur"]:
        mask = mask.filter(ImageFilter.GaussianBlur(look["blur"]))
    mask = mask.point(lambda v: round(v * look["shadow"]))
    shadow = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    shadow.putalpha(mask)
    canvas.alpha_composite(shadow)
    if halo is not None:
        canvas.alpha_composite(halo, (0, 0))
    canvas.alpha_composite(art, (0, 0))
    return canvas


def ico_frames(path):
    with Image.open(path) as im:
        return {s[0]: im.ico.getimage(s).convert("RGBA") for s in im.info["sizes"] if s[0] == s[1]}


def real_art(frames, size):
    if size in frames:
        return frames[size], False
    larger = sorted(s for s in frames if s > size)
    if not larger:
        return None, False
    whole = [s for s in larger if s % size == 0]
    source = frames[whole[0] if whole else larger[0]]
    filt = Image.BOX if whole else Image.LANCZOS
    scaled = source.resize((size, size), filt)
    return scaled, True


def save(img, path):
    path.parent.mkdir(parents=True, exist_ok=True)
    buf = io.BytesIO()
    img.save(buf, format="PNG", optimize=True)
    if not path.exists() or path.read_bytes() != buf.getvalue():
        path.write_bytes(buf.getvalue())


def covered(icon):
    if not icon["art"]:
        return False
    frames = ico_frames(ICO_DIR / icon["art"])
    return all(any(f >= s for f in frames) for s in SIZES)


def needed_codes():
    return sorted({twemoji_code(i["emoji"]) for i in ICONS.values() if not i["glyph"] and not covered(i)})


FORBIDDEN = re.compile(rb"<\s*(script|foreignObject|image|iframe|use)\b|<!ENTITY|<!DOCTYPE|href\s*=\s*[\"'](?!#)|url\(\s*[\"']?(?!#)|@import|\bon[a-z]+\s*=", re.I)


def plain_svg(data):
    if len(data) > 200_000:
        return False
    head = data.lstrip()[:200]
    if not head.startswith(b"<svg"):
        return False
    return not FORBIDDEN.search(data)


def fetch(args):
    codes = needed_codes()
    missing = codes if args.all else [c for c in codes if not (TWEMOJI_DIR / f"{c}.svg").exists()]
    landing = pathlib.Path(tempfile.mkdtemp(prefix="twemoji-svg-"))
    try:
        for code in missing:
            url = TWEMOJI_URL.format(version=TWEMOJI_VERSION, code=code)
            request = urllib.request.Request(url, headers={"User-Agent": "partytown-icons"})
            with urllib.request.urlopen(request, timeout=30) as response:
                data = response.read(200_001)
            if not plain_svg(data):
                sys.exit(f"refused {url}: not a plain SVG")
            (landing / f"{code}.svg").write_bytes(data)
            print(f"fetched {code}")
        TWEMOJI_DIR.mkdir(parents=True, exist_ok=True)
        for code in missing:
            shutil.copyfile(landing / f"{code}.svg", TWEMOJI_DIR / f"{code}.svg")
    finally:
        shutil.rmtree(landing)
    for old in TWEMOJI_DIR.glob("*.svg"):
        if old.stem not in codes:
            old.unlink()
            print(f"dropped {old.name}")
    print(f"{len(codes)} Twemoji SVGs kept in {TWEMOJI_DIR.relative_to(ROOT)}")


def build(_args):
    entries = []
    written = set()
    for name in sorted(ICONS):
        icon = ICONS[name]
        sizes = {}
        scaled = []
        frames = ico_frames(ICO_DIR / icon["art"]) if icon["art"] else {}
        big = None
        for size in SIZES:
            art, was_scaled = real_art(frames, size) if frames else (None, False)
            if art is not None:
                tier = "real"
                if was_scaled:
                    scaled.append(size)
            else:
                tier = "pixel"
                svg = svg_for(icon)
                if not svg.exists():
                    sys.exit(f"{name}: {svg.relative_to(ROOT)} is missing, run fetch first")
                if big is None:
                    big = render_svg(svg, 360)
                    halo = dim(big)
                art = pixelate(big, size, True, halo)
            path = OUT / tier / f"{name}-{size}.png"
            save(art, path)
            written.add(path)
            sizes[str(size)] = tier
        tier = "real" if icon["art"] else "pixel"
        entry = {
            "name": name,
            "tier": tier,
            "source": f"frontend/public/img/icons/{icon['art']}" if icon["art"] else icon["emoji"],
            "sizes": sizes,
        }
        if icon["art"]:
            entry["emoji"] = icon["emoji"]
        if scaled:
            entry["scaled"] = scaled
        entries.append(entry)
    for tier in ("pixel", "real"):
        for png in (OUT / tier).glob("*.png"):
            if png not in written:
                png.unlink()
    REGISTRY_JSON.write_text(json.dumps({"sizes": list(SIZES), "icons": entries}, indent=4, ensure_ascii=False) + "\n")
    subprocess.run(["pnpm", "exec", "biome", "format", "--write", str(REGISTRY_JSON)], cwd=ROOT / "frontend", capture_output=True)
    REGISTRY_TS.write_text(registry_ts(entries))
    pixels = sum(1 for e in entries if e["tier"] == "pixel")
    print(f"{len(entries)} icons: {len(entries) - pixels} real, {pixels} pixel, {len(written)} PNGs")


def ident(name, size):
    return "i_" + name.replace("-", "_") + f"_{size}"


def registry_ts(entries):
    lines = []
    for e in entries:
        for size, tier in e["sizes"].items():
            lines.append(f"import {ident(e['name'], size)} from '../../../../icons/{tier}/{e['name']}-{size}.png?no-inline';")
    lines.append("")
    lines.append("export const iconSizes = [16, 32, 48] as const;")
    lines.append("export type IconSize = (typeof iconSizes)[number];")
    lines.append("export type IconTier = 'real' | 'pixel';")
    lines.append("export type IconArt = { src: string; tier: IconTier; scaled: boolean };")
    lines.append("")
    lines.append("export const iconNames = [")
    for e in entries:
        lines.append(f"    '{e['name']}',")
    lines.append("] as const;")
    lines.append("export type IconName = (typeof iconNames)[number];")
    lines.append("")
    lines.append("export type IconEntry = {")
    lines.append("    tier: IconTier;")
    lines.append("    source: string;")
    lines.append("    sizes: Record<IconSize, IconArt>;")
    lines.append("};")
    lines.append("")
    lines.append("export const icons: Record<IconName, IconEntry> = {")
    for e in entries:
        lines.append(f"    '{e['name']}': {{")
        lines.append(f"        tier: '{e['tier']}',")
        lines.append(f"        source: {json.dumps(e['source'], ensure_ascii=False)},")
        lines.append("        sizes: {")
        for size, tier in e["sizes"].items():
            scaled = "true" if int(size) in e.get("scaled", []) else "false"
            lines.append(f"            {size}: {{ src: {ident(e['name'], size)}, tier: '{tier}', scaled: {scaled} }},")
        lines.append("        },")
        lines.append("    },")
    lines.append("};")
    return "\n".join(lines) + "\n"


def sheet(args):
    registry = json.loads(REGISTRY_JSON.read_text())
    names = [e["name"] for e in registry["icons"] if not args.only or e["name"] in args.only]
    zoom = args.zoom
    backs = [(43, 45, 52, 255), (32, 34, 39, 255)] if args.noir else [(236, 233, 216, 255), (255, 255, 255, 255)]
    cell = (16 + 32 + 48 + 24) * zoom
    cols = args.columns
    rows = math.ceil(len(names) / cols)
    page = Image.new("RGBA", (cols * cell * len(backs) // len(backs), rows * 56 * zoom), backs[0])
    for i, name in enumerate(names):
        e = next(x for x in registry["icons"] if x["name"] == name)
        x0 = (i % cols) * cell
        y0 = (i // cols) * 56 * zoom
        back = backs[(i // cols) % 2 if args.mixed else 0]
        page.paste(Image.new("RGBA", (cell, 56 * zoom), back), (x0, y0))
        x = x0 + 4 * zoom
        for size in SIZES:
            tier = e["sizes"][str(size)]
            img = Image.open(OUT / tier / f"{name}-{size}.png").convert("RGBA")
            img = img.resize((size * zoom, size * zoom), Image.NEAREST)
            page.alpha_composite(img, (x, y0 + 4 * zoom))
            x += (size + 8) * zoom
    page.save(args.out)
    print(f"wrote {args.out}")


def main():
    parser = argparse.ArgumentParser(prog="pixelate.py", description=USAGE, formatter_class=argparse.RawDescriptionHelpFormatter)
    commands = parser.add_subparsers(dest="command", required=True)
    f = commands.add_parser("fetch", help="download the Twemoji SVGs the name set needs")
    f.add_argument("--all", action="store_true", help="download every needed SVG again, not only missing ones")
    f.set_defaults(run=fetch)
    b = commands.add_parser("build", help="render every PNG and write icons.json and registry.gen.ts")
    b.set_defaults(run=build)
    s = commands.add_parser("sheet", help="write a contact sheet of the built icons")
    s.add_argument("--out", default="/tmp/partytown-icons.png", help="where to write the sheet (default /tmp/partytown-icons.png)")
    s.add_argument("--zoom", type=int, default=1, help="nearest-neighbour zoom factor (default 1)")
    s.add_argument("--columns", type=int, default=8, help="icons per row (default 8)")
    s.add_argument("--mixed", action="store_true", help="alternate the XP face with white rows")
    s.add_argument("--noir", action="store_true", help="on the Noir face (and Noir content rows with --mixed)")
    s.add_argument("--only", nargs="*", help="only these names")
    s.set_defaults(run=sheet)
    args = parser.parse_args()
    args.run(args)


if __name__ == "__main__":
    main()
