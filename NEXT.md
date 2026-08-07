# 다음 세션에서 이어서 할 일

## 지금 상태 (2026-08-07)

게임은 정상 동작. git 커밋 34개, 마지막 `a400eb8`.
플레이어는 이미 8방향 스프라이트로 교체 완료 (`art/player/idle`, `art/player/walk`).

## 막혀 있던 것

PixelLab MCP 가 **옛 API 키로 연결된 채** 세션이 시작돼서 401 이 났다.
- 새 키는 유효함 — REST 직접 호출로 확인 완료
- `~/.claude.json` 설정 정상, 중복 서버(`pixellab-forge-mcp`)도 정리됨
- `/mcp reconnect` 는 이 앱 화면에서 사용 불가였음 → **재시작이 유일한 해결책**

### 확인된 사실 (재조사 불필요)
- **구독 generation 있음.** REST 호출 응답에 `{"type":"generations","generations":1.0}`.
  `/v1/balance` 의 `{"type":"usd","usd":0.0}` 은 크레딧 잔액일 뿐 — **충전 불필요**.
- **REST 우회는 실패.** 공개 API 는 8개 엔드포인트뿐이고 캐릭터 ID 기반 애니메이션이 없다.
  `/animate-with-text` 로 테스트했으나(1 generation 소모):
  - 64x64 상한이라 104px 캐릭터를 잘라 넣어야 함
  - 결과물이 정체성을 잃음 — 배낭 사라지고 머리 형태·비율·색이 전부 달라짐
  - **이 경로는 쓰지 말 것.** MCP `animate_character` (저장된 캐릭터의 8방향을
    레퍼런스로 쓰는 템플릿/v3 파이프라인) 를 써야 기존 idle/walk 과 일관성이 유지된다.

## 할 일 — PixelLab 모션 6종 생성 후 게임에 붙이기

캐릭터: `fe3099a0-dbf9-48d2-8e42-6af93e94d635`
사양: 104x104, 8방향, low top-down, template `mannequin`
프롬프트(동일 캐릭터 유지용):
> A novice adventurer in a clean white t-shirt and loose black shorts, standing in a
> confident, ready-to-explore stance, simple athletic sneakers, clear silhouette with
> crisp black outlines.

| 모션 | 방식 | 붙을 애니 상태 |
|---|---|---|
| running | 템플릿 `running-8-frames` | `run` |
| dash | 템플릿 `running-slide` | `dash` |
| slash(베기) | v3 커스텀 `slashing with a sword` | `attack_windup/active/recovery` |
| heavy(강타) | 템플릿 `surprise-uppercut` | `heavy_windup/active/recovery` |
| guard(막기) | v3 커스텀 `raising shield to block` | `guard_hold` |
| shout(포효) | v3 커스텀 `shouting with head back` | 함성 발동 시 |

총 ~48 generations (104px 라 v3 는 방향당 2 일 수 있어 최대 70 예상).

### 진행 순서 (중요)
1. `get_balance` 로 구독 generation 잔액 확인
2. **south 1방향만** 먼저 생성 → 기존 idle/walk 과 톤이 맞는지 사용자에게 보여주고 승인받기
3. 승인 후 나머지 7방향 + 5개 모션
4. `art/player/{상태}/{방향}/frame_NNN.png` 로 배치
5. `PlayerSprite.cs` 에 상태별 텍스처 로딩 추가 (지금은 idle/walk 만 로드)
6. 게임 실행해서 검증 + 커밋

한 번에 전부 태우지 말 것. 1방향 확인 → 승인 → 확장.

## 관련 파일
- `scripts/Entities/PlayerSprite.cs` — 8방향 스프라이트 렌더러 (여기에 상태 추가)
- `scripts/Runtime/AnimationStateMachine.cs` — 상태머신 (13개 상태 이미 정의됨)
- `data/animations.json` — 상태 메타데이터
- `art/player/metadata.json` — PixelLab 원본 메타데이터

## 프로젝트 규칙
`CLAUDE.md` 참고. 특히:
- 수치는 코드에 하드코딩 금지, `data/**.json` + 로더
- 전투 수치는 `scripts/Combat/CombatTuning.cs` 한 곳에
- 커밋은 마일스톤 단위, 빌드 통과 필수
