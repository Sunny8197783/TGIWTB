# 확인된 작업 상태

갱신일: 2026-09-28

## 진입점과 주요 심볼
- Godot .NET: `scripts/Runtime/Boot.cs` → `GameWorld` → `ReferenceWorld3D` / `ReferenceTerrain3D`. 기본은 원근 3D, `--flat`은 기존 2D.
- `WorldLayout`: 400×288 연속 숲, 작은 마을·고블린 부락·별도 성소. `TownGenerator`는 12채와 문 앞 접근로를 배치한다.
- `TileWorld.Paint/ForestProps/PaintSanctuary`: 유기적 외곽, 숲길·개울·다리 4곳, 실내 석재 바닥. `PlayerHud.DrawMinimap`은 실내 지도를 전환한다.
- `MeadowLayout` / `MeadowEncounters`: `data/encounters/meadow.json`의 20거점과 13경로; 동쪽 6거점은 단일 종 개체별 지속 재생성. `ForestSecrets`는 조사·문·귀환·샛길 상태를 저장한다.
- `PlayerCharacter.SpriteClip` → `CombatMotion.Progress`: 검 일반/강공격 접촉 프레임 동기화. `ActorArt.FrameFor`는 고블린 8방향 보행·공격을 제공한다.
- `SpriteCollision3D.Shapes/Set`: 건물·몬스터의 불투명 윤곽을 공유. F2로 표시. 나무는 보이는 줄기만 이동을 막는다.
- `SpriteCollision3D.CleanContour` / `SpriteContourChecks`: 북동 고블린 attack frame_005의 직선 되접힘을 정리한다. `--reproduce-raw-contour`는 과거 오류를 의도적으로 재현하는 별도 명령이다.
- `CharacterCreator` → `IPlayerContext.ApplyAppearance` → `CharacterArt` / `PixelHeroArt` / `PlayerSprite`: 기본은 PixelLab 남녀 검객, 체형 6·머리색 30·피부색 12. 기존 헤어/눈 60조합은 별도 파츠 모드로 보존한다. 모드·외형 저장과 C키 편집을 지원한다.
- `CombatCollision.Sweep/ClearSight`: 발사체·근접의 벽 차단. `GestureSkillInput`은 우클릭 드래그를 기존 스킬 입력에 연결한다.
- 스킬 연출: `CombatFeedback` → `ReferenceWorld3D.Ring/Impact`. 베기 JSON의 `effect`를 사용하며, 궤적·명중 불꽃·충격파는 자동 소멸한다. `GameCamera`의 감쇠 진동을 3D 투영 오프셋에 적용한다. F3의 `shake.strength`를 0으로 두면 흔들림이 꺼진다.

- `RpgUi` / `PlayerHud`: 황동·목재색 HUD, P 캐릭터 기록, B 기술/숙련 창. `GameWorld.BuildDialogueBox`는 같은 테마의 NPC 대화창. UI 상태는 `IPlayerContext`를 통해 읽으며 열린 창은 배경 입력을 막는다.
- 스태미나: `CombatTuning.Stamina*`, `PlayerCharacter.StartDash/TickTimers`. 회피 24 소모, 0.5초 후 초당 20 회복. `SaveData.Stamina`는 이전 저장에서 빠져 있으면 최대값으로 복원한다.

## 결정과 이유
- 이동 원본의 정면 회전/대기 자세 혼입을 방향별 템플릿으로 교체하고 `PixelHeroArt`에서 상체 전방 기울기·의상 색을 보정했다. 동쪽 숲은 슬라임 습지/고블린 주거·작업·경계 구역, 새 버드나무 원본 `819528bd-44e1-4262-93a0-6dddee4389cb`. 강변은 곡선 수면/충돌과 1/4타일 지형·동일 `HeightAt`을 공유한다. 작업 전 백업 `%TEMP%/TGIWTB-before-forest-motion-20260915.zip`.
- 사용자가 이전 3D 느낌 복원과 숲속 마을 전면 교체를 승인했다. 기존 13지역은 활성 월드에서 제거했다. 이번 아트·음향 요청은 P1 문서의 제외 범위보다 우선한다.
- 표본은 dev_in_airforce의 최근 엔진 변경 화면. 출처·설계·화면은 `docs/review/forest-rebuild.md`에 기록했다.
- 새 의존성 없이 Godot 네이티브 지형·물리와 기존 전투/숙련 모델을 재사용했다. 플레이어 상태는 `IPlayerContext`로 접근한다.
- 8방향 이동은 키 입력, 마우스 조준은 좌클릭 공격 중, 시선 고정은 Tab. 숨겨진 2D 몸체의 중복 이동 충돌은 끈다.
- PixelLab 원본 플레이어 `fe3099a0-dbf9-48d2-8e42-6af93e94d635`, 검 상태 `43627efc-a016-4c1f-81c4-21d3a04b4d60`, 고블린 `e45f7640-1df3-44c7-9cd7-fb759c4d52c6`. 다운로드 목록은 `docs/pixellab-assets.json`.
- `WarmGoblinArt`는 전투 전에 프레임과 윤곽을 준비한다. 실제 캡처의 PNG 저장 시간은 전투 프레임 지연과 구분한다.
- `PlayerSprite.WarmFrames`는 외형 확정/시작 시 남녀 8방향을 준비한다. 걷기↔달리기 위상 보존, `PlayerCharacter`의 타격 정지 중 입력 보존과 공통 전투 초기화, 동일 지형 높이 캐시/비활성 몬스터 중복 갱신 제거를 적용했다.
- `SaveSystem`은 이전 월드 위치만 새 마을로 이동하고 성장 데이터를 유지한다. 실제 저장 교체 전 `.before-forest` 백업, 임시 파일 작성 후 교체. 캡처/검증은 사용자 저장을 쓰지 않는다.
- 작업 전부터 큰 변경 묶음이 있었다. 전체 변경을 이번 결과로 간주하거나 일괄 되돌리지 않는다. 숲 개편 전 백업: `%TEMP%/TGIWTB-before-forest-rebuild-20260908.zip`.

## 검증 명령과 결과
- 2026-09-28 최종 `tools/verify_forest.ps1` 전체 통과: 빌드 경고/오류 0, reference/combat 실패 0, selfcheck 정상. 7,068 윤곽 표본 불일치 0. 새 남녀 모션 준비 검사에서 발견한 외형 복사본의 캐시 무효화를 값 비교로 수정한 뒤 재검증했다.
- 2026-09-27 `--hunt-check` 30분 실제 입력 자동 플레이 PASS: 슬라임 311/고블린 91, 자연 사망 5, 첫 진화 657초, 마을 귀환·저장 및 별도 `--hunt-resume` 상태 복원 PASS. 전용 `hunt-check.json`을 사용하며 사용자 저장 SHA256 동일. 근거 `docs/review/hunt-polish.md`와 `hunt-soak-final.log`.
- 성능은 `--capture-no-images --capture-seconds=45`로 준비 후 15초 제외, `tools/benchmark_hunt.ps1`의 동일 세 지점으로 비교한다. 30분 입력 플레이 중앙값 18.40ms/p95 39.12ms로 안정적 60fps는 미달. 최초 `hunt-before.log`에는 로딩이 섞여 개선율 근거로 쓰지 않는다.
- 최종 고정 시점 p95(ms): 마을 33.36→28.48, 슬라임 34.95→35.93, 고블린 80.06→36.24. 각 1회/AI 변동이 있어 전 지역 개선을 단정하지 않는다. 같은 이동 경로의 전후 비교와 네이티브 물리/다른 물리 콜백 비용 분리 측정이 남았다. 수치·제한은 `docs/review/hunt-polish.md`.
- 2026-09-16 `tools/verify_forest.ps1` 전체 통과: 빌드 경고/오류 0, reference/combat/selfcheck PASS. 확장 경로·강변 3,998표본, 연결 59,650칸, 윤곽 7,068표본 불일치 0. 남녀 이동의 누락·잘림·정지, 단일 종·개체 재생성·순찰·연못 차단 회귀 검사 포함. 근거 `docs/review/forest-motion.md`.
- `tools/verify_forest.ps1`: 빌드, Godot 임포트, `--reference-check`, `--combat-check`, `--selfcheck`. 로그는 `docs/review/forest-*.log`.
- 2026-09-13 새 아트 연결 후 `verify_forest.ps1` 통과. 빌드 경고 0/오류 0, 임포트·지도·전투 검사 오류 0, 데이터 직업 8/스킬 28/NPC 16 정상. PixelLab 560프레임·색상·체형·방향별 접촉·저장 회귀 검사 포함. 화면과 근거는 `docs/review/hero-polish.md`.
- `reference-check`에 `CustomizationChecks`와 윤곽 정밀도 회귀 검사를 포함한다. 윤곽 7,068표본 차이 0, 남녀별 실제 이미지 고유값 60/60/6. 자세한 근거·화면·재현과 정상 로그 구분은 `docs/review/character-creator.md`.
- 지도 검증: 마을 길 259칸의 막힘 0, 경로·보급품 3,018표본, 연결 가능 40,093칸. 8방향·윤곽 명중/빗나감·경사 접지·발사체·성소 입장/귀환 포함.
- `python tools/check_sprite_consistency.py sword sword_walk sword_heavy`: 세 클립 통과. 실제 전투 캡처에서 처치·피격·숙련 상승 확인.
- 스킬 연출 검증: `docs/review/skill-feedback.md`. 빌드·3D 투영 흔들림/복귀·효과 생성/소멸·전투·데이터 검사 통과. `CombatChecks.Finish`는 테스트 월드를 해제한 뒤 종료해 .NET 리소스 종료 오류를 방지한다.
- Godot 실행 파일은 Downloads의 `Godot_v4.7.1-stable_mono_win64/Godot_v4.7.1-stable_mono_win64/Godot_v4.7.1-stable_mono_win64_console.exe`.

## 남은 문제와 다음 행동
- 새 아트: 남성 `9cd50f46-be01-49fb-9cac-897bc5504cd0`, 여성 `68a5ca29-1089-4555-8a31-e83fc4cb4035`. 공격 `hero_slash_fixed` / `hero_heavy_fixed` 9프레임, 보행 `hero_stride` / 전력질주 `hero_sprint` 8프레임. 기존 목록 `tools/hero_downloads.json`, 새 이동 목록 `tools/movement_downloads.json`; 새 공격을 만들면 `PixelHeroArt.ContactFrame`의 방향별 접촉값을 소스 시트로 다시 검토한다. 백업 `%TEMP%/TGIWTB-before-hero-polish-20260910.zip`.
- 30분 자동 입력 검 전투의 성장 목표는 충족했으나 사람의 재미 평가는 별도 필요하다. 슬라임 피해 4/예고 0.42초, 고블린 및 진화 기준 300 유지. 모든 직업의 전용 무기 모션은 아직 없다. 검 방어/피격은 무장 대기 이미지와 기존 반응 연출을 재사용한다.
- PixelLab 검객의 헤어·눈 모양은 원화 고정이다. 기존 60종은 파츠 조합이며 독립 원화 60장이 아니다. 색상 마스크는 현재 의상에 맞춰져 있어 새로운 의상에는 별도 마스크 검토가 필요하다. 원작 영상과 정확히 일치한다고 검증한 자산은 아니다.
- 지형은 좌표당 높이 하나인 높이장이다. 윤곽은 고정 카메라 기준 1px 단순화 형상이다. 자유 회전·다층 실내에는 별도 모델이 필요하다.
- 최신 맵/UI 근거: `docs/review/rpg-world-ui.md`. 숲 개편과 전투 연출 기록은 이전 보고서를 참조한다. UI 교체 전 백업은 `%TEMP%/TGIWTB-before-rpg-ui-world-20260909.zip`.
