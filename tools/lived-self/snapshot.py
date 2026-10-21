#!/usr/bin/env python3
"""Append a lifelikeness snapshot (ADR 0021) of a running World to .scratch/lived-self/metrics.jsonl.

Usage: tools/lived-self/snapshot.py [party-id] [base-url]
"""
import json, sys, time, urllib.request, pathlib, statistics

party = sys.argv[1] if len(sys.argv) > 1 else "00000000-0000-0000-0000-000000000000"
base = sys.argv[2] if len(sys.argv) > 2 else "http://localhost:5072/api"
get = lambda path: json.load(urllib.request.urlopen(f"{base}/worlds/{party}{path}"))

voice = get("/voice")
selves = {s["personaId"]: s for s in get("/selves")}
name = lambda pid: selves.get(pid, {}).get("name", pid[:8])
avg = lambda d: round(statistics.mean(d.values()), 3) if d else None

snapshot = {
    "at": time.strftime("%Y-%m-%dT%H:%M:%S"),
    "avgWords": avg(voice["averageWords"]),
    "stageDirectionRate": avg(voice["stageDirectionRate"]),
    "vocativeRate": avg(voice["vocativeRate"]),
    "repeatRate": avg(voice["repeatRate"]),
    "wornWords": voice["wornWords"],
    "perPersona": {
        name(pid): {
            "words": round(voice["averageWords"].get(pid, 0)),
            "vocative": round(voice["vocativeRate"].get(pid, 0), 2),
            "repeat": round(voice["repeatRate"].get(pid, 0), 2),
            "battery": selves.get(pid, {}).get("battery"),
            "availability": selves.get(pid, {}).get("availability"),
        }
        for pid in voice["averageWords"]
    },
}
out = pathlib.Path(__file__).resolve().parents[2] / ".scratch" / "lived-self" / "metrics.jsonl"
out.parent.mkdir(parents=True, exist_ok=True)
with out.open("a") as f:
    f.write(json.dumps(snapshot, ensure_ascii=False) + "\n")
print(json.dumps(snapshot, ensure_ascii=False, indent=2))
