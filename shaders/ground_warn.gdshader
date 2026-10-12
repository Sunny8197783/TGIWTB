// 강한 공격 예고: 바닥의 빨간 원(내려찍기·회전) 또는 띠(돌진). 테두리는 처음부터, 안쪽은 예고 시간 동안 차오른다
// (원은 가운데서 바깥으로, 띠는 몸 쪽에서 돌진 끝으로). 차오름이 끝에 닿는 순간 = 공격이 떨어지는 순간. 픽셀 단위로 끊어 그린다.
shader_type spatial;
render_mode unshaded, blend_mix, depth_draw_never, cull_disabled, shadows_disabled;

uniform float fill = 0.0;          // 0..1 예고 진행
uniform vec2 px = vec2(40.0);      // 가로·세로 픽셀 수 (UV 를 이만큼 끊는다)
uniform int shape = 0;             // 0 원, 1 띠
uniform vec4 color : source_color = vec4(1.0, 0.25, 0.2, 1.0);

void fragment() {
	vec2 cell = (floor(UV * px) + 0.5) / px;
	float ring, inner, edge;
	if (shape == 0) {
		vec2 q = cell - 0.5;
		float r = length(q) * 2.0;
		if (r > 1.0) discard;
		ring = step(1.0 - 2.5 / px.x, r);          // 바깥 테두리 한 픽셀 남짓
		inner = step(r, fill);
		edge = step(fill - 2.0 / px.x, r) * inner;  // 차오르는 가장자리는 밝게
	} else {
		// 띠: UV.y 1 = 몸 쪽, 0 = 돌진 끝
		float along = 1.0 - cell.y;
		vec2 b = min(cell, 1.0 - cell) * px;       // 가장자리까지 픽셀 수
		ring = step(min(b.x, b.y), 1.0);
		inner = step(along, fill);
		edge = step(fill - 2.0 / px.y, along) * inner;
	}
	float a = max(ring, inner * 0.5 + edge * 0.45);
	if (a < 0.01) discard;
	ALBEDO = color.rgb * (1.0 + edge * 0.6);
	ALPHA = a;
}
