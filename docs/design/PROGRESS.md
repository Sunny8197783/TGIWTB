# M9 진행 기록 (자동 재개용 — 새 세션은 이 파일부터 읽는다)

작업 폴더: `C:\Users\김태양\Desktop\TGIWTB`. 규칙·도구·캡처 옵션은 `CLAUDE.md` (맨 아래 «무명록 (M9)»), 설계는 `docs/design/NAMELESS.md`.
빌드: `export PATH="/c/Program Files/dotnet:$PATH"; dotnet build` → `tools/godot.sh --headless --import` → `tools/godot.sh -- --capture --shot=x,z,시각`.
파이썬: `/c/Users/김태양/AppData/Local/Programs/Python/Python312/python.exe` (numpy·Pillow 있음).
커밋·푸시는 사용자가 말할 때만. Bash 한 명령에 긴 heredoc·한글 든 작은따옴표 글을 넣으면 파싱이 깨진다 — 파일은 Write/Edit 로 쓴다.

## 사용자 요청 (2026-11-11, 원문 요지)
1. 신박하고 재밌는 RPG 소재 적용 (진짜 판타지, "드디어 찾던 RPG")  2. 몰입감 있게 플레이 방식 수정
3. 정말 자유로운 커스터마이징 (색·크기만 바뀌는 게 아니라 하나하나 조정)  4. 소재에 맞춘 지도 구조 (아래→위 일직선 탈피, 모험 느낌)
5. 몹 스폰율 적당히  6. 몹 종류 늘리기 (움직임 자연스럽게)  7. UI·화면 구성 출시 수준
8. 필요한 그림은 PixelLab 에서 새로 뽑기 (재사용 비추천)  9. 탄탄한 스토리 (메인 줄기 여럿 가능)
10. 직업 대규모 업데이트 대비 (NPC 기본 직업 → 다양한 트리, 플레이로 정해지는 직업, 기연·히든, 한 직업만 파도 강함, 평범하지 않은 직업)
11. 이펙트·타격감·몰입감 (직업마다 만족스러운 이펙트)  12. 모험 중 멋진 풍경
13. 초반에 함께하다 떠난 NPC 가 큰 위기에 돌아와 돕는 낭만 서사  14. 토큰은 개발에만  15. 실수·버그 없이
16. 한도에 닿으면 초기화 뒤 자동 재개

## 상태 (2026-11-11 16시 기준)
- [x] 환경·스킬·설계 (NAMELESS.md)
- [x] 시스템: 행적(Core/Deeds)·각인(EngraveDef)·이름(직업) 개편 — data/jobs 15개 (5개 열림 + 10개 봉인/히든)
- [x] 몬스터 AI 유형·적 투사체(EnemyShot)·밀도 스포너(data/world/spawns.json)
- [x] 무명지·이름비 (World/Hush) — 색 빠짐, 무리 싸움, 색이 번지는 복원 연출
- [x] 지도 재구성 (시작 나루, 절벽 끝 세계수, 뿌리 계단, 이름비 자리) + 미니맵·전체 지도 그림
- [x] 이야기 실행기(5줄기)·동행 나래·위기 연출 — 위기 연출은 `--crisis` 로 처음부터 끝까지 캡처 확인
- [x] UI: Ui 부품·Dialogue·Hud·Menus — 타이틀·멈춤·이야기·이름록·지도·조작법·설정 캡처 확인. 첫 화면 → 새로 시작 → 모습 → 여는 글 흐름도 `--boot --keys` 로 확인
- [x] 그림 (PixelLab): 몬스터 9종(hush_shade moss_boar petal_wisp stone_crab field_scarecrow maple_fox bark_golem frost_wolf hollow_stag),
      나래, NPC 9명 — 전부 받아 묶음. (maple_fox 의 move 는 한 번 누락돼 다시 냄 — `art/characters/maple_fox/move.png` 가 있는지 확인)
- [x] 모습 빚기(Forge): 원화 → 동작 9개 생성·묶기·갈아입기까지 실제 API 로 끝까지 확인 (시험으로 빚은 몸 `user://forge/53a19fa7-…`, 서약검·호명사로 전투 캡처)
- [x] 직업별 이펙트 (Combat/JobFx, 직업 JSON 의 "fx"): 서약검 금빛 메아리·인장, 묵영 먹 붓질·먹 튐·잔상, 풍문사 빛의 활·회오리, 호명사 부름 고리·울림 — 캡처 확인
- [x] 다듬기: 여신상·이름비 안내를 Npcs.Prompt 로 통일, 미니맵 지역 이름 잘림, 멈춤 판 크기, CLAUDE.md 갱신

## 남은 일 / 다음에 할 만한 것
- 봉인된 이름 10개(warden … lamplighter)의 실제 평타·스킬·전용 이펙트 (대규모 직업 업데이트)
- 보스(허)·이름비 싸움의 실제 플레이 밸런스 (캡처로는 연출만 확인)
- 자동 재개용 예약 작업 `tgiwtb-rpg-resume` 은 일이 끝나면 지운다

## 메모
- PC 가 절전에 들어가면 생성 대기가 멈춘다.
- 예전 PixelLab 계정과 같은 계정이다 (기존 그림 id 그대로 유효). 원화 2회, 동작은 방향당 1회(큰 몸은 더).
- 큰 몸(bark_golem 80px, hollow_stag 96px)은 시트 칸 192 (`assets.json` 의 `cell`, MonsterDef.Cell).
- 템플릿 동작(breathing-idle)은 zip 안에서 `breathing_idle` 폴더로 내려온다 (assets.json 의 anim).
- 캡처에서 '새로 시작'을 누르면 장면이 다시 불려 캡처도 처음부터 다시 돈다 (같은 이름의 그림을 덮어쓴다 — 버그 아님).
