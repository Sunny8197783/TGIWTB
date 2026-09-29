// 바닥 충격파 고리: 가운데서 바깥으로 퍼지며 얇아지고 사라진다. 픽셀 단위로 끊어 그린다.
// 색은 4단 램프(흰 심 → 밝은 → 중간 → 짙은). 가산이라 흰 심이 글로우로 번진다.
shader_type spatial;
render_mode unshaded, blend_add, depth_draw_never, cull_disabled, shadows_disabled;

uniform float progress = 0.0;   // 0..1
uniform float px = 128.0;       // 지름 픽셀 수
uniform vec4 core : source_color = vec4(1.0);
uniform vec4 bright : source_color = vec4(0.55, 0.98, 1.0, 1.0);
uniform vec4 mid : source_color = vec4(0.16, 0.72, 0.82, 1.0);
uniform vec4 deep : source_color = vec4(0.04, 0.3, 0.42, 1.0);
uniform float energy = 1.8;

void fragment() {
	vec2 q = (floor(UV * px) + 0.5) / px - 0.5;
	float r = length(q) * 2.0;
	float front = sqrt(progress);                 // 처음엔 빠르게, 끝으로 갈수록 느리게
	float width = mix(0.16, 0.03, progress);
	float d = front - r;                           // 앞머리 뒤쪽이 양수
	if (d < 0.0 || d > width || r > 1.0) discard;
	float k = d / width;                           // 0 앞머리 .. 1 꼬리
	vec3 col = k < 0.2 ? core.rgb * 1.4 : k < 0.45 ? bright.rgb : k < 0.75 ? mid.rgb : deep.rgb;
	ALBEDO = col * energy * (1.0 - progress * 0.7);
}
