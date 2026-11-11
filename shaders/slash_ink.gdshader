// 먹 붓질 궤적 (묵영). 빛의 호(slash.gdshader)와 같은 메시·같은 변수를 쓰되, 더하지 않고 덮어 칠한다 —
// 먹은 빛나지 않는다. 붓끝(머리)만 옅게 빛나고, 몸통은 짙은 먹, 꼬리는 마른 붓처럼 줄줄이 갈라진다.
// UV.x = 호를 따라 0..1 (휘두르는 방향), UV.y = 안쪽 0 .. 바깥 1.
shader_type spatial;
render_mode unshaded, cull_disabled, shadows_disabled; // 불투명 패스: 깊이를 써야 뒤에 그려지는 땅이 덮지 않는다

#include "res://shaders/common.gdshaderinc"

uniform float progress = 0.0;
uniform float tail = 0.85;
uniform float band = 0.66;
uniform float dissolve = 0.0;
uniform float thickness = 1.0;
uniform vec4 core : source_color = vec4(0.92, 0.9, 1.0, 1.0);
uniform vec4 bright : source_color = vec4(0.5, 0.48, 0.62, 1.0);
uniform vec4 mid : source_color = vec4(0.2, 0.18, 0.3, 1.0);
uniform vec4 deep : source_color = vec4(0.05, 0.04, 0.09, 1.0);
uniform float seed = 0.0;

void fragment() {
	float a = UV.x;
	float behind = progress - a;
	if (behind < 0.0 || behind > tail) discard;
	float t = behind / tail; // 0 붓끝 .. 1 꼬리 끝
	// 붓질: 처음엔 눌러 굵고, 끝으로 갈수록 들려 가늘다
	float press = pow(sin(clamp(a, 0.0, 1.0) * PI), 0.55);
	float width = press * thickness * (1.0 - t * 0.35) * 0.66;
	float d = abs(UV.y - band) / max(width, 0.001);
	if (d > 1.0) discard;
	// 마른 붓: 털 한 올(줄)마다 먹이 다르게 묻어, 꼬리에서 줄줄이 끊긴다
	float row = floor(UV.y * 22.0);
	float wet = hash21(vec2(row, seed));
	if (t > 0.3 + 0.7 * wet) discard;
	// 가장자리는 털이 갈라져 듬성듬성
	if (d > 0.72 && hash21(vec2(row, floor(a * 40.0) + seed)) < 0.45) discard;
	// 흩어짐: 먹물이 종이에 스미듯 알갱이로 사라진다
	if (dissolve > 0.0 && hash21(floor(FRAGCOORD.xy)) < dissolve * (0.55 + 0.9 * max(d, t))) discard;
	// 붓끝만 옅은 빛, 그 뒤는 짙은 먹 → 가장자리·꼬리는 묽은 먹
	vec3 col = t < 0.07 && d < 0.5 ? core.rgb : (d > 0.8 || t > 0.62 * (0.3 + 0.7 * wet) + 0.2) ? mid.rgb : deep.rgb;
	ALBEDO = col;
}
