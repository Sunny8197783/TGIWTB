# PixelMMO

Godot 4.7 (.NET/C#) 액션 RPG. **저해상도 3D 디오라마 + 픽셀 스프라이트** (A Short Hike / HD-2D 계열).
2026-09-28 전면 재시작. 설계·조사 근거는 `docs/design/REBUILD.md`, 큰 지도(1024m)는 `docs/design/WORLD.md`, 둥근 대륙·왕국(M10)은 `docs/design/M10_kingdoms.md`. 이전 버전은 `archive/astra-3d` 브랜치.

## 절대 규칙
1. 직업·스킬·기연 데이터는 코드에 하드코딩하지 않는다. 전부 `data/**.json` + C# 로더.
2. 플레이어 상태 접근은 반드시 `IPlayerContext` 인터페이스를 경유한다. (P5에서 서버 권위로 전환하기 위함)
3. 숙련도는 '사용 횟수'가 아니라 '유효한 사용'만 센다.
4. 히든 요소의 조건은 게임 내 어디에도 노출하지 않는다.
5. 픽셀 스프라이트는 **1텍셀 = 1화면픽셀**로 찍힌다 (`scripts/Core/Px.cs`). 월드 1m = 32텍셀. 확대·축소해서 쓰지 않는다.
6. 이 노트북(i5-8250U + Intel UHD 620)에서 60fps. 렌더러는 **Mobile** (Forward+ 는 이 드라이버에서 크래시).

## 화면 규격
- 3D 월드는 640x360 SubViewport → 정수배 확대 (`Render/PixelView.cs`). 카메라는 텍셀 격자에 스냅.
- 카메라는 북쪽(-Z)을 38° 내려다본다. 보여 줄 풍경(폭포·바다·노을)은 북쪽에 둔다.
- 스프라이트는 PixelLab `high top-down`. 스타일 기준 에셋: `art/env/tree_sakura_0.png`, `art/env/house_red_0.png`.

## 코드 규칙
- 네임스페이스: `PixelMmo.Core`, `PixelMmo.Render`, `PixelMmo.World`, `PixelMmo.Combat`, `PixelMmo.Data`, `PixelMmo.Dev`
- 매직 넘버 금지. 전투 수치는 `scripts/Combat/CombatTuning.cs` 한 곳에 모은다
- 모든 타이밍 값은 초 단위 float, 60fps 기준으로 주석에 프레임 수 병기
- 커밋은 마일스톤 단위로. 마일스톤 완료 시 빌드가 반드시 통과해야 함

## 빌드 / 실행 / 검증
- `dotnet build` → `tools/godot.sh --headless --import` (새 그림을 넣었을 때) → `tools/godot.sh`
- 지도(1024m): `$PY tools/mapgen/mapgen.py` → `data/world/*` + `docs/design/map_preview.png` (약 2분)
  - `$PY` = `/c/Users/gram/AppData/Local/Programs/Python/Python313/python.exe` (numpy·Pillow 가 여기 깔려 있다. Bash 의 `python` 은 msys 라 없다)
  - 지역·절벽선·강·길·마을·폭포는 mapgen.py 위쪽 상수. 하루미 마을이 한가운데(`CENTER`), 지역은 마을에서 본 방향(북 0°)과 절벽 A(`CLIFF_A_PTS`, 위/아래)로 가른다
  - 왕국 여섯은 `KINGDOMS` (성 그림 `keep_<id>`·`castle_front_<id>`, 집 종류, 전송석 레벨), 지역 입구는 `HUBS`, 우두머리 자리는 `BOSS_SPOTS`(spawns.json fixed 와 같게)
- 스프라이트 법선 지도: 새 그림을 받으면 `$PY tools/normals.py` (→ `*_n.png`: RG 법선, B 부피 깊이) 뒤 `--headless --import`.
  나무·덤불·바위는 B 깊이만큼 판을 부풀려 입체로 그린다 (`PropBuilder.Relief`, 끄기 `--no=relief`). 충돌은 그림 아랫선 모양 그대로 (`World/Footprint`)
- 세계수는 조각 셋(`art_src/world_tree`)을 `$PY tools/compose_world_tree.py` 로 합성한다 (PixelLab 한 장은 256px 까지)
- 에셋: `tools/assets.json` 에 PixelLab id 를 적고 `python tools/fetch_assets.py`
- 화면 확인: `tools/godot.sh -- --capture --shot=x,z,시각[,피치,거리] --out=user://shots`
  - 전망(V) 모습은 피치 7·거리 34: `--shot=100,374,18.45,7,34` (노을 절벽의 해넘이). 전망 지점은 카메라가 천천히 옮겨 가니 캡처엔 피치를 직접 준다
- 성능: `... --capture --novsync --hold=3 --shot=...` (장면마다 프레임 시간), `--no=grass,props,hero,monsters,shadows,...` 로 A/B
  - 노트북이 달아올라 같은 장면도 14→23ms 로 흔들린다. A/B 는 기준·변경을 **번갈아** 여러 번 잰다 (한 번씩 차례로 재면 순서가 결과를 만든다)
  - `[Spike]` 줄: 튄 프레임 앞뒤의 그리기 CPU/GPU·물리 걸음 수·GC. Performance 모니터(스크립트·물리)는 1초에 한 번만 바뀌어 프레임 단위로는 못 쓴다
  - 매 프레임 넘기는 셰이더 변수·조작 이름은 `Render/Uniform`·`Core/Controls` 의 StringName 을 쓴다 (문자열은 GC 끊김을 부른다)
  - 처음 그리는 셰이더는 파이프라인을 만드느라 멈칫한다 → 새 이펙트 셰이더는 `CombatFx.Prewarm` 에 넣는다
- 동작 확인: `--press=attack --settle=12` (누르고 12프레임 뒤), `--react=guard --after=7` (적 공격 예고 끝에 막기 → 패링)
- 캐릭터 그림: `tools/assets.json` 에 `{"sheet": ..., "char": id, "anim": "PixelLab 애니 이름"}` → 원본은 `art_src/`, 시트는 `art/characters/<캐릭터>/<동작>.png`
  - 한 방향만 망쳤으면 그 방향만 다시 뽑아 `"redo": {"west": "새 애니 이름"}`, 칼끝 푸른 빛만 남았으면 `"deglow": ["south"]` (푸른 옷·눈 없는 캐릭터만). 받은 뒤 `--force`
  - rot 시트를 먼저 받는다 (없으면 그 동작 시트가 빈 줄로 묶인다)
- 스킬: `data/skills/*.json` (이벤트 목록, `Combat/SkillRunner` 가 해석. 투사체는 `projectile`·`rain` 이벤트 → `Combat/Projectile`). 칸 구성은 직업이 정한다
- 직업(M8): `data/jobs/*.json` — 평타 연속기(근접 베기 또는 투사체)·스킬 칸(열리는 레벨)·성별별 원화·레벨당 체력·힘. 레벨 곡선 `data/player/levels.json`, 몬스터 `exp`
  - 견습생으로 시작 → Lv 10 에 마을 교관 NPC(`data/npcs/*.json`, `World/Npcs`)에게서 전직. 확인은 `--job=archer --level=15 --press=skill_3 --settle=40`,
    전직 대화는 `--level=10 --tap=interact:6:30 --settle=200 --shot=533,885,10` (전사 교관 앞에서 F 를 30프레임마다 톡톡)
- 몬스터·사냥터(M9): `data/monsters/*.json` — 레벨·공격 목록(`melee`·`charge`·`shoot`·`slam`(leap)·`spin`, 연속 `hits`, 못 막는 `unblockable`, 순간이동 `blink`)·원거리 `keep_away`·등껍질 `guard_arc_deg`·우두머리 `boss`+`phases`.
  색만 바꾼 변종은 `"base"` + `"recolor"` (hue 범위는 `tools/palette.py --mark` 로). 경험치는 레벨 차이로 깎인다 (`levels.json exp_below`)
  - 사냥터 무리는 `data/world/spawns.json` (지역별 계획 → `$PY tools/place_camps.py --preview` 가 자리를 채운다, 우두머리는 fixed). 주인공 둘레 60m 무리만 세운다 (`Combat/MonsterSpawner`, 한 프레임에 한 무리씩, 몬스터 그림은 시작할 때 뒤에서 미리 읽는다)
    `dist` [최소, 최대] = 마을에서 거리대 (같은 지역도 멀수록 세다), `gap` = 무리 간격 (초원은 18m)
  - 몬스터 그림은 5방향(S·SE·E·NE·N)만 뽑고 `assets.json` `"mirror": true` 로 서쪽을 뒤집어 채운다. 동작은 move·attack(·attack2)·death
  - 확인: `--no=monsters --spawn=goblin_archer,kappa --react=guard --after=8 --shot=600,700,10`. id 뒤 `*` 는 정예 (`--spawn=forest_goblin*`)
  - 정예(M10): 무리에서 8% 확률, 체력 x3·피해 x1.5·경험치 x5, 붉게 일렁임 (`CombatTuning.Elite*`). 그림 크기는 그대로 (규칙 5)
  - 지역 우두머리 여섯은 기존 그림의 색 변종(`base`+`recolor`+`boss`+`phases`): 고블린 대장·허수아비 왕·텐구 대장·고목 수호자·안개 여왕·설인 왕
  - 쓰러지면 그 레벨 필요 경험치의 10% 를 잃는다 (`levels.json death_exp_loss`, 레벨은 안 내려감)
- 전송석(M10): `meta.waystones` (마을 1·지역 입구 6·왕국 6, 레벨). F → 부활 지점·체력 회복 + 목록, 레벨이 되면 가 본 적 없어도 간다 (`World/Waystones`).
  교관 넷·안내인은 하루미 마을 광장. 확인은 `--level=12 --shot=520,575.8,10 --tap=interact:1:20 --settle=50`
- 숙련·진화: 스킬 JSON 의 `mastery.evolution`. 진화 확인은 `--mastery=sk_moon_crescent:29.5 --press=skill_1 --settle=40`. 판정 규칙 검사는 `tools/godot.sh --headless -- --selftest`
- 전망 지점은 지도 meta `viewpoints` (pitch·distance·look_ahead·look_up), 환경음은 `data/world/ambience.json` + `python tools/synth_ambience.py` (자연 소리는 Kenney 에 없어 합성). 끄기 `--no=ambience`
- 세이브는 `user://save.json`. 캡처(`--capture`) 실행은 세이브를 읽지도 쓰지도 않는다
- 모습(M6): `data/player/appearance.json` — 바탕 원화 4종 + 옷 색. 확인은 `--look=warrior:1` (모습 고르기 화면은 `--creator`).
  옷 색 범위는 `python tools/palette.py <시트> --mark out.png 70,150,0.25[,명도]` 로 옷만 칠해지는지 보고 정한다
- 새 주인공 바탕을 만들 때: hero 와 같은 설정(v3, 64px, high top-down, selective outline, high detail)과 같은 동작 프롬프트·칸 수를 쓴다 (전투 판정 칸이 전역이라)
- 작은 소품: 한 장에 격자로 뽑아 `tools/assets.json` 의 `{"object": id, "cols", "rows", "cells": [...]}` 로 잘라 쓴다 (생성 1회 = 소품 6~16개)
  - 격자는 `create_map_object`(1회)로. Pro Flash 는 격자 지시를 무시하고 하나만 그린다. 배경판을 칠해 오면 `"clear_bg": true`
- 나무·건물: `create_object_pro_flash` + 기존 그림을 `style_image` 로 (한 장 6회, 긴 변 208px 넘으면 9회). 참고 그림이 캔버스보다 크면 거절된다
  - 같은 종류의 그림 여러 장은 `<종류>_<번호>.png` — 불러올 때 아틀라스로 이어 붙이고 인스턴스마다 무작위로 고른다 (`World/PropBuilder`)
- 주의: PowerShell 은 스크립트 인자의 맨 `--` 를 삼킨다. Godot 실행은 Bash 의 `tools/godot.sh` 로.
