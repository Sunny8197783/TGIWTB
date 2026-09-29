// 강한 공격 예고: 바닥의 빨간 원. 테두리는 처음부터, 안쪽은 예고 시간 동안 가운데서 바깥으로 차오른다.
// 차오름이 테두리에 닿는 순간 = 공격이 떨어지는 순간. 픽셀 단위 고리로 끊어 그린다.
shader_type spatial;
render_mode unshaded, blend_mix, depth_draw_never, cull_disabled, shadows_disabled;

uniform float fill = 0.0;       // 0..1 예고 진행
uniform float px = 40.0;        // 원 지름의 픽셀 수 (UV 를 이만큼 끊는다)
uniform vec4 color : source_color = vec4(1.0, 0.25, 0.2, 1.0);

void fragment() {
	vec2 q = (floor(UV * px) + 0.5) / px - 0.5;
	float r = length(q) * 2.0;
	if (r > 1.0) discard;
	float ring = step(1.0 - 2.5 / px, r);          // 바깥 테두리 한 픽셀 남짓
	float inner = step(r, fill);
	// 차오르는 가장자리는 밝게
	float edge = step(fill - 2.0 / px, r) * inner;
	float a = max(ring, inner * 0.5 + edge * 0.45);
	if (a < 0.01) discard;
	ALBEDO = color.rgb * (1.0 + edge * 0.6);
	ALPHA = a;
}
