// 베기 궤적 (조사: cyanilux 칼 궤적 셰이더). 고리 조각 메시 위로 '머리'가 쓸고 지나가고,
// 뒤로 꼬리가 줄무늬로 흩어지며 사라진다. 값 하나(progress)로 드러났다 사라진다.
// 색은 4단(흰 심 → 밝은 청록 → 청록 → 짙은 청록)으로 끊어 픽셀아트처럼. 흰 심은 글로우 문턱을 넘겨 번진다.
// UV.x = 호를 따라 0..1 (휘두르는 방향), UV.y = 안쪽 0 .. 바깥 1.
shader_type spatial;
render_mode unshaded, blend_add, depth_draw_never, cull_disabled, shadows_disabled;

#include "res://shaders/common.gdshaderinc"

uniform float progress = 0.0;   // 머리 위치 (1 을 넘기면 꼬리가 빠져나간다)
uniform float tail = 0.85;      // 꼬리 길이 (호 비율)
uniform float band = 0.66;      // 띠 중심 반지름 (0..1)
uniform float dissolve = 0.0;   // 0..1 다 그은 초승달이 픽셀 단위로 흩어진다
uniform float thickness = 1.0;
uniform vec4 core : source_color = vec4(1.0, 1.0, 1.0, 1.0);
uniform vec4 bright : source_color = vec4(0.55, 0.98, 1.0, 1.0);
uniform vec4 mid : source_color = vec4(0.16, 0.72, 0.82, 1.0);
uniform vec4 deep : source_color = vec4(0.04, 0.3, 0.42, 1.0);
uniform float energy = 1.9;
uniform float seed = 0.0;

void fragment() {
	float a = UV.x;
	float behind = progress - a;
	if (behind < 0.0 || behind > tail) discard;
	float t = behind / tail; // 0 머리 .. 1 꼬리 끝
	// 초승달: 호 가운데가 가장 두껍고 양끝은 뾰족
	float crescent = pow(sin(clamp(a, 0.0, 1.0) * PI), 0.7);
	float width = crescent * thickness * (1.0 - t * 0.6) * 0.42;
	float d = abs(UV.y - band) / max(width, 0.001);
	if (d > 1.0) discard;
	// 줄마다 꼬리 길이가 달라 끝이 빗살처럼 흩어진다
	float row = floor(UV.y * 18.0);
	float reach = 0.35 + 0.65 * hash21(vec2(row, seed));
	if (t > reach) discard;
	// 흩어짐: 화면 픽셀마다 문턱이 달라 가장자리·꼬리부터 알갱이로 사라진다
	if (dissolve > 0.0 && hash21(floor(FRAGCOORD.xy)) < dissolve * (0.6 + 0.8 * max(d, t))) discard;
	float heat = (1.0 - t / reach) * (1.0 - d * 0.55);
	vec3 col = heat > 0.78 ? core.rgb * 1.6 : heat > 0.5 ? bright.rgb : heat > 0.25 ? mid.rgb : deep.rgb;
	ALBEDO = col * energy; // 주인공 이펙트라 슬로우 중에도 제 색
}
