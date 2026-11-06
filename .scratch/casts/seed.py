#!/usr/bin/env python3
"""Generate casts through the real generator and move them in. Resumable; state in generated/.

  seed.py generate <key>   write generated/<key>.json only, nothing reaches the World
  seed.py all              generate, move in, relations, for every cast in spec.json
  seed.py jobs             set jobs as a temporary Player, removed again straight after
"""
import json, pathlib, subprocess, sys, time, urllib.error, urllib.request, uuid
import websocket

here = pathlib.Path(__file__).resolve().parent
repo = here.parents[1]
spec = json.loads((here / "spec.json").read_text())
out = here / "generated"
out.mkdir(exist_ok=True)
api = "http://localhost:5072/api"
world = f"{api}/worlds/00000000-0000-0000-0000-000000000000"
token = lambda: subprocess.run([repo / ".claude/skills/dev-login/login.sh", "token", "admin"], capture_output=True, text=True, check=True).stdout.strip().splitlines()[-1]


def call(method, url, body=None):
    data = None if body is None else json.dumps(body).encode()
    req = urllib.request.Request(url, data=data, method=method, headers={"Authorization": f"Bearer {token()}", "Content-Type": "application/json"})
    try:
        with urllib.request.urlopen(req, timeout=120) as res:
            raw = res.read()
            return json.loads(raw) if raw else None
    except urllib.error.HTTPError as e:
        raise SystemExit(f"{method} {url}: {e.code} {e.read()[:300]!r}")


def generate(seed):
    ws = websocket.create_connection("ws://localhost:5072/api/Persona/ws", header=[f"Authorization: Bearer {token()}"], timeout=180)
    ws.send(json.dumps({"type": "generate", "prompt": seed}))
    raw = ""
    try:
        while True:
            env = json.loads(ws.recv())
            if env["type"] == "persona.generation.delta":
                raw += env["data"]["data"]
            elif env["type"] == "persona.generation.completed":
                return {**env["data"], "raw": raw}
            elif env["type"] == "persona.generation.error":
                raise SystemExit(f"generator: {env['data']}")
    finally:
        ws.close()


def person(key, seed):
    file = out / f"{key}.json"
    if file.exists():
        return json.loads(file.read_text())
    made = generate(seed)
    if made.get("chattiness") is None:
        raise SystemExit(f"{key}: no Card, so not the model these prompts were written for; refusing to move them in")
    file.write_text(json.dumps(made, ensure_ascii=False, indent=2))
    return made


def move_in(key, made):
    if made.get("id"):
        return made["id"]
    pid = str(uuid.uuid4())
    call("PUT", f"{api}/Persona/{pid}", {
        "id": pid, "name": made["name"], "systemPrompt": made["systemPrompt"], "bio": made["bio"],
        "chattiness": made.get("chattiness") or 0.5, "impulsivity": made.get("impulsivity") or 0.5,
    })
    made["id"] = pid
    (out / f"{key}.json").write_text(json.dumps(made, ensure_ascii=False, indent=2))
    # Inviting is idempotent; the clock's write can lose a race after the Participant is in.
    for attempt in range(3):
        try:
            call("POST", f"{world}/residents", {"personaId": pid})
            break
        except SystemExit:
            if attempt == 2:
                raise
            time.sleep(5)
    return pid


def jobs():
    player = call("POST", f"{world}/player", {"name": "Test Agent"})["playerId"]
    try:
        for cast in spec.values():
            for p in cast["people"]:
                file = out / f"{p['key']}.json"
                if "job" not in p or not file.exists():
                    continue
                pid = json.loads(file.read_text())["id"]
                job = {"workplace": cast.get("workplace"), **p["job"]}
                print(call("PUT", f"{world}/jobs/{pid}", job)["name"], "->", job["title"], "at", job["workplace"])
    finally:
        call("DELETE", f"{world}/players/{player}")
        print("removed temporary Player")


def main():
    if sys.argv[1] == "jobs":
        jobs()
        return
    if sys.argv[1] == "generate":
        seeds = {p["key"]: p["seed"] for c in spec.values() for p in c["people"]}
        made = person(sys.argv[2], seeds[sys.argv[2]])
        print(made["raw"])
        return
    ids = {}
    casts = {}
    declared = {frozenset((r["aId"], r["bId"])) for r in call("GET", f"{world}/relations")}
    try:
        for name, cast in spec.items():
            for p in cast["people"]:
                made = person(p["key"], p["seed"])
                ids[p["key"]] = move_in(p["key"], made)
                casts.setdefault(name, []).append(ids[p["key"]])
                print(f"{name}: {made['name']} ({made.get('chattiness')}/{made.get('impulsivity')})")
            for a, b, kind, story in cast["relations"]:
                if frozenset((ids[a], ids[b])) not in declared:
                    call("POST", f"{world}/relations", {"aId": ids[a], "bId": ids[b], "kind": kind, "story": story, "past": False})
    finally:
        (here / "casts.json").write_text(json.dumps(casts, indent=2))


main()
