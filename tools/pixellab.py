"""PixelLab REST v2 로 캐릭터·애니메이션·그림을 만든다. 만든 것의 id 는 tools/pixellab_ids.json 에 이름으로 적어 둔다.

열쇠: 환경 변수 PIXELLAB_KEY 또는 tools/.pixellab_key (깃에 올리지 않는다).

  python tools/pixellab.py balance
  python tools/pixellab.py char <이름> "<묘사>" [--size 64] [--template mannequin]
  python tools/pixellab.py anim <이름> <동작> "<움직임 묘사>" [--frames 8] [--drop-first] [--template breathing-idle]
        [--dirs south,east] [--mode v3]
  python tools/pixellab.py wait            진행 중인 작업이 모두 끝날 때까지 기다린다
  python tools/pixellab.py image <파일> "<묘사>" --w 64 --h 64 [--bg]      pixflux 한 장 (아이콘·삽화)
  python tools/pixellab.py object <파일> "<묘사>" --w 128 --h 128           지도 소품 한 장 (high top-down)
  python tools/pixellab.py status <이름>

묶기(시트)는 tools/fetch_assets.py 가 한다 — 여기서 만든 캐릭터·동작은 tools/assets.json 에 자동으로 더한다.
스타일은 기존 그림과 같게: high top-down, selective outline, high detail, 64px (CLAUDE.md).
"""
import base64
import json
import os
import sys
import time
import urllib.error
import urllib.request

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), ".."))
API = "https://api.pixellab.ai/v2"
IDS = os.path.join(ROOT, "tools", "pixellab_ids.json")
MANIFEST = os.path.join(ROOT, "tools", "assets.json")
DIRS = ["south", "south-east", "east", "north-east", "north", "north-west", "west", "south-west"]


def key():
    k = os.environ.get("PIXELLAB_KEY")
    if not k:
        p = os.path.join(ROOT, "tools", ".pixellab_key")
        if os.path.exists(p):
            k = open(p, encoding="utf-8").read().strip()
    if not k:
        sys.exit("PIXELLAB_KEY 가 없다 (환경 변수 또는 tools/.pixellab_key)")
    return k


def call(method, path, body=None, raw=False, tries=4):
    data = json.dumps(body).encode("utf-8") if body is not None else None
    for attempt in range(tries):
        req = urllib.request.Request(API + path, data=data, method=method, headers={
            "Authorization": "Bearer " + key(), "Content-Type": "application/json", "User-Agent": "pixellab-tool"})
        try:
            with urllib.request.urlopen(req, timeout=120) as r:
                payload = r.read()
                return payload if raw else json.loads(payload.decode("utf-8"))
        except urllib.error.HTTPError as e:
            text = e.read().decode("utf-8", "replace")
            # 429(동시 작업 한도)·5xx 는 잠깐 쉬고 다시
            if e.code in (429, 500, 502, 503, 504) and attempt < tries - 1:
                time.sleep(20 * (attempt + 1))
                continue
            raise RuntimeError(f"{method} {path} → {e.code}: {text[:600]}")
        except (urllib.error.URLError, TimeoutError) as e:
            if attempt < tries - 1:
                time.sleep(10)
                continue
            raise RuntimeError(f"{method} {path} → {e}")


def load_ids():
    return json.load(open(IDS, encoding="utf-8")) if os.path.exists(IDS) else {"chars": {}, "jobs": []}


def save_ids(ids):
    json.dump(ids, open(IDS, "w", encoding="utf-8"), ensure_ascii=False, indent=1)


def manifest_add(entry):
    """assets.json 에 같은 시트가 없으면 더한다."""
    m = json.load(open(MANIFEST, encoding="utf-8"))
    if any(e.get("sheet") == entry["sheet"] for e in m):
        for e in m:
            if e.get("sheet") == entry["sheet"]:
                e.update(entry)
    else:
        m.append(entry)
    json.dump(m, open(MANIFEST, "w", encoding="utf-8"), ensure_ascii=False, indent=1)


def opt(args, name, default=None):
    for i, a in enumerate(args):
        if a == name and i + 1 < len(args):
            return args[i + 1]
        if a.startswith(name + "="):
            return a[len(name) + 1:]
    return default


def flag(args, name):
    return name in args


def poll(job_id, quiet=False, limit=1500):
    t0 = time.time()
    while time.time() - t0 < limit:
        j = call("GET", f"/background-jobs/{job_id}")
        if j["status"] != "processing":
            return j
        time.sleep(6)
    j = call("GET", f"/background-jobs/{job_id}")   # 절전에서 깨어나 시간이 훌쩍 지났을 수 있다 — 한 번 더 본다
    return j if j["status"] != "processing" else {"status": "timeout", "id": job_id}


def cmd_balance(args):
    print(json.dumps(call("GET", "/balance"), ensure_ascii=False))


def cmd_char(args):
    name, desc = args[0], args[1]
    ids = load_ids()
    if name in ids["chars"] and not flag(args, "--force"):
        print("이미 있다:", name, ids["chars"][name]["id"])
        return
    size = int(opt(args, "--size", "64"))
    body = {
        "description": desc, "name": name, "view": "high top-down",
        "image_size": {"width": size, "height": size},
        "template_id": opt(args, "--template", "mannequin"),
        "outline": "selective outline", "detail": "high detail", "no_background": True,
    }
    seed = opt(args, "--seed")
    if seed:
        body["seed"] = int(seed)
    r = call("POST", "/create-character-v3", body)
    job = r.get("background_job_id")
    cid = r.get("character_id")
    print("submitted", name, "char", cid, "job", job, r.get("usage"))
    j = poll(job) if job else r
    if j.get("status") not in ("completed", None):
        print("FAILED", json.dumps(j, ensure_ascii=False)[:800])
        return
    cid = cid or (j.get("last_response") or {}).get("character_id")
    ids["chars"][name] = {"id": cid, "desc": desc, "size": size, "anims": {}}
    save_ids(ids)
    manifest_add({"sheet": f"art/characters/{name}/rot.png", "char": cid, "rotations": True})
    print("done", name, cid)


def cmd_anim(args):
    name, anim, action = args[0], args[1], args[2]
    ids = load_ids()
    ch = ids["chars"][name]
    if anim in ch["anims"] and not flag(args, "--force") and not opt(args, "--dirs"):
        print("이미 있다:", name, anim)
        return
    template = opt(args, "--template")
    dirs = opt(args, "--dirs")
    dirs = dirs.split(",") if dirs else DIRS
    anim_name = f"{name}_{anim}"
    body = {"character_id": ch["id"], "animation_name": anim_name, "directions": dirs}
    if template:
        body["template_animation_id"] = template
        body["mode"] = opt(args, "--mode", "template")
    else:
        body["action_description"] = action
        body["mode"] = opt(args, "--mode", "v3")
        body["frame_count"] = int(opt(args, "--frames", "8"))
        body["keep_first_frame"] = not flag(args, "--drop-first")
    group = ch["anims"].get(anim, {}).get("group")
    if group and opt(args, "--dirs"):
        body["animation_group_id"] = group
    r = call("POST", "/characters/animations", body)
    jobs = r.get("background_job_ids") or ([r["background_job_id"]] if r.get("background_job_id") else [])
    ch["anims"][anim] = {"name": anim_name, "group": r.get("animation_group_id") or group, "action": action,
                         "frames": body.get("frame_count"), "drop_first": flag(args, "--drop-first")}
    ids["jobs"] = [j for j in ids.get("jobs", [])] + [{"job": j, "what": anim_name} for j in jobs]
    save_ids(ids)
    # 템플릿 동작은 우리가 준 이름이 아니라 템플릿 이름(breathing-idle → breathing_idle) 폴더로 내려온다
    manifest_add({"sheet": f"art/characters/{name}/{anim}.png", "char": ch["id"],
                  "anim": template.replace("-", "_") if template else anim_name})
    print("submitted", anim_name, len(jobs), "jobs", json.dumps(r.get("usage"), ensure_ascii=False),
          {k: v for k, v in r.items() if k not in ("background_job_ids", "usage")})


def cmd_wait(args):
    ids = load_ids()
    pending = ids.get("jobs", [])
    t0 = time.time()
    limit = float(opt(args, "--limit", "3000"))
    while pending and time.time() - t0 < limit:
        still = []
        for p in pending:
            try:
                j = call("GET", f"/background-jobs/{p['job']}")
            except RuntimeError as e:
                if "404" in str(e):
                    continue   # 끝난 작업은 치워진다
                print("?", e)
                still.append(p)
                continue
            if j["status"] == "processing":
                still.append(p)
            elif j["status"] != "completed":
                print("FAILED", p["what"], json.dumps(j.get("last_response"), ensure_ascii=False)[:300])
        pending = still
        ids["jobs"] = pending
        save_ids(ids)
        if pending:
            print(f"… {len(pending)} 남음 ({int(time.time() - t0)}s)", flush=True)
            time.sleep(20)
    print("모두 끝" if not pending else f"시간 초과: {len(pending)} 남음")


def cmd_status(args):
    ids = load_ids()
    c = call("GET", f"/characters/{ids['chars'][args[0]]['id']}")
    print(json.dumps({k: c.get(k) for k in ("id", "name", "size", "status", "animation_count")}, ensure_ascii=False))
    for a in c.get("animations") or []:
        print("  ", json.dumps(a, ensure_ascii=False)[:300])


def save_b64(image, dest):
    b = image.get("base64") if isinstance(image, dict) else image
    if b.startswith("data:"):
        b = b.split(",", 1)[1]
    os.makedirs(os.path.dirname(dest), exist_ok=True)
    open(dest, "wb").write(base64.b64decode(b))


def cmd_image(args):
    dest, desc = os.path.join(ROOT, args[0]), args[1]
    body = {"description": desc,
            "image_size": {"width": int(opt(args, "--w", "64")), "height": int(opt(args, "--h", "64"))},
            "no_background": not flag(args, "--bg"), "outline": opt(args, "--outline", "selective outline"),
            "shading": opt(args, "--shading", "detailed shading"), "detail": opt(args, "--detail", "highly detailed")}
    view = opt(args, "--view")
    if view:
        body["view"] = view
    seed = opt(args, "--seed")
    if seed:
        body["seed"] = int(seed)
    r = call("POST", "/create-image-pixflux", body)
    save_b64(r["image"], dest)
    print("image", args[0], json.dumps(r.get("usage"), ensure_ascii=False))


def cmd_object(args):
    dest, desc = os.path.join(ROOT, args[0]), args[1]
    body = {"description": desc, "view": "high top-down",
            "image_size": {"width": int(opt(args, "--w", "128")), "height": int(opt(args, "--h", "128"))},
            "outline": "selective outline", "shading": "detailed shading", "detail": "high detail"}
    r = call("POST", "/map-objects", body)
    j = poll(r["background_job_id"])
    if j.get("status") != "completed":
        print("FAILED", args[0], json.dumps(j, ensure_ascii=False)[:400])
        return
    lr = j.get("last_response") or {}
    img = lr.get("image") or (lr.get("images") or [None])[0]
    if img:
        save_b64(img, dest)
    else:
        # 저장된 오브젝트로 내려받는다
        payload = call("GET", f"/objects/{r['object_id']}/zip", raw=True)
        open(dest + ".zip", "wb").write(payload)
    print("object", args[0], r.get("object_id"), json.dumps(r.get("usage"), ensure_ascii=False), list(lr.keys())[:8])


if __name__ == "__main__":
    sys.stdout.reconfigure(encoding="utf-8")
    # 자동 재개 작업이 '누가 일하는 중'인지 보는 표시
    os.makedirs(os.path.join(ROOT, ".claude"), exist_ok=True)
    open(os.path.join(ROOT, ".claude", "heartbeat"), "w").write(str(time.time()))
    cmds = {"balance": cmd_balance, "char": cmd_char, "anim": cmd_anim, "wait": cmd_wait, "status": cmd_status,
            "image": cmd_image, "object": cmd_object}
    if len(sys.argv) < 2 or sys.argv[1] not in cmds:
        sys.exit(__doc__)
    cmds[sys.argv[1]](sys.argv[2:])
