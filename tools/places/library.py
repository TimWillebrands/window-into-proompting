#!/usr/bin/env python3
"""Writes the Places model library (ADR 0049) to the backend and its frontend copy. Run after editing: python3 tools/places/library.py"""
import json
import math
import pathlib

ROOT = pathlib.Path(__file__).resolve().parents[2]
OUT = [ROOT / "backend/World/Places/library.json", ROOT / "frontend/src/apps/places/scene/library.json"]

PALETTE = {
    "wood": "#a0703f", "dark-wood": "#5b3a22", "light-wood": "#d6ae74", "birch": "#e8dcc6",
    "metal": "#a7b0ba", "chrome": "#d9dee5", "iron": "#2a2f2c", "brass": "#c9a24a", "gold": "#e6b422", "copper": "#b8693a",
    "stone": "#9a958c", "dark-stone": "#5f5a55", "concrete": "#b5b2ab", "brick": "#8a4a36", "terracotta": "#b5562f",
    "white": "#f3efe6", "cream": "#efe3c8", "paper": "#f7f1e1", "black": "#22242a", "charcoal": "#3a3d44", "grey": "#8b9097",
    "glass": "#9fc7e6", "water": "#3f8fa8", "ice": "#d6f1ff",
    "leaf": "#4f9a43", "dark-leaf": "#2f6b34", "moss": "#6b8e3a", "lime": "#a4c639", "dry-leaf": "#a58f3e",
    "red": "#c0392b", "crimson": "#8b1e2d", "orange": "#e07a2f", "amber": "#f2a541", "yellow": "#f2c94c",
    "green": "#3e8e5a", "teal": "#2a9d8f", "blue": "#3a6ea5", "navy": "#243b63", "sky": "#8fc4ef",
    "purple": "#7b4fa3", "lilac": "#b9a0e0", "pink": "#e86a92", "blush": "#f4b6c2", "magenta": "#c2267a",
    "sand": "#e3cf9a", "tan": "#c8a173", "rust": "#9a4a2a", "wine": "#6b1d2e",
    "fire": "#ff8a2a", "ember": "#ffd27a", "neon-pink": "#ff4fa3", "neon-blue": "#4fc3ff", "neon-green": "#7dff7a", "warm-light": "#ffe2a8",
}


def P(shape, val, at=(0, 0, 0), c="grey", **o):
    d = {shape: val, "at": [round(v, 3) for v in at], "c": c}
    d.update(o)
    return d


def box(w, h, d, x=0, y=0, z=0, c="wood", **o): return P("box", [w, h, d], (x, y, z), c, **o)
def cyl(r, h, x=0, y=0, z=0, c="wood", **o): return P("cyl", [r, h], (x, y, z), c, **o)
def taper(rt, rb, h, x=0, y=0, z=0, c="wood", **o): return P("cyl", [rt, rb, h], (x, y, z), c, **o)
def ball(r, x=0, y=0, z=0, c="leaf", **o): return P("ball", r, (x, y, z), c, **o)
def cone(r, h, x=0, y=0, z=0, c="leaf", **o): return P("cone", [r, h], (x, y, z), c, **o)
def use(name, x=0, y=0, z=0, ry=0, **o):
    d = {"use": name, "at": [x, y, z]}
    if ry: d["ry"] = round(ry, 4)
    d.update(o)
    return d


H = math.pi / 2
MODELS = []


def model(name, about, parts, size=(1, 1), **o):
    m = {"name": name, "size": list(size), "about": about}
    m.update(o)
    m["parts"] = parts
    MODELS.append(m)


def seat(x, z, h=0.46, face=0): return {"at": [x, z], "h": h, "face": face}


# ---- Sets: one prop is a whole place to be ----
model("dining-set", "round table with four chairs", size=(3, 3), group="sets",
      seats=[seat(1, 0, face=0), seat(1, 2, face=2), seat(0, 1, face=1), seat(2, 1, face=3)],
      parts=[cyl(0.24, 0.03, c="dark-wood"), cyl(0.05, 0.7, c="dark-wood"), cyl(0.62, 0.05, y=0.7, c="$top"),
             use("chair", 0, 0, -1), use("chair", 0, 0, 1, math.pi), use("chair", -1, 0, 0, H), use("chair", 1, 0, 0, -H)],
      tint={"top": "light-wood"})
model("cafe-set", "small table with two chairs facing each other", size=(3, 1), group="sets",
      seats=[seat(0, 0, face=1), seat(2, 0, face=3)],
      parts=[use("table"), use("chair", -1, 0, 0, H), use("chair", 1, 0, 0, -H),
             cyl(0.05, 0.09, 0.12, 0.76, 0.05, "white"), cyl(0.05, 0.09, -0.1, 0.76, -0.08, "white")])
model("lounge-set", "sofa, two armchairs and a coffee table on a rug", size=(3, 2), group="sets",
      seats=[seat(0.5, 0, 0.43), seat(1.5, 0, 0.43), seat(0, 1, 0.43, 1), seat(2, 1, 0.43, 3)],
      parts=[box(2.7, 0.012, 1.7, 0, 0.001, 0.1, "$rug", flat=True), box(2.3, 0.014, 1.3, 0, 0.001, 0.1, "cream", flat=True),
             use("sofa", -0.5, 0, -0.5), use("armchair", -1, 0, 0.5, H), use("armchair", 1, 0, 0.5, -H), use("coffee-table", 0, 0, 0.5)],
      tint={"rug": "crimson"})
model("bar-set", "bar counter with three stools in front", size=(3, 2), group="sets",
      seats=[seat(0, 1, 0.7, 2), seat(1, 1, 0.7, 2), seat(2, 1, 0.7, 2)],
      parts=[use("counter", 0, 0, -0.5), use("stool", -1, 0, 0.5), use("stool", 0, 0, 0.5), use("stool", 1, 0, 0.5),
             cyl(0.04, 0.16, 0.9, 1.04, -0.6, "brass"), cyl(0.04, 0.16, 0.7, 1.04, -0.6, "brass"),
             cyl(0.05, 0.12, -0.6, 1.04, -0.45, "glass", opacity=0.6)])
model("desk-set", "desk with a chair, lamp and laptop", size=(2, 2), group="sets", seats=[seat(0.5, 1, 0.46, 2)],
      parts=[use("desk", 0, 0, -0.5), use("chair", -0.5, 0, 0.5, math.pi), use("laptop", -0.4, 0.79, -0.5),
             cyl(0.08, 0.02, 0.7, 0.79, -0.7, "charcoal"), P("tube", [[0.7, 0.8, -0.7], [0.7, 1.1, -0.7], [0.55, 1.2, -0.6]], c="charcoal", r=0.015),
             P("cone", [0.09, 0.1], (0.5, 1.12, -0.56), "amber", glow=True)],
      light={"at": [0.5, 1.1, -0.5], "colour": "#ffd9a0", "power": 1.2})
model("picnic-table", "picnic table with two benches", size=(2, 2), group="sets",
      seats=[seat(0, 0, 0.42, 0), seat(1, 0, 0.42, 0), seat(0, 1, 0.42, 2), seat(1, 1, 0.42, 2)],
      parts=[box(1.8, 0.06, 0.7, 0, 0.68, 0, "wood"), box(1.8, 0.05, 0.26, 0, 0.4, -0.62, "wood"), box(1.8, 0.05, 0.26, 0, 0.4, 0.62, "wood"),
             box(0.08, 0.7, 1.5, 0.7, 0, 0, "dark-wood", mirror="x"), box(0.1, 0.06, 1.5, 0.7, 0.36, 0, "dark-wood", mirror="x")])

# ---- Outdoors and nature ----
model("campfire", "campfire in a ring of stones, lit", group="outdoors",
      parts=[ball(0.09, 0.3, 0.05, 0, "stone", spin=7, squash=0.7),
             box(0.5, 0.08, 0.1, 0, 0.03, 0, "dark-wood", ry=0.5), box(0.5, 0.08, 0.1, 0, 0.09, 0, "dark-wood", ry=-0.7),
             cone(0.16, 0.42, 0, 0.1, 0, "fire", glow=True, tag="flame", sides=6),
             cone(0.09, 0.28, 0.03, 0.12, 0.02, "ember", glow=True, tag="flame", sides=5)],
      light={"at": [0, 0.5, 0], "colour": "#ff9a4a", "power": 3},
      do="Sit by the fire and watch it a while", gives="one small thing the fire does or brings up: a log collapsing, a smell from years ago, a shape in the flames")
model("log-seat", "a log to sit on", group="outdoors", seats=[seat(0, 0, 0.36)],
      parts=[P("cyl", [0.2, 0.8], (0, 0.2, 0), "dark-wood", rz=H, sides=8), cyl(0.2, 0.002, 0, 0, 0, "dark-wood", flat=True),
             P("cyl", [0.15, 0.82], (0, 0.2, 0), "light-wood", rz=H, sides=8)])
model("tent", "small ridge tent, open at the front", size=(2, 2), group="outdoors",
      parts=[P("gable", [1.5, 1.1, 1.7], (0, 0, 0), "$canvas", ry=H), P("gable", [0.5, 0.75, 0.06], (0, 0, 0.84), "charcoal", ry=0),
             cyl(0.02, 1.15, 0, 0, 0.86, "light-wood"), box(1.2, 0.02, 1.4, 0, 0.005, 0, "tan", flat=True)],
      tint={"canvas": "moss"})
model("pine", "pine tree", group="outdoors",
      parts=[cyl(0.07, 0.5, c="dark-wood"), cone(0.42, 0.7, 0, 0.4, 0, "dark-leaf", sides=7), cone(0.33, 0.6, 0, 0.85, 0, "dark-leaf", sides=7),
             cone(0.22, 0.5, 0, 1.3, 0, "leaf", sides=7)])
model("birch", "slim birch tree", group="outdoors",
      parts=[cyl(0.05, 1.5, c="birch"), box(0.11, 0.03, 0.02, 0, 0.5, 0.05, "charcoal", array={"n": 3, "step": [0, 0.35, 0]}),
             ball(0.34, 0, 1.75, 0, "lime", squash=1.2, tag="sway"), ball(0.24, 0.2, 1.5, 0.1, "leaf"), ball(0.22, -0.2, 1.55, -0.1, "lime")])
model("cherry-tree", "cherry tree in blossom", size=(2, 2), group="outdoors",
      parts=[taper(0.09, 0.14, 1.0, c="dark-wood"), P("cyl", [0.05, 0.6], (0.2, 0.9, 0), "dark-wood", rz=-0.8), P("cyl", [0.05, 0.6], (-0.2, 0.9, 0.1), "dark-wood", rz=0.8),
             ball(0.6, 0, 1.6, 0, "blush", squash=0.75), ball(0.42, 0.5, 1.35, 0.2, "pink", squash=0.8), ball(0.42, -0.5, 1.4, -0.15, "blush", squash=0.8),
             ball(0.35, 0, 1.45, 0.5, "pink", squash=0.8)])
model("palm", "palm tree", group="outdoors",
      parts=[P("tube", [[0, 0, 0], [0.08, 0.7, 0], [0.02, 1.4, 0], [-0.08, 1.9, 0]], c="tan", r=0.07, sides=6),
             P("extrude", [[0, 0], [0.8, 0.12], [0.95, -0.25], [0.5, -0.05]], (0, 1.9, 0), "leaf", depth=0.16, spin=6, rz=0.1),
             ball(0.07, 0.06, 1.82, 0.05, "dark-wood")])
model("bush", "round bush", group="outdoors",
      parts=[ball(0.36, 0, 0.3, 0, "dark-leaf", squash=0.85), ball(0.26, 0.2, 0.36, 0.12, "leaf"), ball(0.22, -0.22, 0.3, -0.08, "moss")])
model("rock", "boulder you can sit on", group="outdoors", seats=[seat(0, 0, 0.42)],
      parts=[ball(0.4, 0, 0.2, 0, "stone", squash=0.62, detail=0), ball(0.22, 0.26, 0.12, 0.2, "dark-stone", squash=0.6, detail=0)])
model("mushrooms", "a cluster of toadstools", group="outdoors", solid=False,
      parts=[cyl(0.03, 0.14, 0, 0, 0, "cream"), P("dome", 0.11, (0, 0.13, 0), "red", squash=0.7),
             cyl(0.02, 0.09, 0.2, 0, 0.1, "cream"), P("dome", 0.07, (0.2, 0.08, 0.1), "red", squash=0.7),
             cyl(0.02, 0.07, -0.15, 0, 0.18, "cream"), P("dome", 0.06, (-0.15, 0.06, 0.18), "orange", squash=0.7)])
model("cactus", "tall cactus", group="outdoors",
      parts=[P("capsule", [0.13, 1.1], c="moss"), P("tube", [[0.1, 0.5, 0], [0.34, 0.55, 0], [0.34, 0.85, 0]], c="moss", r=0.07, sides=6),
             P("tube", [[-0.1, 0.35, 0], [-0.3, 0.4, 0], [-0.3, 0.62, 0]], c="moss", r=0.06, sides=6), ball(0.05, 0, 1.12, 0, "pink")])
model("flower-bed", "bed of mixed flowers", size=(2, 1), group="outdoors", solid=False,
      parts=[box(1.8, 0.1, 0.8, 0, 0, 0, "dark-wood"), box(1.7, 0.03, 0.7, 0, 0.09, 0, "rust"),
             {"group": [cyl(0.015, 0.22, c="dark-leaf"), ball(0.07, 0, 0.26, 0, "pink")], "at": [-0.7, 0.1, -0.15], "array": {"n": 5, "step": [0.35, 0, 0]}},
             {"group": [cyl(0.015, 0.18, c="dark-leaf"), ball(0.06, 0, 0.21, 0, "yellow")], "at": [-0.55, 0.1, 0.18], "array": {"n": 4, "step": [0.35, 0, 0]}}])
model("reeds", "reeds at the water's edge (on water)", group="outdoors", solid=False,
      parts=[{"group": [cyl(0.012, 0.8, c="moss"), P("capsule", [0.03, 0.16], (0, 0.72, 0), "dark-wood")], "at": [0.14, -0.3, 0], "spin": 5, "tag": "sway"},
             cyl(0.012, 0.9, 0, -0.3, 0, "leaf")])
model("rowboat", "rowing boat (on water)", size=(2, 1), group="outdoors", seats=[seat(0, 0, 0.1, 1), seat(1, 0, 0.1, 3)],
      parts=[P("extrude", [[-0.9, 0], [-0.7, -0.22], [0.7, -0.22], [0.95, 0], [0.7, 0.02], [-0.7, 0.02]], (0, -0.12, 0), "$hull", depth=0.62),
             box(1.3, 0.04, 0.5, 0, -0.1, 0, "light-wood"), box(0.12, 0.03, 0.56, -0.4, -0.02, 0, "wood"), box(0.12, 0.03, 0.56, 0.4, -0.02, 0, "wood"),
             P("cyl", [0.015, 1.1], (0, 0.02, 0.33), "light-wood", rz=H, ry=0.25)],
      tint={"hull": "teal"})
model("well", "old stone well with a little roof", group="outdoors",
      parts=[P("lathe", [[0.34, 0], [0.36, 0.5], [0.3, 0.52], [0.28, 0.1]], c="stone", sides=10), cyl(0.27, 0.02, 0, 0.12, 0, "water"),
             box(0.06, 1.1, 0.06, 0.32, 0.4, 0, "dark-wood", mirror="x"), P("gable", [0.5, 0.25, 0.9], (0, 1.5, 0), "rust", ry=H),
             P("cyl", [0.03, 0.6], (0, 1.3, 0), "dark-wood", rz=H), cyl(0.008, 0.5, 0, 0.8, 0, "tan")],
      do="Drop a coin down the well and make a wish", gives="the wish, in their own few words, and the sound the coin makes", leaves=True)
model("wishing-tree", "bare tree hung with ribbons and paper wishes", size=(2, 2), group="outdoors",
      parts=[taper(0.08, 0.15, 1.1, c="dark-wood"),
             P("tube", [[0, 1.0, 0], [0.4, 1.5, 0.1], [0.7, 1.7, 0.2]], c="dark-wood", r=0.04, spin=5),
             P("tube", [[0, 1.0, 0], [0.15, 1.7, 0.1], [0.2, 2.1, 0]], c="dark-wood", r=0.035, spin=3),
             box(0.05, 0.22, 0.01, 0.55, 1.35, 0.15, "red", spin=5, tag="sway"), box(0.05, 0.18, 0.01, 0.35, 1.25, -0.2, "paper", spin=4, tag="sway"),
             box(0.05, 0.2, 0.01, 0.2, 1.75, 0.1, "yellow", spin=3, tag="sway")],
      do="Tie a wish to the wishing tree", gives="the wish they write on the paper, in their own few words", leaves=True)
model("statue", "weathered statue on a plinth, with a plaque", group="outdoors",
      parts=[box(0.6, 0.5, 0.6, 0, 0, 0, "stone"), box(0.7, 0.06, 0.7, 0, 0.5, 0, "dark-stone"), box(0.3, 0.14, 0.02, 0, 0.2, 0.31, "brass"),
             P("capsule", [0.13, 0.7], (0, 0.56, 0), "moss"), ball(0.11, 0, 1.36, 0, "moss"), P("tube", [[0.1, 1.1, 0], [0.3, 1.25, 0.1], [0.34, 1.55, 0.1]], c="moss", r=0.045)],
      do="Read the plaque on the statue", gives="who or what the statue is of and one odd detail the plaque gives, plausible for this town")
model("little-library", "little free library box on a post", group="outdoors",
      parts=[box(0.08, 0.9, 0.08, 0, 0, 0, "dark-wood"), box(0.5, 0.45, 0.32, 0, 0.9, 0, "$paint"), P("gable", [0.4, 0.18, 0.6], (0, 1.35, 0), "charcoal", ry=H),
             box(0.4, 0.34, 0.02, 0, 0.95, 0.16, "glass", opacity=0.5), box(0.05, 0.26, 0.2, -0.14, 0.94, 0, "red", array={"n": 5, "step": [0.07, 0, 0]})],
      tint={"paint": "teal"},
      do="Swap a book at the little library", gives="the book they take (title, one detail about its state) and what they leave behind", leaves=True)
model("mailbox", "red pillar mailbox", group="outdoors",
      parts=[cyl(0.2, 1.0, c="red", sides=12), P("dome", 0.21, (0, 1.0, 0), "crimson", squash=0.5), box(0.2, 0.03, 0.02, 0, 0.8, 0.2, "black"), cyl(0.23, 0.08, c="black")],
      do="Post a card in the mailbox", gives="who the card is to and the one line on it", leaves=True)
model("ice-cream-cart", "ice cream cart with a striped parasol", size=(2, 1), group="outdoors",
      parts=[box(1.2, 0.6, 0.6, 0, 0.3, 0, "white"), box(1.2, 0.08, 0.6, 0, 0.9, 0, "blush"), P("ring", [0.2, 0.03], (-0.45, 0.2, 0.32), "charcoal", mirror="x"),
             cyl(0.02, 1.2, 0.3, 0.9, 0, "metal"), P("cone", [0.75, 0.3], (0.3, 2.0, 0), "pink", sides=8), P("cone", [0.76, 0.1], (0.3, 2.0, 0), "white", sides=8),
             cone(0.05, 0.14, -0.3, 1.12, 0, "tan", rx=math.pi), ball(0.07, -0.3, 1.16, 0, "blush"), P("sign", [0.6, 0.2], (0, 0.5, 0.31), "pink", text="IJS", fg="#ffffff")],
      do="Get an ice cream from the cart", gives="the flavour they pick, and what happens to it within the first minute")
model("market-stall", "market stall with a striped awning", size=(2, 1), group="outdoors",
      parts=[box(1.8, 0.8, 0.7, 0, 0, 0, "wood"), box(1.9, 0.05, 0.8, 0, 0.8, 0, "light-wood"), box(0.05, 2.0, 0.05, 0.9, 0, -0.35, "dark-wood", mirror="x"),
             P("wedge", [2.0, 0.3, 1.1], (0, 1.9, 0.1), "$awning"), box(0.25, 0.02, 1.12, -0.75, 2.06, 0.1, "white", array={"n": 4, "step": [0.5, 0, 0]}, rx=0.266),
             box(0.3, 0.14, 0.3, -0.6, 0.85, 0, "light-wood", array={"n": 4, "step": [0.4, 0, 0]}),
             ball(0.07, -0.6, 1.02, 0, "orange", array={"n": 2, "step": [0.8, 0, 0]}), ball(0.07, -0.2, 1.02, 0, "red", array={"n": 2, "step": [0.8, 0, 0]})],
      tint={"awning": "red"},
      do="See what's on the market stall today", gives="one thing on the stall that catches their eye, what it costs, and what the seller says about it")
model("string-lights", "string of bulbs between two poles", size=(3, 1), group="outdoors", solid=False,
      parts=[cyl(0.03, 2.3, 1.4, 0, 0, "dark-wood", mirror="x"), P("tube", [[-1.4, 2.25, 0], [-0.7, 2.0, 0], [0, 1.92, 0], [0.7, 2.0, 0], [1.4, 2.25, 0]], c="black", r=0.01),
             ball(0.05, -1.05, 2.07, 0, "warm-light", glow=True, tag="twinkle"), ball(0.05, -0.7, 1.95, 0, "amber", glow=True, tag="twinkle"),
             ball(0.05, -0.35, 1.89, 0, "warm-light", glow=True, tag="twinkle"), ball(0.05, 0, 1.87, 0, "amber", glow=True, tag="twinkle"),
             ball(0.05, 0.35, 1.89, 0, "warm-light", glow=True, tag="twinkle"), ball(0.05, 0.7, 1.95, 0, "amber", glow=True, tag="twinkle"),
             ball(0.05, 1.05, 2.07, 0, "warm-light", glow=True, tag="twinkle")],
      light={"at": [0, 1.8, 0], "colour": "#ffd9a0", "power": 2})
model("lantern", "standing lantern with a flame", group="outdoors",
      parts=[box(0.24, 0.04, 0.24, 0, 0, 0, "iron"), box(0.02, 0.36, 0.02, 0.1, 0.04, 0.1, "iron", mirror="xz"), P("pyramid", [0.3, 0.12, 0.3], (0, 0.4, 0), "iron"),
             box(0.18, 0.34, 0.18, 0, 0.05, 0, "warm-light", opacity=0.35), cone(0.04, 0.14, 0, 0.1, 0, "fire", glow=True, tag="flame", sides=5),
             P("ring", [0.05, 0.01], (0, 0.56, 0), "iron")],
      light={"at": [0, 0.4, 0], "colour": "#ffb060", "power": 1.6})
model("sandcastle", "sandcastle with a paper flag", group="outdoors", solid=False,
      parts=[box(0.6, 0.14, 0.6, 0, 0, 0, "sand"), taper(0.1, 0.13, 0.3, 0.2, 0.1, 0.2, "sand", mirror="xz"), taper(0.12, 0.16, 0.42, 0, 0.14, 0, "sand"),
             cyl(0.008, 0.22, 0, 0.56, 0, "dark-wood"), box(0.12, 0.07, 0.005, 0.06, 0.7, 0, "red")])
model("beach-towel", "beach towel to lie on", size=(1, 2), group="outdoors", solid=False, seats=[seat(0, 0.5, 0.03, 0)],
      parts=[box(0.8, 0.02, 1.8, 0, 0.001, 0, "$towel", flat=True), box(0.8, 0.022, 0.12, 0, 0.001, -0.6, "white", flat=True, array={"n": 4, "step": [0, 0, 0.4]})],
      tint={"towel": "teal"})
model("surfboard", "surfboard stuck upright in the sand", group="outdoors",
      parts=[P("extrude", [[0, 0], [0.2, 0.3], [0.22, 1.2], [0, 1.9], [-0.22, 1.2], [-0.2, 0.3]], (0, 0, 0), "$board", depth=0.06, rx=-0.12),
             box(0.04, 1.5, 0.065, 0, 0.2, -0.03, "white", rx=-0.12)],
      tint={"board": "yellow"})
model("hot-tub", "steaming wooden hot tub", size=(2, 2), group="outdoors",
      seats=[seat(0, 0, 0.25, 0), seat(1, 0, 0.25, 0), seat(0, 1, 0.25, 2), seat(1, 1, 0.25, 2)],
      parts=[P("lathe", [[0.9, 0], [0.92, 0.75], [0.8, 0.75], [0.78, 0.2]], c="wood", sides=14), cyl(0.79, 0.02, 0, 0.6, 0, "water", opacity=0.8),
             P("ring", [0.92, 0.025], (0, 0.3, 0), "iron", rx=H), P("ring", [0.92, 0.025], (0, 0.6, 0), "iron", rx=H),
             ball(0.14, 0.2, 0.8, 0, "white", tag="smoke", opacity=0.5), ball(0.12, -0.3, 0.8, 0.2, "white", tag="smoke", opacity=0.5)])
model("swing-bench", "porch swing for two", size=(2, 1), group="outdoors", seats=[seat(0, 0, 0.45), seat(1, 0, 0.45)],
      parts=[box(0.07, 1.9, 0.07, 0.95, 0, 0, "white", mirror="x"), box(2.0, 0.07, 0.07, 0, 1.9, 0, "white"),
             {"group": [box(1.5, 0.05, 0.5, 0, 0.42, 0.05, "$seat"), box(1.5, 0.5, 0.05, 0, 0.45, -0.2, "$seat"), cyl(0.008, 1.45, 0.7, 0.45, 0, "iron", mirror="x")], "tag": "sway"}],
      tint={"seat": "sky"})

# ---- Things to do ----
model("telescope", "brass telescope on a tripod", group="things to do",
      parts=[P("cyl", [0.012, 1.1], (0.22, 0, 0), "dark-wood", rz=0.2, spin=3), ball(0.06, 0, 1.05, 0, "brass"),
             P("cyl", [0.05, 0.07, 0.8], (0, 1.08, -0.1), "brass", rx=1.1), P("cyl", [0.075, 0.1], (0, 1.38, 0.42), "copper", rx=1.1),
             P("cyl", [0.03, 0.12], (0, 0.82, -0.52), "black", rx=1.1)],
      do="Look through the telescope", gives="one specific thing seen through it that fits the hour and the weather: a window across town, a named star, a gull on a mast")
model("record-crates", "crates of second-hand records", group="things to do",
      parts=[box(0.8, 0.5, 0.42, 0, 0, -0.2, "light-wood"), box(0.8, 0.3, 0.42, 0, 0, 0.24, "wood"),
             box(0.03, 0.34, 0.36, -0.33, 0.24, -0.2, "$sleeve", array={"n": 10, "step": [0.072, 0, 0]}, rz=0.08),
             box(0.03, 0.34, 0.36, -0.3, 0.06, 0.24, "navy", array={"n": 9, "step": [0.072, 0, 0]}, rz=-0.1)],
      tint={"sleeve": "orange"},
      do="Flip through the record crates", gives="the one record they pull out: artist, title, state of the sleeve, the price sticker")
model("library-shelves", "tall shelves packed with books", group="things to do",
      parts=[box(0.9, 2.0, 0.34, 0, 0, -0.26, "dark-wood"), box(0.82, 0.03, 0.3, 0, 0.5, -0.22, "wood", array={"n": 4, "step": [0, 0.45, 0]}),
             box(0.07, 0.34, 0.2, -0.34, 0.08, -0.16, "crimson", array={"n": 10, "step": [0.077, 0, 0]}),
             box(0.07, 0.3, 0.2, -0.34, 0.53, -0.16, "navy", array={"n": 10, "step": [0.077, 0, 0]}),
             box(0.07, 0.33, 0.2, -0.34, 0.98, -0.16, "green", array={"n": 10, "step": [0.077, 0, 0]}),
             box(0.07, 0.28, 0.2, -0.34, 1.43, -0.16, "amber", array={"n": 9, "step": [0.077, 0, 0]})],
      do="Pull a book off the shelves", gives="the book: its title, and the line or picture on the page it falls open at")
model("chess-table", "chess table with a game left mid-play", group="things to do",
      parts=[cyl(0.06, 0.66, c="iron"), cyl(0.22, 0.03, c="iron"), box(0.7, 0.05, 0.7, 0, 0.66, 0, "dark-wood"), box(0.56, 0.01, 0.56, 0, 0.71, 0, "cream"),
             box(0.14, 0.012, 0.14, -0.21, 0.71, -0.21, "charcoal", array={"n": 2, "step": [0.28, 0, 0.28]}), box(0.14, 0.012, 0.14, 0.07, 0.71, -0.21, "charcoal"),
             box(0.14, 0.012, 0.14, -0.21, 0.71, 0.07, "charcoal"),
             cone(0.03, 0.1, -0.14, 0.72, 0.14, "white"), cone(0.03, 0.12, 0.14, 0.72, -0.07, "black"), cyl(0.025, 0.08, 0.07, 0.72, 0.2, "white"), cyl(0.025, 0.09, -0.2, 0.72, -0.14, "black")],
      do="Study the game left on the chess table and make a move", gives="the position in a few words, the move they make, and what it now threatens", leaves=True)
model("guestbook", "guestbook open on a lectern", group="things to do",
      parts=[cyl(0.05, 0.9, c="dark-wood"), box(0.4, 0.04, 0.4, 0, 0, 0, "dark-wood"), box(0.56, 0.04, 0.42, 0, 0.95, 0, "wood", rx=0.35),
             box(0.24, 0.03, 0.34, -0.125, 0.99, 0.01, "paper", rx=0.35), box(0.24, 0.03, 0.34, 0.125, 0.99, 0.01, "paper", rx=0.35),
             P("cyl", [0.008, 0.2], (0.2, 1.02, 0.1), "navy", rz=1.2)],
      do="Write a line in the guestbook", gives="the line they write, in their own voice, signed", leaves=True)
model("fortune-machine", "coin-operated fortune teller in a glass case", group="things to do",
      parts=[box(0.7, 0.9, 0.6, 0, 0, 0, "wine"), box(0.62, 0.8, 0.52, 0, 0.9, 0, "glass", opacity=0.3), box(0.74, 0.14, 0.64, 0, 1.7, 0, "gold"),
             P("capsule", [0.14, 0.5], (0, 0.9, -0.08), "purple"), ball(0.11, 0, 1.45, -0.08, "tan"), P("dome", 0.13, (0, 1.5, -0.08), "gold", squash=0.8),
             ball(0.09, 0, 1.0, 0.14, "neon-blue", glow=True, tag="pulse"), box(0.2, 0.05, 0.02, 0, 0.5, 0.31, "brass"),
             P("sign", [0.6, 0.12], (0, 1.71, 0.33), "gold", text="FORTUNA", fg="#6b1d2e")],
      light={"at": [0, 1.1, 0.3], "colour": "#7fd0ff", "power": 1.2},
      do="Put a coin in the fortune machine", gives="the fortune printed on the card: one odd, specific sentence, not a platitude")
model("photo-booth", "photo booth with a curtain", group="things to do",
      parts=[box(0.95, 2.0, 0.9, 0, 0, -0.02, "$shell"), box(0.5, 1.5, 0.04, -0.18, 0.1, 0.44, "crimson", tag="sway"), box(0.24, 0.5, 0.03, 0.32, 1.0, 0.44, "white"),
             box(0.99, 0.2, 0.94, 0, 1.8, -0.02, "warm-light", glow="night"), P("sign", [0.8, 0.16], (0, 1.82, 0.46), "black", text="FOTO", fg="#ffe2a8")],
      tint={"shell": "navy"},
      do="Take a strip of photos in the booth", gives="what the four frames show, frame by frame, in a few words")
model("aquarium", "lit fish tank on a cabinet", size=(2, 1), group="things to do",
      parts=[box(1.6, 0.7, 0.5, 0, 0, 0, "dark-wood"), box(1.5, 0.6, 0.44, 0, 0.72, 0, "water", opacity=0.55), box(1.56, 0.05, 0.5, 0, 1.32, 0, "black"),
             box(1.4, 0.05, 0.36, 0, 0.72, 0, "sand"), cone(0.04, 0.3, -0.5, 0.76, -0.1, "leaf", tag="sway"), cone(0.03, 0.22, 0.4, 0.76, 0.1, "lime", tag="sway"),
             ball(0.05, -0.3, 1.0, 0, "orange", tag="bob", s=[1.6, 1, 0.6]), ball(0.04, 0.2, 1.1, 0.1, "yellow", tag="bob", s=[1.6, 1, 0.6]),
             ball(0.045, 0.5, 0.92, -0.05, "neon-blue", tag="bob", s=[1.6, 1, 0.6])],
      light={"at": [0, 1.2, 0.3], "colour": "#8fd8ff", "power": 1.4},
      do="Watch the fish for a while", gives="what one particular fish is up to, small and exact")
model("globe", "big floor globe that spins", group="things to do",
      parts=[cyl(0.2, 0.04, c="dark-wood"), cyl(0.03, 0.5, c="dark-wood"), P("ring", [0.33, 0.015], (0, 0.85, 0), "brass", rz=0.4),
             {"group": [ball(0.3, 0, 0, 0, "blue", detail=2), ball(0.12, 0.2, 0.12, 0.12, "leaf", squash=0.8), ball(0.1, -0.18, -0.05, 0.19, "moss"),
                        ball(0.13, -0.05, 0.1, -0.23, "leaf", squash=0.7), ball(0.08, 0.1, -0.2, -0.16, "sand")], "at": [0, 0.85, 0], "tag": "spin"}],
      do="Spin the globe and stop it with a finger", gives="the real place the finger lands on, and one thing they know or wonder about it")
model("pottery-wheel", "pottery wheel with a pot half thrown", group="things to do",
      parts=[box(0.6, 0.4, 0.6, 0, 0, 0, "charcoal"), {"group": [cyl(0.26, 0.04, c="metal", sides=14), P("lathe", [[0.1, 0], [0.16, 0.12], [0.12, 0.26], [0.14, 0.3]], (0, 0.04, 0), "terracotta")], "at": [0, 0.4, 0], "tag": "spin"},
             ball(0.07, 0.36, 0.05, 0.3, "terracotta", squash=0.6)],
      do="Throw a pot on the wheel", gives="the shape it takes under their hands and the moment it goes right or wrong")
model("workbench", "workbench with tools and a half-fixed thing", size=(2, 1), group="things to do",
      parts=[box(1.9, 0.08, 0.7, 0, 0.8, 0, "wood"), box(0.08, 0.8, 0.08, 0.85, 0, 0.25, "dark-wood", mirror="xz"), box(1.7, 0.04, 0.5, 0, 0.25, 0, "dark-wood"),
             box(1.9, 0.9, 0.04, 0, 0.88, -0.34, "tan"), box(0.04, 0.3, 0.02, -0.7, 1.3, -0.31, "metal", array={"n": 5, "step": [0.22, 0, 0]}),
             box(0.3, 0.2, 0.2, 0.5, 0.88, 0, "red"), box(0.22, 0.12, 0.3, -0.4, 0.88, 0.05, "metal"), cyl(0.02, 0.18, -0.4, 1.0, 0.05, "black")],
      do="Tinker at the workbench", gives="the small thing they get working, or the part that pings off under the bench")
model("shrine", "small shrine with candles", group="things to do",
      parts=[box(0.7, 0.5, 0.4, 0, 0, -0.2, "stone"), box(0.8, 0.05, 0.5, 0, 0.5, -0.2, "dark-stone"), P("gable", [0.5, 0.2, 0.44], (0, 1.05, -0.2), "rust", ry=H),
             box(0.04, 0.5, 0.04, 0.3, 0.55, -0.02, "dark-wood", mirror="x"),
             {"group": [cyl(0.03, 0.12, c="cream"), cone(0.015, 0.05, 0, 0.12, 0, "fire", glow=True, tag="flame", sides=4)], "at": [-0.2, 0.55, -0.12], "array": {"n": 3, "step": [0.2, 0, 0]}},
             ball(0.06, 0, 0.64, -0.32, "gold")],
      light={"at": [0, 0.8, 0], "colour": "#ffb060", "power": 1.2},
      do="Light a candle at the shrine", gives="who or what the candle is for, said plainly", leaves=True)
model("whiteboard", "whiteboard covered in scribbles (wall)", group="things to do", wall=True,
      parts=[box(0.92, 0.7, 0.03, 0, 1.0, -0.47, "metal"), box(0.86, 0.64, 0.035, 0, 1.03, -0.47, "white"),
             P("tube", [[-0.3, 1.5, -0.44], [-0.1, 1.42, -0.44], [0.05, 1.52, -0.44], [0.3, 1.45, -0.44]], c="blue", r=0.008),
             box(0.3, 0.012, 0.005, -0.2, 1.3, -0.45, "red"), box(0.4, 0.012, 0.005, -0.1, 1.2, -0.45, "black"), box(0.5, 0.04, 0.06, 0, 0.98, -0.44, "metal")],
      do="Add something to the whiteboard", gives="what's already on the board in a few words, and the thing they add", leaves=True)
model("punching-bag", "punching bag on a stand", group="things to do",
      parts=[box(0.7, 0.05, 0.7, 0, 0, 0, "iron"), box(0.06, 2.0, 0.06, 0, 0, -0.3, "iron"), box(0.06, 0.06, 0.4, 0, 1.95, -0.12, "iron"),
             {"group": [cyl(0.008, 0.25, 0, 0.85, 0, "metal"), P("capsule", [0.17, 0.9], (0, 0, 0), "crimson")], "at": [0, 0.82, 0.05], "tag": "sway"}],
      do="Go a round with the punching bag", gives="how the round goes and what, honestly, they were hitting")
model("stage-mic", "microphone on a stand", group="things to do", solid=False,
      parts=[cyl(0.16, 0.02, c="iron"), cyl(0.012, 1.3, c="metal"), P("capsule", [0.035, 0.14], (0, 1.3, 0.03), "charcoal", rx=0.5), ball(0.04, 0, 1.42, 0.09, "metal")],
      do="Step up to the mic", gives="what comes out when they try it: a line, a hum, a joke that half lands")
model("drum-kit", "drum kit", size=(2, 1), group="things to do",
      parts=[P("cyl", [0.3, 0.3], (0, 0.3, 0), "$shell", rx=H, sides=14), cyl(0.18, 0.16, -0.5, 0.5, 0.1, "$shell"), cyl(0.2, 0.3, 0.5, 0.15, 0.15, "$shell"),
             cyl(0.01, 0.9, -0.75, 0, -0.1, "metal"), cyl(0.2, 0.01, -0.75, 0.9, -0.1, "brass"), cyl(0.01, 1.1, 0.75, 0, -0.15, "metal"), P("cyl", [0.24, 0.01], (0.75, 1.1, -0.15), "brass", rz=0.2),
             cyl(0.15, 0.03, 0, 0.4, -0.4, "black"), cyl(0.02, 0.4, 0, 0, -0.4, "metal")],
      tint={"shell": "crimson"},
      do="Sit down at the drum kit", gives="the beat they try and where it falls apart or locks in")
model("turntable", "record player; lift onto a table with y", group="things to do", solid=False,
      parts=[box(0.5, 0.08, 0.4, 0, 0, 0, "wood"), {"group": [cyl(0.17, 0.012, c="black", sides=16), cyl(0.05, 0.014, c="red", sides=10), box(0.02, 0.016, 0.1, 0.1, 0, 0, "charcoal")], "at": [-0.04, 0.08, 0], "tag": "spin"},
             P("tube", [[0.2, 0.1, -0.14], [0.2, 0.12, 0], [0.1, 0.1, 0.06]], c="metal", r=0.008)],
      do="Put a record on the turntable", gives="the record they choose, and the first seconds of it")

# ---- Light and atmosphere ----
model("chandelier", "chandelier hanging overhead", group="light", solid=False,
      parts=[cyl(0.008, 0.5, 0, 2.3, 0, "brass"), ball(0.08, 0, 2.25, 0, "brass"),
             {"group": [P("tube", [[0, 2.22, 0], [0.2, 2.1, 0], [0.34, 2.2, 0]], c="brass", r=0.012), cyl(0.02, 0.08, 0.34, 2.2, 0, "cream"),
                        cone(0.018, 0.06, 0.34, 2.28, 0, "ember", glow=True, tag="flame", sides=4)], "spin": 6}],
      light={"at": [0, 2.0, 0], "colour": "#ffd9a0", "power": 3})
model("disco-ball", "mirror ball turning overhead", group="light", solid=False,
      parts=[cyl(0.008, 0.4, 0, 2.5, 0, "metal"), {"group": [ball(0.22, 0, 0, 0, "chrome", detail=1, glow=True)], "at": [0, 2.3, 0], "tag": "spin"},
             ball(0.03, 0.5, 1.9, 0.3, "neon-pink", glow=True, tag="twinkle"), ball(0.03, -0.4, 2.0, -0.3, "neon-blue", glow=True, tag="twinkle"),
             ball(0.03, 0.1, 1.8, -0.5, "neon-green", glow=True, tag="twinkle")],
      light={"at": [0, 2.0, 0], "colour": "#c9a0ff", "power": 2.4})
model("candles", "cluster of candles; lift onto a table with y", group="light", solid=False,
      parts=[cyl(0.04, 0.16, -0.06, 0, 0, "cream"), cyl(0.035, 0.1, 0.06, 0, 0.04, "cream"), cyl(0.03, 0.07, 0, 0, -0.07, "blush"),
             cone(0.016, 0.06, -0.06, 0.16, 0, "fire", glow=True, tag="flame", sides=4), cone(0.016, 0.06, 0.06, 0.1, 0.04, "fire", glow=True, tag="flame", sides=4),
             cone(0.014, 0.05, 0, 0.07, -0.07, "ember", glow=True, tag="flame", sides=4)],
      light={"at": [0, 0.3, 0], "colour": "#ffb060", "power": 1})
model("paper-lantern", "round paper lantern hanging overhead", group="light", solid=False,
      parts=[cyl(0.006, 0.5, 0, 2.2, 0, "black"), ball(0.26, 0, 1.95, 0, "$paper", glow=True, squash=0.9, tag="bob"), cyl(0.07, 0.03, 0, 2.17, 0, "black")],
      tint={"paper": "warm-light"}, light={"at": [0, 1.9, 0], "colour": "#ffd9a0", "power": 2.2})
model("neon-tube", "glowing neon squiggle (wall)", group="light", wall=True,
      parts=[P("tube", [[-0.35, 1.2, -0.46], [-0.2, 1.6, -0.46], [0, 1.25, -0.46], [0.2, 1.6, -0.46], [0.35, 1.2, -0.46]], c="$neon", r=0.025, glow=True, tag="pulse")],
      tint={"neon": "neon-pink"}, light={"at": [0, 1.4, 0.2], "colour": "#ff6fb5", "power": 1.6})
model("server-rack", "humming server rack with blinking lights", group="light",
      parts=[box(0.7, 1.9, 0.8, 0, 0, 0, "charcoal"), box(0.6, 0.12, 0.02, 0, 0.2, 0.4, "black", array={"n": 9, "step": [0, 0.18, 0]}),
             box(0.03, 0.03, 0.01, -0.2, 0.25, 0.415, "neon-green", glow=True, tag="twinkle", array={"n": 5, "step": [0, 0.36, 0]}),
             box(0.03, 0.03, 0.01, 0.1, 0.43, 0.415, "amber", glow=True, tag="twinkle", array={"n": 4, "step": [0, 0.36, 0]})])

# ---- Indoors ----
model("column", "stone column", group="indoors",
      parts=[box(0.5, 0.12, 0.5, 0, 0, 0, "stone"), P("lathe", [[0.2, 0.12], [0.17, 0.3], [0.16, 2.1], [0.2, 2.25]], c="white", sides=10), box(0.5, 0.12, 0.5, 0, 2.25, 0, "stone")])
model("folding-screen", "three-panel folding screen", group="indoors",
      parts=[box(0.34, 1.6, 0.03, -0.3, 0.05, 0.06, "$panel", ry=0.5), box(0.34, 1.6, 0.03, 0, 0.05, 0, "$panel", ry=-0.5), box(0.34, 1.6, 0.03, 0.3, 0.05, 0.06, "$panel", ry=0.5)],
      tint={"panel": "teal"})
model("curtain", "heavy curtain (wall)", group="indoors", wall=True,
      parts=[box(1.0, 0.04, 0.04, 0, 2.15, -0.44, "brass"), box(0.12, 2.0, 0.05, -0.42, 0.12, -0.44, "$cloth", array={"n": 8, "step": [0.12, 0, 0]}),
             box(0.12, 2.0, 0.07, -0.36, 0.12, -0.44, "$cloth", array={"n": 4, "step": [0.24, 0, 0]})],
      tint={"cloth": "wine"})
model("wine-rack", "wine rack full of bottles", group="indoors",
      parts=[box(0.8, 1.4, 0.36, 0, 0, -0.2, "dark-wood"), {"group": [P("cyl", [0.04, 0.3], (0, 0, -0.2), "dark-leaf", rx=H, sides=7)], "at": [-0.28, 0.2, 0.1], "array": {"n": 4, "step": [0.19, 0, 0]}},
             {"group": [P("cyl", [0.04, 0.3], (0, 0, -0.2), "wine", rx=H, sides=7)], "at": [-0.28, 0.55, 0.1], "array": {"n": 4, "step": [0.19, 0, 0]}},
             {"group": [P("cyl", [0.04, 0.3], (0, 0, -0.2), "dark-leaf", rx=H, sides=7)], "at": [-0.28, 0.9, 0.1], "array": {"n": 4, "step": [0.19, 0, 0]}}])
model("barrel-table", "standing table made from a barrel", group="indoors",
      parts=[P("lathe", [[0.26, 0], [0.34, 0.45], [0.26, 0.9]], c="wood", sides=12), P("ring", [0.31, 0.015], (0, 0.22, 0), "iron", rx=H), P("ring", [0.31, 0.015], (0, 0.68, 0), "iron", rx=H),
             cyl(0.42, 0.04, 0, 0.9, 0, "dark-wood", sides=14)])
model("water-cooler", "office water cooler", group="indoors",
      parts=[box(0.36, 0.95, 0.36, 0, 0, 0, "white"), P("lathe", [[0.05, 0], [0.15, 0.08], [0.15, 0.4], [0.02, 0.44]], (0, 0.95, 0), "glass", opacity=0.55),
             box(0.06, 0.04, 0.05, -0.07, 0.7, 0.2, "blue"), box(0.06, 0.04, 0.05, 0.07, 0.7, 0.2, "red")],
      do="Get a cup at the water cooler and linger", gives="the bit of office news going round the cooler today, harmless and specific")
model("yoga-mat", "yoga mat", size=(1, 2), group="indoors", solid=False, seats=[seat(0, 0.5, 0.03, 0)],
      parts=[box(0.7, 0.02, 1.8, 0, 0.001, 0, "$mat", flat=True), P("cyl", [0.06, 0.6], (0, 0.06, -0.85), "$mat", rz=H)], tint={"mat": "lilac"})
model("bunk-bed", "bunk bed", size=(1, 2), group="indoors", seats=[seat(0, 0.5, 0.5, 1)],
      parts=[box(0.07, 1.8, 0.07, 0.42, 0, 0.92, "wood", mirror="xz"), box(0.9, 0.12, 1.9, 0, 0.35, 0, "$blanket"), box(0.9, 0.12, 1.9, 0, 1.3, 0, "$blanket"),
             box(0.6, 0.1, 0.36, 0, 0.47, -0.7, "white"), box(0.6, 0.1, 0.36, 0, 1.42, -0.7, "white"),
             box(0.04, 0.9, 0.04, 0.42, 0.45, 0.5, "wood"), box(0.04, 0.04, 0.3, 0.42, 0.6, 0.7, "wood", array={"n": 3, "step": [0, 0.25, 0]})],
      tint={"blanket": "blue"})
model("bathtub", "claw-foot bathtub", size=(2, 1), group="indoors", seats=[seat(0.5, 0, 0.2, 1)],
      parts=[P("lathe", [[0.3, 0], [0.42, 0.45], [0.44, 0.5], [0.38, 0.5], [0.3, 0.1]], (0, 0.12, 0), "white", sides=12, s=[2.1, 1, 1]),
             ball(0.05, 0.6, 0.06, 0.2, "brass", mirror="xz"), P("tube", [[0.8, 0.6, 0], [0.8, 0.9, 0], [0.65, 0.9, 0]], c="brass", r=0.02),
             cyl(0.5, 0.01, 0, 0.5, 0, "water", opacity=0.7, s=[1.6, 1, 0.7])])
model("grand-piano", "grand piano with its lid up", size=(2, 2), group="indoors",
      parts=[P("extrude", [[-0.75, -0.6], [0.75, -0.6], [0.75, 0.1], [0.3, 0.75], [-0.2, 0.8], [-0.75, 0.3]], (0, 0.85, 0), "black", depth=0.3, rx=H),
             P("extrude", [[-0.75, -0.6], [0.75, -0.6], [0.75, 0.1], [0.3, 0.75], [-0.2, 0.8], [-0.75, 0.3]], (-0.02, 1.35, -0.1), "charcoal", depth=0.03, rx=H, rz=0.5),
             cyl(0.04, 0.72, 0.6, 0, 0.45, "black", mirror="x"), cyl(0.04, 0.72, 0, 0, -0.55, "black"),
             box(1.3, 0.04, 0.16, 0, 0.74, 0.68, "white"), box(0.03, 0.045, 0.1, -0.6, 0.745, 0.65, "black", array={"n": 16, "step": [0.08, 0, 0]}),
             cyl(0.01, 0.5, 0.5, 0.86, -0.2, "brass", rz=0.3)],
      do="Sit down and play something on the grand piano", gives="what they play, how far they get, and the note that goes wrong or surprisingly right")

# ---- What existing prefabs afford, so placing one is enough ----
DEFAULTS = {
    "piano": {"do": "Play something on the piano", "gives": "what they play, how far they get before a wrong note, and which key sticks"},
    "books": {"do": "Pull a book off the shelf", "gives": "the book: its title, and the line on the page it falls open at"},
    "k-bookcase": {"do": "Pull a book out of the bookcase", "gives": "the book: its title, and the line on the page it falls open at"},
    "arcade": {"do": "Play a round on the arcade cabinet", "gives": "the game, the score, and the initials at the top of the high-score table"},
    "pool-table": {"do": "Knock a few balls around the pool table", "gives": "the one shot worth describing, made or missed"},
    "fountain": {"do": "Toss a coin in the fountain", "gives": "the wish that goes with the coin, in their own few words", "leaves": True},
    "noticeboard": {"do": "Read the noticeboard and pin something up", "gives": "one notice already there that stands out, and the note they pin beside it", "leaves": True},
    "chalkboard": {"do": "Read today's chalkboard", "gives": "the special chalked up today and the joke or doodle next to it"},
    "vending-machine": {"do": "Get something from the vending machine", "gives": "what they pick, and what the machine does about it"},
    "herring-cart": {"do": "Get a herring at the cart", "gives": "how they take it (onions, pickles, by the tail) and what the herring man says"},
    "flower-stall": {"do": "Pick out flowers at the stall", "gives": "which flowers, and who they turn out to be for"},
    "mirror": {"do": "Catch yourself in the mirror", "gives": "the one thing about themselves they notice today, concrete, not a judgement"},
    "fireplace": {"do": "Stand by the fireplace a while", "gives": "one small thing the fire does or brings up: a pop, a smell, a memory with a place and a year"},
    "guitar": {"do": "Pick up the guitar", "gives": "what they play or try to, and which string is out"},
    "tv": {"do": "See what's on TV", "gives": "what's on: the programme and the one absurd moment in it"},
    "dj-booth": {"do": "Take a turn at the decks", "gives": "the track they put on and how the mix into it goes"},
    "duck": {"do": "Feed the ducks", "gives": "what one particular duck does about it"},
    "clothes-rack": {"do": "Go through the clothes rack", "gives": "the one piece they hold up: what it is, the colour, the price tag"},
    "shelf": {"do": "Browse the shelves", "gives": "the one thing they pick up and turn over, and what's written on it"},
    "bar-shelf": {"do": "Read the bottles behind the bar", "gives": "the strangest bottle on the shelf: its name, its colour, where it's from"},
    "painting": {"do": "Stand in front of the painting", "gives": "what the painting shows, and the detail most people would miss"},
    "poster": {"do": "Read the poster", "gives": "what the poster announces, with its date and the small print"},
    "houseboat": {"do": "Look in at the houseboat", "gives": "one thing visible on deck or through a window that says who lives there"},
}

GROUPS = {
    "seating": ["sofa", "armchair", "chair", "stool", "bench", "window-bench", "booth", "beanbag", "hammock", "k-lounge-sofa", "k-chair", "k-desk-chair", "k-bar-stool", "k-park-bench"],
    "tables": ["table", "long-table", "coffee-table", "desk", "counter", "reception", "terrace-table", "k-desk", "k-desk-corner", "k-round-table", "k-kitchen-bar", "k-cloth-table"],
    "home": ["kitchen", "fridge", "bed", "tv", "rug", "books", "fireplace", "k-bed", "k-stove", "k-fridge", "k-bookcase", "k-shelves", "k-carton"],
    "plants": ["plant", "big-plant", "planter", "potted-palm", "k-potted-plant", "elm", "k-tree-large", "k-tree-fall"],
    "lights": ["lamp", "desk-lamp", "street-lamp", "neon"],
    "on the wall": ["poster", "painting", "photo", "noticeboard", "chalkboard", "mirror", "clock"],
    "bar, shop, club": ["bar-shelf", "shelf", "register", "clothes-rack", "speaker", "dj-booth", "dance-floor", "pool-table", "piano", "arcade", "jukebox", "vending-machine", "k-barrel"],
    "street and water": ["bollard", "bike", "bike-rack", "herring-cart", "coffee-cart", "flower-stall", "fountain", "steps", "houseboat", "duck", "umbrella"],
    "small things": ["cups", "coffee", "espresso", "laptop", "guitar", "easel", "thing", "k-screen", "k-laptop", "k-coffee-machine", "k-hospital-sign"],
    "buildings": ["k-office-block", "k-low-building", "k-skyscraper", "k-small-building"],
}

doc = {"palette": PALETTE, "groups": GROUPS, "defaults": DEFAULTS, "models": MODELS}
text = json.dumps(doc, indent=1, ensure_ascii=False) + "\n"
for path in OUT:
    path.write_text(text)
# The frontend copy goes through Biome like everything else there.
import subprocess
subprocess.run(["pnpm", "exec", "biome", "format", "--write", "src/apps/places/scene/library.json"], cwd=ROOT / "frontend", capture_output=True)
print(f"{len(MODELS)} models, {len(DEFAULTS)} defaults, {len(PALETTE)} colours")
