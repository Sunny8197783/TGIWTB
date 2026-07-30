extends CharacterBody2D
# 학습용 — 8방향 이동 + idle/walk + 공격/방어 애니메이션 + 좌우 반전
# 자식: AnimatedSprite2D (SpriteFrames 에 idle / walk / attack / guard 정의)
# ※ 본 게임(C#)과 분리된 연습용입니다.

@export var speed: float = 90.0             # 이동 속도(px/초)
@export var guard_speed_mult: float = 0.4   # 방어 중 이동 감속 배율

@onready var anim: AnimatedSprite2D = $AnimatedSprite2D

var _attacking: bool = false   # 공격 애니 재생 중인가
var _guarding: bool = false    # 방어 유지 중인가

func _ready() -> void:
	# 공격 애니(루프 꺼짐)가 끝나면 알림을 받아 상태를 푼다
	anim.animation_finished.connect(_on_anim_finished)

func _physics_process(_delta: float) -> void:
	# --- 1) 이동 입력 (WASD + 방향키, InputMap 설정 불필요) ---
	var input := Vector2.ZERO
	if Input.is_key_pressed(KEY_A) or Input.is_key_pressed(KEY_LEFT):  input.x -= 1.0
	if Input.is_key_pressed(KEY_D) or Input.is_key_pressed(KEY_RIGHT): input.x += 1.0
	if Input.is_key_pressed(KEY_W) or Input.is_key_pressed(KEY_UP):    input.y -= 1.0
	if Input.is_key_pressed(KEY_S) or Input.is_key_pressed(KEY_DOWN):  input.y += 1.0
	input = input.normalized()   # 대각선 속도 보정 (0 벡터는 그대로 0)

	# --- 2) 방어: K 또는 마우스 오른쪽을 누르고 있는 동안 ---
	_guarding = Input.is_key_pressed(KEY_K) or Input.is_mouse_button_pressed(MOUSE_BUTTON_RIGHT)

	# --- 3) 공격: J / Space / 마우스 왼쪽 (공격 중엔 다시 발동 안 함) ---
	#     누르고 있으면 끝난 뒤 자동 반복 — 데모 편의상 허용.
	if not _attacking and (
			Input.is_key_pressed(KEY_J)
			or Input.is_key_pressed(KEY_SPACE)
			or Input.is_mouse_button_pressed(MOUSE_BUTTON_LEFT)):
		_attacking = true
		anim.play("attack")

	# --- 4) 이동 처리 (공격 중엔 멈춤, 방어 중엔 감속) ---
	var spd := speed * (guard_speed_mult if _guarding else 1.0)
	velocity = Vector2.ZERO if _attacking else input * spd
	move_and_slide()

	# --- 5) 좌우 반전 — 왼쪽으로 갈 때만 뒤집는다 ---
	if input.x < 0.0:
		anim.flip_h = true
	elif input.x > 0.0:
		anim.flip_h = false

	# --- 6) 애니메이션 우선순위: 공격 > 방어 > 걷기 > 대기 ---
	if _attacking:
		pass                       # 공격 애니 유지 (끝나면 6에서 해제됨)
	elif _guarding:
		_play("guard")
	elif input != Vector2.ZERO:
		_play("walk")
	else:
		_play("idle")

# 공격 애니가 끝나면 상태 해제 → 다음 프레임에 idle/walk/guard 로 복귀
func _on_anim_finished() -> void:
	if anim.animation == "attack":
		_attacking = false

# 같은 애니가 이미 재생 중이면 다시 호출하지 않는다 (끊김 방지)
func _play(name: String) -> void:
	if anim.animation != name or not anim.is_playing():
		anim.play(name)
