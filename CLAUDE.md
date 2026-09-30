# PixelMMO

Godot 4.7 (.NET/C#) 액션 RPG. **저해상도 3D 디오라마 + 픽셀 스프라이트** (A Short Hike / HD-2D 계열).
2026-09-28 전면 재시작. 설계·조사 근거는 `docs/design/REBUILD.md`. 이전 버전은 `archive/astra-3d` 브랜치.

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
- 지도: `python tools/mapgen/mapgen.py` → `data/world/*` + `docs/design/map_preview.png`
- 에셋: `tools/assets.json` 에 PixelLab id 를 적고 `python tools/fetch_assets.py`
- 화면 확인: `tools/godot.sh -- --capture --shot=x,z,시각[,피치,거리] --out=user://shots`
- 성능: `... --capture --novsync --hold=3 --shot=...` (장면마다 프레임 시간), `--no=grass,props,hero,monsters,...` 로 A/B
- 동작 확인: `--press=attack --settle=12` (누르고 12프레임 뒤), `--react=guard --after=7` (적 공격 예고 끝에 막기 → 패링)
- 캐릭터 그림: `tools/assets.json` 에 `{"sheet": ..., "char": id, "anim": "PixelLab 애니 이름"}` → 원본은 `art_src/`, 시트는 `art/characters/<캐릭터>/<동작>.png`
- 스킬: `data/skills/*.json` (이벤트 목록, `Combat/SkillRunner` 가 해석), 칸 구성 `data/player/loadout.json`. 확인은 `--press=skill_1 --settle=10`
- 숙련·진화: 스킬 JSON 의 `mastery.evolution`. 진화 확인은 `--mastery=sk_moon_crescent:29.5 --press=skill_1 --settle=40`. 판정 규칙 검사는 `tools/godot.sh --headless -- --selftest`
- 세이브는 `user://save.json`. 캡처(`--capture`) 실행은 세이브를 읽지도 쓰지도 않는다
- 작은 소품: 한 장에 격자로 뽑아 `tools/assets.json` 의 `{"object": id, "cols", "rows", "cells": [...]}` 로 잘라 쓴다 (생성 1회 = 소품 6~16개)
- 주의: PowerShell 은 스크립트 인자의 맨 `--` 를 삼킨다. Godot 실행은 Bash 의 `tools/godot.sh` 로.
