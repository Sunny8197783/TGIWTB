# 기사 캐릭터 — 학습용 독립 씬

**본 게임(TGIWTB, C#)과 분리된 GDScript 연습용입니다.** 여기 있는 `.gd`/`.tscn`은
C# 빌드에 영향을 주지 않고, 게임에서 로드되지도 않습니다. Godot 4 스프라이트 파이프라인을
직접 익혀 보는 용도예요.

## 미리 세팅된 것
- `Knight.tscn` — 노드 트리(CharacterBody2D → Sprite2D / CollisionShape2D / AnimationPlayer)
- Sprite2D: **Hframes = 3**, **Texture Filter = Nearest** 설정 완료
- AnimationPlayer: **idle** 애니메이션(1.2초 루프, 위아래 -2px 숨쉬기) 등록 완료
- `PlayerKnight.gd` — WASD/방향키 8방향 이동 + 좌우 반전 + idle 재생

## 사용 순서
1. 첨부한 기사 이미지를 **배경을 투명(알파)으로 지운 뒤** `experiments/knight/knight.png` 로 저장.
   - 빠른 확인만 할 거면 배경 제거 없이 넣고, 아래 "배경 임시 제거" 참고.
2. Godot 에디터에서 `Knight.tscn` 열기.
3. **Sprite2D** 선택 → 인스펙터 **Texture** 칸에 `knight.png` 드래그.
   - Hframes=3 이 이미 설정돼 있어 기사 하나만 보입니다. **Frame**(0/1/2)로 방패 종류 선택.
   - 캐릭터가 잘리면 세 캐릭터의 간격이 안 맞는 것 → 이미지 편집기에서 여백을 균등하게 자르세요.
4. **CollisionShape2D** 크기를 캐릭터에 맞게 조절.
5. **F6**(현재 씬 실행) → WASD/방향키로 이동, 멈추면 숨쉬기 확인.

## 배경 임시 제거 (선택)
1. Sprite2D → CanvasItem → **Material → New ShaderMaterial**
2. Shader 칸에 `res://experiments/knight/keyout.gdshader` 지정
3. `key_color` 를 실제 배경색으로 조정
> 외곽에 배경색 자국이 남을 수 있음 → 최종본은 편집기에서 진짜 알파로 지우기.

## 한계 (중요)
- 첨부 이미지는 **같은 포즈 + 방패 3종**이라, 진짜 걷기/공격 애니는 만들 수 없습니다.
  포즈가 다른 walk/attack 시트를 따로 구하면 그때 `Sprite2D:frame` 트랙으로 애니를 만듭니다.
- 이 방식(GDScript + AnimatedSprite)을 익힌 뒤, 본 게임에는 **C#으로** 옮기는 걸 권장.
