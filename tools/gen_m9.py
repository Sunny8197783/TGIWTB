"""M9(무명록) 에 쓰는 캐릭터 그림을 PixelLab 에서 한꺼번에 뽑는다. 몇 번을 다시 돌려도 이미 만든 것은 건너뛴다.

  python tools/gen_m9.py chars     원화(8방향) 제출 → 끝날 때까지 기다림
  python tools/gen_m9.py anims     동작 제출 → 끝날 때까지 기다림
  python tools/gen_m9.py all       위 둘 + 시트 묶기(fetch_assets) + 법선 지도
  python tools/gen_m9.py all --only=moss_boar,narae

몬스터 동작 이름은 Enemy 가 찾는 이름 그대로: idle · move · attack · hurt · death (+ 종마다 guard·stomp·roar).
"""
import json
import os
import subprocess
import sys
import time

sys.path.insert(0, os.path.dirname(__file__))
import pixellab as pl  # noqa: E402

HURT = "flinches hard from a hit, body jerking backward, then recovers balance"

# 이름: (묘사, 크기, 뼈대 틀, [(동작, 움직임, 칸 수, 첫 칸 버림, 템플릿)])
CHARS = {
    "hush_shade": ("", 64, "mannequin", [
        ("idle", "idle stance: swaying slowly side to side, shoulders rising and falling, wisps of mist drifting off its body", 4, True, None),
        ("move", "shambling walk forward, dragging its long arms, body swaying", 8, True, None),
        ("attack", "raises one long clawed arm high overhead, then slashes it down diagonally in front, and recovers to a hunched stance", 8, False, None),
        ("hurt", HURT, 4, True, None),
        ("death", "collapses to its knees and crumbles into a low pile of grey ash on the ground", 8, False, None),
    ]),
    "moss_boar": ("a wild boar beast with a shaggy dark-brown hide overgrown with green moss and small ferns on its back, big curved ivory tusks, small red eyes, stocky and angry, fantasy monster", 64, "bear", [
        ("idle", "standing idle, breathing heavily, snorting, ears twitching", 4, True, None),
        ("move", "trotting forward at a steady run, legs galloping", 8, True, None),
        ("attack", "lowers its head and paws the ground with a front hoof again and again, shaking its tusks, about to charge", 8, False, None),
        ("hurt", "flinches from a hit, head snapping aside, stumbling, then recovers", 4, True, None),
        ("death", "staggers and topples over onto its side, lying still on the ground", 8, False, None),
    ]),
    "petal_wisp": ("a small floating flower spirit: a round glowing pink blossom bud body with large petal wings and two tiny leaf arms, a cute face with closed smiling eyes, sparkling pollen, hovering above the ground, fantasy creature", 64, "mannequin", [
        ("idle", "hovering in place, bobbing gently up and down, petal wings fluttering", 4, True, None),
        ("move", "flying forward, petal wings beating", 8, True, None),
        ("attack", "curls all petals inward gathering light, then bursts the petals open and spits a seed forward", 8, False, None),
        ("hurt", "knocked back by a hit, spinning, petals ruffled, then steadies", 4, True, None),
        ("death", "petals wilt and fall off, it drops to the ground and lies there as a withered bud", 8, False, None),
    ]),
    "stone_crab": ("a big rock crab monster: a mossy grey boulder for a shell, two heavy stone pincers, six short armored legs, two small glowing yellow eyes peeking from under the shell, blue-grey stone and moss green, fantasy monster", 64, "mannequin", [
        ("idle", "idle, pincers opening and closing slowly, eyes blinking", 4, True, None),
        ("move", "scuttling forward quickly on its many legs", 8, True, None),
        ("attack", "raises both heavy pincers high, then slams them down together in front", 8, False, None),
        ("guard", "pulls its legs and pincers in and hides completely under its boulder shell", 4, False, None),
        ("hurt", "flinches from a hit, shell rocking, pincers flailing, then recovers", 4, True, None),
        ("death", "flips over onto its back, legs curling up, and goes still", 8, False, None),
    ]),
    "frost_wolf": ("a fierce frost wolf with thick white fur, pale ice-blue tufts along its back and tail like icicles, glowing blue eyes, lean muscular body, snarling, fantasy monster", 64, "dog", [
        ("idle", "standing alert, breathing, tail swaying slightly, ears turning", 4, True, None),
        ("move", "running forward in a fast gallop", 8, True, None),
        ("attack", "crouches low on its front legs, then pounces forward with jaws wide open in a biting lunge, and lands", 8, False, None),
        ("hurt", "yelps and flinches from a hit, stumbling sideways, then recovers", 4, True, None),
        ("death", "collapses onto its side and lies still on the ground", 8, False, None),
    ]),
    "maple_fox": ("a mystical fox spirit with fiery orange-red fur, three big fluffy tails shaped like autumn maple leaves, white mask markings around glowing golden eyes, small and elegant, fantasy creature", 64, "dog", [
        ("idle", "standing idle, three tails swaying like flames, ears flicking", 4, True, None),
        ("move", "running forward lightly, tails streaming behind", 8, True, None),
        ("attack", "rears back and fans its three tails upward, then barks forward sharply with its mouth open, and settles", 8, False, None),
        ("hurt", "flinches from a hit, jumping back, fur bristling, then recovers", 4, True, None),
        ("death", "collapses and curls up on the ground, tails going limp", 8, False, None),
    ]),
    "field_scarecrow": ("a living scarecrow monster: burlap sack head with a stitched mouth and two glowing orange eyes, wide torn straw hat, ragged patched farmer coat stuffed with straw, stick arms, holding a rusty pitchfork, autumn colors, fantasy monster", 64, "mannequin", [
        ("idle", "standing idle, swaying loosely as if hanging on a pole, head tilting", 4, True, None),
        ("move", "walking forward with jerky loose limbs, straw shaking", 8, True, None),
        ("attack", "pulls the pitchfork back with both hands, then thrusts it far forward in a long stab, and pulls it back", 8, False, None),
        ("hurt", "flinches from a hit, straw bursting out, head wobbling, then recovers", 4, True, None),
        ("death", "falls apart into a heap of straw on the ground, the hat landing on top", 8, False, None),
    ]),
    "bark_golem": ("a hulking tree golem made of ancient gnarled bark and thick roots, moss and small glowing mushrooms on its broad shoulders, a hollow in its chest with a glowing green core, huge heavy wooden fists, small glowing green eyes, fantasy monster", 80, "mannequin", [
        ("idle", "standing idle, heavy slow breathing, chest core pulsing", 4, True, None),
        ("move", "stomping forward with slow heavy steps", 8, True, None),
        ("attack", "slowly raises both huge fists high above its head, then smashes them down onto the ground in front with great force", 10, False, None),
        ("hurt", "flinches from a hit, bark chips flying, torso twisting, then recovers", 4, True, None),
        ("death", "its core dims, it falls to its knees and collapses into a pile of logs and bark", 8, False, None),
    ]),
    "hollow_stag": ("a colossal ancient stag deity turned colorless: a huge deer beast with pale ash-grey fur, enormous branching antlers like bare dead tree branches with a few last glowing white leaves, hollow glowing white eyes, tattered moss and roots hanging from its body, a dark crack across its chest, majestic and terrifying, monochrome grey palette, fantasy boss monster", 96, "horse", [
        ("idle", "standing idle, breathing slowly, head lowering and rising", 4, True, None),
        ("move", "walking forward with slow majestic heavy steps", 8, True, None),
        ("attack", "lowers its head and sweeps its huge antlers in a wide horizontal arc from one side to the other, then raises its head", 8, False, None),
        ("stomp", "rears up on its hind legs, then slams both front hooves down onto the ground", 8, False, None),
        ("roar", "lowers its head, then throws it back and bellows a long roar at the sky, body trembling", 8, False, None),
        ("hurt", "staggers, head dropping low, front legs buckling, then recovers", 6, False, None),
        ("death", "its legs give out, it slowly kneels and lies down on the ground, head lowering to rest", 10, False, None),
    ]),
    "narae": ("a brave teenage girl traveler with short ash-brown hair tied with a small red ribbon, a long scarf that is vivid red on one half and faded colorless grey on the other half, cream tunic with a short teal cape, brown boots, carrying a wooden staff with a small glowing paper lantern hanging from its tip, gentle determined face", 64, "mannequin", [
        ("idle", "", 0, False, "breathing-idle"),
        ("run", "running forward at a brisk pace, holding the lantern staff in one hand, scarf trailing behind", 8, True, None),
        ("attack", "swings the lantern staff in a wide horizontal arc in front of her, then returns to a ready stance; no magic effects, no glow", 8, False, None),
        ("cheer", "raises the lantern staff high above her head with both hands and holds it there proudly, then lowers it", 8, False, None),
        ("hurt", "getting hit: flinches and recoils backward from a blow, then recovers balance", 4, True, None),
    ]),
    "npc_guide": ("an old wandering scholar man with a long white beard, round spectacles, a wide-brimmed traveler hat, a big backpack full of rolled scrolls, long olive-green coat, holding a walking staff, kind face", 64, "mannequin", [("idle", "", 0, False, "breathing-idle")]),
    "npc_warrior": ("a tall veteran knight man with short grey hair and a scar over one eye, heavy steel plate armor with a crimson tabard, a big kite shield on his back, both hands resting on a greatsword planted in the ground in front of him", 64, "mannequin", [("idle", "", 0, False, "breathing-idle")]),
    "npc_archer": ("a ranger woman with a long green hooded cloak and a feathered cap, brown braid, leather bracers, a longbow and quiver on her back, a small white messenger bird on her shoulder", 64, "mannequin", [("idle", "", 0, False, "breathing-idle")]),
    "npc_mage": ("an elegant sorceress woman with long violet hair, deep blue robe with golden star embroidery, a tall crystal-topped staff in one hand and an open spellbook in the other", 64, "mannequin", [("idle", "", 0, False, "breathing-idle")]),
    "npc_rogue": ("a lean masked man in dark charcoal clothes with an ink-black scarf covering his lower face, a large calligraphy brush strapped on his back and two short blades at his belt, arms crossed, quiet and sharp", 64, "mannequin", [("idle", "", 0, False, "breathing-idle")]),
    "npc_miko": ("a shrine maiden girl in a white kimono top and red hakama, long black hair with a cherry blossom hairpin, holding a paper purification wand, serene face", 64, "mannequin", [("idle", "", 0, False, "breathing-idle")]),
    "npc_keeper": ("an old lighthouse keeper man in a navy pea coat and captain cap, bushy grey mustache, holding a big brass oil lantern, weathered kind face", 64, "mannequin", [("idle", "", 0, False, "breathing-idle")]),
    "npc_monk": ("a calm bald buddhist monk man in saffron-orange and brown robes, wooden prayer beads around his neck, holding a wooden bell mallet, gentle smile", 64, "mannequin", [("idle", "", 0, False, "breathing-idle")]),
    "npc_scribe": ("a small gentle woman scribe in moss-green robes with a leaf-shaped hair ornament in her brown bun, carrying a huge open book and a feather quill, ink-stained fingers, round glasses", 64, "mannequin", [("idle", "", 0, False, "breathing-idle")]),
}


def wait_jobs(limit=5400):
    pl.cmd_wait(["--limit", str(limit)])


def submit_chars(names):
    ids = pl.load_ids()
    for name in names:
        desc, size, template, _ = CHARS[name]
        if name in ids["chars"]:
            continue
        body = {"description": desc, "name": name, "view": "high top-down",
                "image_size": {"width": size, "height": size}, "template_id": template,
                "outline": "selective outline", "detail": "high detail", "no_background": True}
        try:
            r = pl.call("POST", "/create-character-v3", body)
        except RuntimeError as e:
            print("FAILED char", name, e)
            continue
        ids["chars"][name] = {"id": r["character_id"], "desc": desc, "size": size, "anims": {}}
        ids.setdefault("jobs", []).append({"job": r["background_job_id"], "what": name})
        pl.save_ids(ids)
        entry = {"sheet": f"art/characters/{name}/rot.png", "char": r["character_id"], "rotations": True}
        if size > 64:
            entry["cell"] = 192
        pl.manifest_add(entry)
        print("char 제출", name, r["character_id"], flush=True)
        time.sleep(1)


def fetch(name):
    py = sys.executable
    subprocess.run([py, os.path.join(pl.ROOT, "tools", "fetch_assets.py"), f"--only=characters/{name}/", "--force"])
    subprocess.run([py, os.path.join(pl.ROOT, "tools", "normals.py")])


def submit_anims(names):
    for name in names:
        _, size, _, anims = CHARS[name]
        for anim, action, frames, drop, template in anims:
            ids = pl.load_ids()
            if name not in ids["chars"] or anim in ids["chars"][name]["anims"]:
                continue
            args = [name, anim, action]
            if template:
                args += ["--template", template]
            else:
                args += ["--frames", str(frames)] + (["--drop-first"] if drop else [])
            # 동시 작업 칸이 8개뿐이다 — 동작 하나(8방향)를 내고, 다 끝난 뒤 다음 것을 낸다
            wait_jobs()
            try:
                pl.cmd_anim(args)
            except RuntimeError as e:
                print("FAILED anim", name, anim, e, flush=True)
                continue
            if size > 64:
                # 큰 몸은 큰 칸에 묶는다 (fetch_assets 의 cell)
                pl.manifest_add({"sheet": f"art/characters/{name}/{anim}.png", "cell": 192})
        wait_jobs()
        fetch(name)   # 캐릭터 하나가 끝날 때마다 시트로 묶는다 — 끝난 것부터 게임에서 볼 수 있게
        print("묶음", name, flush=True)


def main():
    sys.stdout.reconfigure(encoding="utf-8")
    what = sys.argv[1] if len(sys.argv) > 1 else "all"
    only = pl.opt(sys.argv, "--only")
    names = only.split(",") if only else list(CHARS)
    if what in ("chars", "all"):
        submit_chars(names)
        wait_jobs()
    if what in ("anims", "all"):
        submit_anims(names)
    if what == "fetch":
        for name in names:
            fetch(name)
    print("gen_m9 끝", flush=True)


if __name__ == "__main__":
    main()
