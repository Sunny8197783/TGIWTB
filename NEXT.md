# 다음 세션 인수인계

## 지금 상태

게임 정상 동작. 작업 트리 깨끗. 실행은 바탕화면 `PixelMMO 실행.lnk` 또는
`godot --path . --resolution 1280x720`.

P1(전투 프로토타입) 완료 → P2 진행 중. 프로젝트 규칙은 `CLAUDE.md`.

## 캐릭터 스프라이트 (PixelLab)

캐릭터 `fe3099a0-dbf9-48d2-8e42-6af93e94d635`
사양: 104x104, 8방향, low top-down, template `mannequin`
프롬프트(동일 캐릭터 유지):
> A novice adventurer in a clean white t-shirt and loose black shorts, standing in a
> confident, ready-to-explore stance, simple athletic sneakers, clear silhouette with
> crisp black outlines.

`art/player/{클립}/{방향}/frame_NNN.png` 규칙. `PlayerSprite.cs` 가 자동 로드하고,
폴더가 없으면 idle 로 폴백한다. 클립 추가는 `ClipNames` 에 이름 한 줄.

| 클립 | 프레임 | 루프 | 생성 방식 |
|---|---|---|---|
| breathe | 4 | 순환 | `breathing-idle` 템플릿 |
| walk | 8 | 순환 | v3 (원본, 문제 없음) |
| run | 8 | 순환(2~7) | v3 전력질주 |
| dash | 6 | 단발 | `running-slide` 템플릿 |
| punch | 6 | 단발 | v3 스트레이트 |
| heavy | 10 | 단발 | v3 오버헤드 스매시 |
| guard | 4 | 순환(2~3) | v3 방어 자세 유지 |
| block | 6 | 단발 | v3 피격 반동 |
| shout | 6 | 단발 | `fireball` 템플릿 |

`animation_group_id` 는 `scratchpad/groups.md` 가 아니라 PixelLab 쪽에만 남는다 —
`get_character` 로 `[group: ...]` 를 다시 읽으면 된다.

### 생성할 때 알아둘 것 (비싸게 배운 것)

- **없는 소품을 프롬프트에 넣지 말 것.** "방패를 들고 막는다" 같이 캐릭터에 없는
  물건을 요구하면 모델이 방향마다 다르게 상상해서 색·모양이 제각각이 된다.
  몸동작만 구체적으로 서술하고 `no weapon, no shield, no object held` 를 붙인다.
- **템플릿 모드**는 하나의 스켈레톤에서 8방향이 나와 일관되지만 표현이 밋밋하다.
  **v3 커스텀**은 표현이 좋지만 방향마다 독립 생성된다 — 소품만 없으면 충분히 일관됐다.
- 동시 작업 슬롯 8개. 한 번에 한 애니메이션(8방향)만 큐에 들어간다.
- 결과 수령은 `https://api.pixellab.ai/mcp/characters/{id}/download` (zip).
  전 작업이 끝날 때까지 HTTP 423 이므로 `curl --fail` 로 폴링한다.
  zip 안 폴더 구조가 우리 규칙과 같아서 그대로 복사하면 된다.
- 공개 REST API(`/v1/animate-with-text`)는 **쓰지 말 것** — 64x64 상한이고
  캐릭터 정체성을 잃는다. 반드시 MCP `animate_character` 를 쓴다.
- **`pro` 모드는 이 캐릭터에 못 쓴다.** 완성된 방향을 참조로 다음 방향을 뽑아 주는
  유일한 모드지만, 64px 초과 캐릭터는 **프레임 수가 4로 고정**된다(104x104 이므로 해당).
  비용도 방향당 20~40 generation.
- `frame_count` 는 **짝수만** 된다(4~16).
- `keep_first_frame=false` 로 해야 요청한 프레임 수가 그대로 나온다(true 면 +1).
- **순환 클립은 앞 1~2프레임이 '서 있다가 자세 잡는' 구간이 된다.** v3 가 항상 idle
  회전 이미지에서 출발하기 때문이고, 프롬프트로는 못 고친다. `PlayerSprite.LoopStart`
  에 루프 시작 프레임을 넣어 진입 구간은 한 번만 지나가게 처리했다.
- 방향은 **남 → 북 → 동/서 → 대각선** 순으로 나눠 걸고, 같은 프롬프트·같은
  `animation_group_id` 로 이어붙인다. 참조 생성은 안 되지만, 남쪽을 먼저 보고
  통과시킨 뒤 나머지를 거는 관문이 생겨서 잘못된 프롬프트에 8방향을 다 태우지 않는다.

### 도구

- `python tools/pull_sprites.py <character_id> <출력폴더> Sprint=run Punch=punch ...`
  완성 zip 을 받아 `{클립}/{방향}/frame_NNN.png` 로 펼친다. 인증은 MCP 설정에서
  읽고 **절대 출력하지 않는다**. 생성 중이면 HTTP 423.
- `python tools/contact_sheet.py <출력.png> art/player/<클립> [방향 ...]`
  프레임을 격자 한 장으로 합친다. 방향별로 훑어보며 눈으로 검수할 때 쓴다.

### 검사

`python tools/check_sprite_consistency.py` — 프레임 수 / 이펙트 오염 / 크기 편차.
색 팔레트 전체 비교는 오탐이라 넣지 않았다(정면은 얼굴, 뒷면은 배낭이 보이므로
방향마다 색 구성이 다른 게 정상). **동작이 자연스러운지는 결국 눈으로 봐야 한다.**

## 아직 안 한 것 / 다음 후보

- **칼·방패 State**: 무기를 든 별도 캐릭터 state 로 만들 예정
  (`create_character_state` 로 같은 캐릭터의 변형 생성). 지금은 맨손만.
- 몬스터 스프라이트 (슬라임/고블린/철턱은 아직 도형)
- 커스터마이즈 UI (색 슬롯은 스프라이트로 대체돼 현재 무력)
- 맵: 동굴 입구, 절벽

## 핵심 파일

- `scripts/Entities/PlayerSprite.cs` — 8방향 클립 렌더러 (클립별 프레임 간격, 홀드 클립)
- `scripts/Entities/PlayerCharacter.cs` — `SpriteClip()` 이 상태→클립 매핑
- `scripts/Runtime/AnimationStateMachine.cs` — 상태머신(관찰자, 시그널 4종)
- `data/animations.json` — 상태 메타데이터
- `scripts/Combat/CombatTuning.cs` — 전투 수치 전부 (F3 로 실시간 조정)
