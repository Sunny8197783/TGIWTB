# 기사 캐릭터 — 학습용 독립 씬 (AnimatedSprite2D 버전)

**본 게임(TGIWTB, C#)과 분리된 GDScript 연습용입니다.** C# 빌드에 영향 없음.

## 미리 세팅된 것
- `Knight.tscn` — CharacterBody2D → **AnimatedSprite2D**(Nearest) + CollisionShape2D
- `AnimatedSprite2D` 안의 **SpriteFrames** 에 애니메이션 4종이 **이름만** 등록됨(프레임은 비어 있음):
  - `idle`(루프), `walk`(루프), `attack`(루프 꺼짐), `guard`(루프)
- `PlayerKnight.gd` — 이동 + 좌우 반전 + 공격/방어 상태 제어

## 프레임 채우기 (에디터에서, 5분)
1. 공격/방어 시트를 **배경 투명(알파)** 으로 만들어 `experiments/knight/knight_sheet.png` 로 저장.
2. `Knight.tscn` 열기 → **AnimatedSprite2D** 선택 → 하단 **SpriteFrames** 패널이 뜸.
3. 왼쪽 애니 목록에서 **`attack`** 선택 → 툴바의 **"시트에서 프레임 추가"**(격자 아이콘) 클릭:
   - 대화창에서 **Horizontal = 9, Vertical = 2** 로 슬라이스 (※ 실제 칸 수에 맞게 조정)
   - **윗줄(공격 스윙) 칸들**을 순서대로 선택 → 추가
4. **`guard`** 선택 → 같은 방식으로 **아랫줄의 방패 든 칸들** 선택 → 추가
5. **`walk`** 선택 → 아랫줄의 걷는/서있는 칸 몇 개 선택 → 추가
6. **`idle`** 선택 → 서있는 칸 1~2개 선택 → 추가
7. **F6** 실행 → WASD 이동 / **J·Space·좌클릭 = 공격** / **K·우클릭 = 방어**

> 핵심: 어떤 칸이 어떤 애니가 되는지는 **에디터에서 셀 클릭으로** 정합니다. 그래서 시트의 정확한
> 프레임 번호를 코드에 박을 필요가 없어요.

## 슬라이스가 안 맞으면
격자(9×2)로 잘랐는데 캐릭터가 어긋나면, 시트가 **균등한 격자가 아닌** 겁니다.
→ 이미지 편집기에서 각 캐릭터를 **같은 크기 칸**으로 재배치하거나, 프레임을 개별로 잘라 추가하세요.

## 조작
| 키 | 동작 |
|---|---|
| WASD / 방향키 | 8방향 이동 |
| J / Space / 마우스 좌클릭 | 공격 (한 번 재생 후 복귀) |
| K / 마우스 우클릭 | 방어 (누르는 동안, 이동 감속) |

## 배경 임시 제거 (선택)
AnimatedSprite2D → CanvasItem → Material → New ShaderMaterial →
`res://experiments/knight/keyout.gdshader` 지정 → `key_color` 를 배경색으로.
> 최종본은 편집기에서 진짜 알파로 지우기.

## 다음 (본 게임)
이 파이프라인(슬라이스 → SpriteFrames → 상태별 play)을 익힌 뒤, 게임엔 **C#** 으로 옮깁니다.
C#에서도 AnimatedSprite2D + SpriteFrames 를 똑같이 쓰고 `Play("attack")` 식으로 호출합니다.
