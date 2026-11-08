#!/usr/bin/env python3
"""Snapshot how much separate casts have grown into each other; appends to tools/lived-self/out/casts.jsonl.

Usage: PGPASSWORD=... tools/lived-self/casts.py casts.json [base-url]
casts.json maps cast name to persona ids; anyone unlisted is "old".
"""
import collections, json, pathlib, subprocess, sys, time, urllib.request

root = pathlib.Path(__file__).resolve().parents[2]
if len(sys.argv) < 2: sys.exit(__doc__)
casts_file = pathlib.Path(sys.argv[1])
base = sys.argv[2] if len(sys.argv) > 2 else "http://localhost:5072/api"
party = "00000000-0000-0000-0000-000000000000"
get = lambda path: json.load(urllib.request.urlopen(f"{base}/worlds/{party}{path}"))

cast_of = {}
if casts_file.exists():
    for cast, ids in json.loads(casts_file.read_text()).items():
        cast_of.update({i: cast for i in ids})
group = lambda pid: cast_of.get(pid, "old")

rows = subprocess.run(
    ["psql", "-h", "localhost", "-p", "5455", "-U", "partytown", "-d", "partytown", "-At", "-c",
     "select grainidextensionstring, convert_from(payloadbinary,'UTF8') from public.orleansstorage where graintypestring='ties'"],
    capture_output=True, text=True, check=True).stdout.splitlines()

names = {l["personaId"]: l["name"] for l in get("/lives")}
dashed = lambda h: f"{h[:8]}-{h[8:12]}-{h[12:16]}-{h[16:20]}-{h[20:]}"
pairs = {}
for row in rows:
    key, _, payload = row.partition("|")
    me = dashed(key.split(":")[1])
    for other, tie in json.loads(payload).get("Ties", {}).items():
        if other.startswith("$") or me not in names or other not in names:
            continue
        pair = tuple(sorted((me, other)))
        seen = pairs.setdefault(pair, {"exchanges": 0.0, "warmth": []})
        seen["exchanges"] = max(seen["exchanges"], tie.get("Exchanges", 0))
        seen["warmth"].append(round(tie.get("Warmth", 0), 2))

within, across = [], []
for (a, b), tie in pairs.items():
    (within if group(a) == group(b) else across).append((tie["exchanges"], names[a], names[b], group(a), group(b), tie["warmth"]))

mix = collections.Counter(f'{a["app"]}.{a["action"]}' for a in get("/activities"))
snapshot = {
    "at": time.strftime("%Y-%m-%dT%H:%M:%S"),
    "people": len(names),
    "withinPairs": len(within),
    "withinExchanges": round(sum(p[0] for p in within), 1),
    "acrossPairs": len(across),
    "acrossExchanges": round(sum(p[0] for p in across), 1),
    "acrossTalked": sum(1 for p in across if p[0] >= 2),
    "topAcross": [f"{p[1]} ({p[3]}) - {p[2]} ({p[4]}): {p[0]:.1f} {p[5]}" for p in sorted(across, reverse=True)[:8]],
    "mix": dict(mix.most_common(12)),
}
out = root / "tools" / "lived-self" / "out" / "casts.jsonl"
out.parent.mkdir(parents=True, exist_ok=True)
with out.open("a") as f:
    f.write(json.dumps(snapshot, ensure_ascii=False) + "\n")
print(json.dumps(snapshot, ensure_ascii=False, indent=2))
