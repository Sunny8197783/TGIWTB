# 기사 캐릭터 처음부터 만들기 — 단계별 가이드 (Godot 4, GDScript)

목표: 스프라이트 시트 한 장 → **WASD로 움직이고, 공격/방어하는 기사**.
아무 사전 지식 없이 따라올 수 있게, 에디터에서 클릭하는 것까지 적었습니다.

> 이미 `experiments/knight/` 에 Knight.tscn / PlayerKnight.gd 를 만들어 뒀습니다.
> 아래는 "그게 어떻게 만들어지는가"를 직접 이해하며 따라 하는 과정입니다.
> 직접 만들어 보고 싶으면 새 씬으로 처음부터, 빨리 보고 싶으면 **Phase 1 + Phase 4**만 하면 됩니다.

---

## Phase 0 — 큰 그림 (5줄 요약)
1. 이미지 배경을 **투명(알파)** 으로 지운다.
2. 씬에 **CharacterBody2D**(몸) + **AnimatedSprite2D**(그림) + **CollisionShape2D**(충돌) 를 놓는다.
3. 픽셀이 안 흐리게 **Nearest** 필터.
4. **SpriteFrames** 에 시트를 잘라 넣어 idle/walk/attack/guard 애니를 만든다.
5. **스크립트**로 입력→이동→애니를 연결한다.

---

## Phase 1 — 이미지 배경 투명하게 (가장 중요)
배경이 투명하지 않으면 캐릭터 주위에 네모 배경이 같이 나옵니다.

1. 무료 도구 **Photopea**(브라우저) 또는 Aseprite/GIMP 로 시트 이미지 열기.
2. **자동 선택(마술봉)** 으로 배경색 클릭 → `Delete` 로 지우기 (여러 칸이면 반복).
3. **PNG로 내보내기** → `experiments/knight/knight_sheet.png` 로 저장.
   - 빠른 확인만 할 거면 배경 안 지우고 넘어가도 됨 → Phase 8의 셰이더로 임시 처리.

---

## Phase 2 — 씬과 노드 만들기
1. Godot 상단 **Scene → New Scene**.
2. 왼쪽 Scene 독의 **"+ Other Node"** → **CharacterBody2D** 선택 → 루트로 생성.
3. 루트 이름을 더블클릭해 **Knight** 으로 변경.
4. Knight 을 우클릭 → **Add Child Node** → **AnimatedSprite2D** 추가.
5. Knight 을 우클릭 → **Add Child Node** → **CollisionShape2D** 추가.
6. **Ctrl+S** 로 `experiments/knight/Knight.tscn` 저장.

완성된 트리:
```
Knight (CharacterBody2D)
├── AnimatedSprite2D
└── CollisionShape2D
```

---

## Phase 3 — 픽셀 퍼펙트 (Nearest)
> 이 프로젝트는 이미 프로젝트 전역이 Nearest 라서 사실 안 해도 됩니다. 원리만:
- **전역**: Project → Project Settings → Rendering → Textures → Canvas Textures →
  **Default Texture Filter = Nearest**.
- **노드별**: AnimatedSprite2D 선택 → 인스펙터 **CanvasItem → Texture → Texture Filter = Nearest**.

---

## Phase 4 — 애니메이션 만들기 (핵심)
1. **AnimatedSprite2D** 선택 → 인스펙터 **Sprite Frames** 칸 → **New SpriteFrames** →
   방금 생긴 파란 리소스를 **한 번 클릭** → 화면 하단에 **SpriteFrames 패널** 이 열림.
2. 하단 패널 왼쪽이 "애니메이션 목록". 기본 `default` 가 있음.
   **+** 버튼으로 애니를 만들고 이름을 바꾼다 (또는 default 이름을 바꿔 재활용):
   - `idle` · `walk` · `attack` · `guard`
3. 각 애니를 선택한 상태에서 오른쪽 위:
   - **루프 버튼(🔁)**: idle/walk/guard = 켬, **attack = 끔**.
   - **Speed(FPS)**: idle 4, walk 8, attack 12, guard 5 정도.
4. 프레임 채우기 — 애니 하나 선택 → 툴바의 **"Add frames from Sprite Sheet"**(격자 모양 아이콘) 클릭:
   - 파일 선택: `knight_sheet.png`
   - 대화창 위쪽 **Horizontal / Vertical** = 칸 수 (이 시트는 대략 **9 / 2**, 실제에 맞게 조정)
   - 그러면 격자로 잘린 미리보기가 뜸 → **이 애니에 넣을 칸들을 클릭**해서 선택 → **Add Frames**.
     - `attack` → **윗줄(검 휘두르는 순서)** 칸들
     - `guard` → **아랫줄 방패 든** 칸들
     - `walk` → 걷는/서있는 칸 몇 개
     - `idle` → 서있는 칸 1~2개
5. 하단 패널에서 ▶ 로 미리보기. 순서가 이상하면 프레임을 드래그로 재정렬.

> 어떤 칸이 어떤 애니가 될지는 **여기서 클릭으로** 정합니다. 코드에 프레임 번호를 안 박아도 됩니다.

---

## Phase 5 — 충돌 모양
1. **CollisionShape2D** 선택 → 인스펙터 **Shape** → **New RectangleShape2D**(또는 Capsule).
2. 뷰포트에서 파란 핸들을 끌어 캐릭터 몸에 맞춤 (너무 크지 않게, 발~머리 정도).

---

## Phase 6 — 스크립트 붙이기
1. **Knight**(루트) 선택 → 상단 **Attach Script**(두루마리 아이콘) → 경로
   `experiments/knight/PlayerKnight.gd` → Load/Create.
2. 스크립트 내용은 이미 작성돼 있음. 하는 일:
   - 매 물리 프레임 **WASD/방향키**를 읽어 `velocity` 로 이동(`move_and_slide`)
   - 왼쪽으로 가면 `flip_h = true` 로 **좌우 반전**
   - **J/Space/좌클릭** = 공격(1회 재생 후 복귀), **K/우클릭** = 방어(이동 감속)
   - 우선순위 **공격 > 방어 > 걷기 > 대기** 로 `anim.play(...)`

---

## Phase 7 — 실행
- 상단 우측 **"Run Current Scene"(F6)** → 창이 뜸.
- **WASD** 이동 / **J·Space·좌클릭** 공격 / **K·우클릭** 방어.

### 안 될 때
| 증상 | 확인 |
|---|---|
| 안 움직임 | Knight(루트=CharacterBody2D)에 스크립트 붙었나 |
| 안 보임 | SpriteFrames에 프레임 넣었나 / 배경 투명한가 / animation="idle" 인가 |
| 흐릿함 | Texture Filter = Nearest |
| 공격이 안 끝남 | attack 애니 **루프 꺼짐** 인가 |
| 애니 안 바뀜 | 애니 이름이 코드와 정확히 같은가(idle/walk/attack/guard) |

---

## Phase 8 — (선택) 배경 임시 제거 셰이더
AnimatedSprite2D → CanvasItem → **Material → New ShaderMaterial** →
Shader 에 `res://experiments/knight/keyout.gdshader` → `key_color` 를 배경색으로.
> 외곽에 색 자국 남을 수 있음 → 최종본은 Phase 1처럼 진짜 알파로.

---

## Phase 9 — 다음: 본 게임(C#)으로
이 흐름(시트 슬라이스 → SpriteFrames → 상태별 play)을 익힌 뒤 게임엔 C#으로 옮깁니다.
C#도 똑같이 `AnimatedSprite2D` + `SpriteFrames` 를 쓰고 `Play("attack")` 로 호출합니다.
차이는 언어뿐, 노드·개념은 동일합니다.
