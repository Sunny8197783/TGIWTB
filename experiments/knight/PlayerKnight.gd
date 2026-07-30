extends CharacterBody2D
# 학습용 — 8방향 이동 + idle(숨쉬기) 재생 + 좌우 반전
# 이 스크립트는 experiments/knight/Knight.tscn 의 루트에 붙어 있습니다.
# ※ 본 게임(C#)과는 분리된 연습용입니다.

@export var speed: float = 90.0   # 이동 속도(px/초)

@onready var sprite: Sprite2D = $Sprite2D
@onready var anim: AnimationPlayer = $AnimationPlayer

func _ready() -> void:
	# 시작하자마자 숨쉬기 시작
	if anim.has_animation("idle"):
		anim.play("idle")

func _physics_process(_delta: float) -> void:
	# 1) 입력 모으기 — WASD와 방향키 둘 다 지원 (InputMap 설정 불필요)
	var input := Vector2.ZERO
	if Input.is_key_pressed(KEY_A) or Input.is_key_pressed(KEY_LEFT):  input.x -= 1.0
	if Input.is_key_pressed(KEY_D) or Input.is_key_pressed(KEY_RIGHT): input.x += 1.0
	if Input.is_key_pressed(KEY_W) or Input.is_key_pressed(KEY_UP):    input.y -= 1.0
	if Input.is_key_pressed(KEY_S) or Input.is_key_pressed(KEY_DOWN):  input.y += 1.0

	# 2) 대각선이 더 빠르지 않도록 정규화 (0 벡터는 그대로 0)
	input = input.normalized()

	# 3) 이동
	velocity = input * speed
	move_and_slide()

	# 4) 좌우 반전 — 왼쪽으로 갈 때만 뒤집는다
	if input.x < 0.0:
		sprite.flip_h = true
	elif input.x > 0.0:
		sprite.flip_h = false

	# 5) 애니메이션 — 지금은 idle(숨쉬기)만 있으므로 항상 유지.
	#    나중에 walk 시트가 생기면 아래 else 를 "walk" 로 바꾸면 된다.
	if input == Vector2.ZERO:
		_play("idle")
	else:
		_play("idle")   # TODO: walk 프레임 생기면 "walk"

# 같은 애니가 이미 재생 중이면 다시 호출하지 않는다 (끊김 방지)
func _play(name: String) -> void:
	if anim.has_animation(name) and anim.current_animation != name:
		anim.play(name)
