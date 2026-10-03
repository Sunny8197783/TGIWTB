# 전면 재시작 설계 (2026-09-28)

## 왜 다시 시작하나 — 이전(astra 3D) 버전의 문제
| 문제 | 원인 | 이번 해법 |
|---|---|---|
| 조잡해 보임 | 고해상도 3D 에 저해상도 스프라이트를 섞음, 평평한 단색 땅 | 3D 를 640x360 으로 그려 스프라이트와 같은 픽셀 밀도로 맞춤 |
| 맵 반복감 | 같은 나무 몇 종 복사, 같은 타일 반복 | 지면은 좌표 해시로 그려 반복 없음, 구역마다 수종·색·지형이 다름 |
| 방향마다 캐릭터가 다름 | 방향별로 따로 생성 | PixelLab v3 8방향 한 번에 생성 + skeleton-v3 애니메이션(정체성 유지) |
| 시점이 멀어 몰입 안 됨 | 캐릭터가 화면 높이의 3.5% | 캐릭터 약 60px / 360px = **17%** (5배 가까움) |
| 커스터마이징이 얕음 | 한 원화의 길이·색만 바꿈 | (후속) 체형·머리·의상이 다른 원화를 따로 생성해 조합 |

## 화면 방향: "픽셀 디오라마"
- A Short Hike 는 3D 를 저해상도 텍스처에 그린 뒤 확대해 픽셀아트처럼 보이게 한다. [1]
- t3ssel8r 방식: 카메라를 텍셀 격자에 스냅하고 남은 소수점만큼 화면을 밀어 픽셀이 기어다니지 않게 한다. 툰 조명·구름 그림자·풀 빌보드. [2][3]
- Octopath(HD-2D): 틸트시프트 피사계심도·블룸·동적 조명으로 디오라마 느낌. [4]
- 이 노트북 벤치마크(같은 장면, 그림자·블룸·안개·물·입자 전부): Forward+ 크래시 / **Mobile 640x360 = 15.9ms** / 1280x720 = 27ms → Mobile + 저해상도 확정.

## 맵: 하루미 섬 (224x224m)
카메라가 북쪽을 보므로 남→북으로 걸을수록 풍경이 한 겹씩 열리게 짰다.
마을(남) → 거울 호수(중앙, 섬 정자·석등·수련) → **폭포**(남향 절벽, 호수로 떨어짐) → **벚꽃 고원**(북서, 강·신사) → 단풍 언덕 → **등대 곶**(북동, 바다 절벽 전망).
동쪽 풍차 들판, 서쪽 속삭이는 숲, 남쪽 해변. 구성 원칙은 BotW 의 삼각형 지형·랜드마크 길잡이. [9]
- 하루 16분. 해는 동북동에서 떠 남쪽을 지나 서북서 바다로 진다 → 노을이 화면 위쪽 수평선에 걸린다.
- 밤: 달빛 방향광, 별, 호수 위 달빛 윤슬, 등불 발광.

## 전투 손맛 조사 요약 → 우리 수치
- **히트스톱**: 칼이 맞으면 3~5프레임 멈춰 뇌가 충격을 등록할 시간을 준다. 강타는 60~90ms. [5]
- **화면 흔들림은 방향성으로**: 무작위 떨림 대신 맞은 힘의 방향으로 밀렸다 돌아온다. [5]
- **파편은 타격 방향으로** 튄다 — 히트스톱·흔들림·파편이 같은 프레임에 맞물려야 한다. [5]
- **패링 vs 막기 (Sekiro)**: 막기는 약한 주황 불꽃·작은 금속음, 패링은 큰 불꽃 다발·크고 높은 금속음. 화면을 안 봐도 소리만으로 구분된다. [6]
- **완벽 회피 슬로우 (Bayonetta Witch Time)**: 마지막 순간 회피에만 발동, **적만 느려지고 나는 정상 속도**, 그 동안 모든 행동이 가능해야 한다(자동 연출 X). [7] 명조도 약 1초 슬로우. [8]
- **효과음은 층으로**: 휘두름(바람) + 재질 긁힘 + 맞는 소리(짧은 어택) + 강화음. 각 층은 한 가지 역할만, 매번 음높이를 조금씩 바꾼다. [10][11]

## 전투 구현 메모
- 수치는 전부 `scripts/Combat/CombatTuning.cs` (프레임 수 주석). 연출은 `CombatFx` 한곳.
- 시간: `GameRoot.HitStop` 은 모두 멈춤, `SlowWorld` 는 적·물·입자만 (주인공은 `HeroScale`).
- 원화는 준비 동작이 길어 `PlayKeyed` 로 "칼이 닿는 칸"을 판정 순간에 맞춘다 (AttackStep.HitFrame).
- 효과음: Kenney CC0 (`audio/KENNEY_LICENSE.txt`) + 바람 소리는 `tools/synth_sfx.py` 로 합성.
- 지형 충돌체는 쓰지 않는다: 캐릭터는 높이맵을 읽어 발을 붙이고(`WorldData.ConstrainMove`) 물리는 건물·나무만.
  높이맵 충돌은 몸통 하나에 0.3ms 였다. 물리 엔진은 Jolt.

## 스킬 이펙트 조사 요약
- 베기: 고리 메시 위로 삼각 마스크를 쓸어 넘기고, 잡음 텍스처로 속을 채우고, 그라데이션으로 색, 블룸으로 발광. 파라미터 하나(0→1)로 드러났다 사라진다. [12]
- 타격 순간 방향성 불꽃을 곁들인다. [13]
- 우리 화면에서는 이 셰이더를 640x360 으로 그리므로 자동으로 픽셀 이펙트가 된다.

## 스킬 이펙트 구현 (M5)
- 스킬 = `data/skills/*.json` 의 "몇 초에 무엇을" 이벤트 목록. 해석기는 `Combat/SkillRunner` 하나 (규칙 1).
  이벤트: sfx · slash(horizontal/reverse/vertical/spin) · ring(바닥 충격파) · sparkle · petals · lightning · hit(arc/circle) · dash · flash · shake · slow
- 모든 이펙트는 4단 색 램프(흰 심 → 밝은 → 중간 → 짙은, `CombatFx.Palettes`)로만 칠하고 흰 심만 글로우 문턱을 넘긴다 → 픽셀아트답게 또렷.
- 3박자: 예비(칼 당기는 소리·전기 모이는 반짝이) → 충돌(히트스톱·섬광·고리·판정 한 프레임) → 흩어짐(두 번째 고리·반짝이·꽃잎).
- 스킬 3종: 월광참(회전 베기+달빛 고리), 벚꽃 난무(돌진 3연 베기+X 마무리+꽃잎), 뇌격(내려찍기+번개 두 줄기+충격파).
- 키: Q·E·R (또는 U·I·O), 패드 Y·RB·RT. 칸 구성은 `data/player/loadout.json`.
- 회전 베기는 새 그림 없이 8방향 그림을 차례로 넘긴다(젤다식, `spin` 이벤트) — PixelLab v3 는 몸을 돌리지 못했다.
- 숙련·진화·히든 (`Combat/Mastery`, archive 에서 이식):
  - 유효한 시전 = 산 적에게 맞은 시전, 한 시전에 한 번 (규칙 3). 최소 간격 미만 연타 0, 같은 적만 치면 6회차부터 반감, 끝내 0.
  - 숙련 100 당 피해 +30%. 진화 임계값(30·30·40)을 넘는 순간 칸이 진화형으로 바뀌고 숙련·대기시간을 넘긴다:
    월광참→만월참(두 바퀴), 벚꽃 난무→벚꽃 폭풍(8m 5연+X+내려베기), 뇌격→뇌신강림(번개 다섯 줄기).
  - 스킬 칸 아래 한 줄이 진화까지 차오른다. 진화·습득 순간 화면 위에 한 줄 알림 (조건은 말하지 않는다).
  - 히든 패시브 2종 (조건은 data/skills 에만, 규칙 4): 판정 창을 넓힌다. 사건 이름은 perfect_dodge · parry.
  - 세이브: `user://save.json` (숙련·히든 횟수·습득·칸). 캡처 실행은 읽지도 쓰지도 않는다. 깨진 세이브는 save.broken.json 으로 옮긴다.
  - 한글 글자: 윈도우 굴림 12px 비트맵을 안티앨리어싱 없이 키운다 (다른 OS 로 가면 OFL 픽셀 한글 글꼴을 넣는다).
- 성능 (10-03 다시 잼): 그림자가 GPU 를 7ms 먹고 있었다 → 그림자 지도 4096→2048, 지형은 그림자를 드리우지 않음(해가 남쪽·카메라가 북쪽이라 언덕 그림자는 어차피 안 보인다) → 중앙 15~17ms → 7~9ms. 전투 중 70~90ms 끊김은 매 프레임 StringName 생성이 부른 GC 였다(Render/Uniform 로 해결, 30초 GC 2→0). 캡처를 연달아 돌리면 노트북이 달아올라 같은 장면도 14→23ms 로 흔들리니 비교는 번갈아 잰다.

## 단계
- [x] M1 월드 기반: 픽셀 뷰·스냅 카메라, 지형·물·폭포·풀·하늘·낮밤, 소품 배치, 60fps
- [x] M2 주인공: 8방향 이동·달리기·돌진 회피(잔상), 근접 카메라(앞보기), 발밑 그림자, 발소리·먼지
- [x] M3 전투: 3연타(질주 공격·입력 버퍼·회피 캔슬·회피 반격), 막기/패링(0.15s), 완벽 회피 슬로우(적만 0.2배 2.4초, 세계 보랏빛 무채색),
      히트스톱·방향 킥·충격량 흔들림·불꽃·임팩트 별·초승달 궤적·효과음 4층, 벚꽃 슬라임, HUD(체력·피해 숫자)
      숲 고블린(내려찍기 예고 원, 걷기·내려찍기·피격·쓰러짐 8방향), 슬라임 피격·녹아내림 8방향. 적은 쓰러진 채 1초 남았다 깜빡이며 사라진다
- [ ] M4 맵 채우기: PixelLab 건물·소품 전량, 밤 등불, 전망 카메라, 환경음
- [x] M5 스킬 이펙트 시스템(데이터 구동) — 스킬 3종 + 진화형 3종, HUD 칸, 숙련·진화·히든 패시브·세이브. (스킬 전용 주인공 동작은 필요할 때)
- [x] M6 캐릭터 커스터마이징 — 바탕 원화 4종(검사·여검사·전사·나그네, 체형·머리·옷이 다름, 각 8동작 8방향) × 옷 색 7가지.
      옷 색은 불러올 때 색상 범위 픽셀의 색상만 옮긴다(`Render/Recolor`, 명도 유지). 머리 색은 원화 안에서 가죽과 같은 색을 써서 못 뗀다.
      모습 고르기: 처음 켤 때·C 키. 미리보기는 월드의 주인공이 제자리에서 돈다. 세이브에 남는다.

## 출처
1. A Short Hike 픽셀 카메라 — https://www.youtube.com/watch?v=L-tNbbov6Bo , https://en.wikipedia.org/wiki/A_Short_Hike
2. 3D Pixel Art Rendering (Godot) — https://www.davidhol.land/articles/3d-pixel-art-rendering/
3. t3ssel8r 방식 재현 논의 — https://discussions.unity.com/t/recreating-t3ssel8rs-3d-pixel-art/928878
4. HD-2D — https://en.wikipedia.org/wiki/HD-2D , https://www.unrealengine.com/en-US/developer-interviews/octopath-traveler-ii-builds-a-bigger-bolder-world-in-its-stunning-hd-2d-style
5. Game feel (hitstop/shake/particles) — https://salivity.github.io/game-development/article/maximizing-game-feel-in-action-game-development , https://github.com/thaenor/neo-city/issues/17
6. Sekiro 패링 해부 — https://medium.com/@gatherer286/song-of-sword-and-fist-sifu-sekiro-and-the-anatomy-of-a-perfect-parry-2f9c4c26867a
7. Witch Time — https://parryeverything.com/2022/10/31/bayonettas-witch-time-is-better-than-most-of-its-derivatives/
8. 명조 완벽 회피 — https://game8.co/games/Wuthering-Waves/archives/456639
9. BotW 지형 구성 — https://www.blog.radiator.debacle.us/2017/10/open-world-level-design-spatial.html , https://book.leveldesignbook.com/process/blockout/massing/composition
10. 검 효과음 4층 — https://www.daviddumaisaudio.com/the-4-secret-layers-behind-epic-sword-sound-effects/
11. 타격음 층 쌓기 — https://pixflow.net/blog/punch-impact-sound-effects-for-fight-scenes/
12. 베기 셰이더 — https://www.cyanilux.com/tutorials/sword-slash-shader-breakdown/
13. 베기 VFX — https://realtimevfx.com/t/sword-slash-help/9407
