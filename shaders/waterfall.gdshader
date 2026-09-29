// 폭포 물줄기 — 절벽 면을 따라 내려오는 띠. 세로로 흘러내리는 줄무늬를 픽셀 격자에 맞춰 그리고
// 밝은 물방울을 EMISSION 으로 내보내 글로우가 잡게 한다.
shader_type spatial;
render_mode blend_mix, depth_draw_opaque, cull_disabled, diffuse_lambert, specular_disabled;

#include "res://shaders/common.gdshaderinc"

global uniform float world_time;
global uniform float night;
global uniform vec4 sky_horizon : source_color;

uniform float length_m = 10.0;  // 물줄기 길이 (UV.y 1 = 이만큼)
uniform float width_m = 4.5;

varying float cloud;

void fragment() {
	// 텍셀 격자: 가로 32/m, 세로 32/m (띠는 거의 수직이라 비스듬히 봐도 거의 1:1)
	vec2 tp = floor(vec2(UV.x * width_m, UV.y * length_m) * PPM);
	float col_id = floor(tp.x / 2.0);
	float speed = 9.0 + hash21(vec2(col_id, 3.0)) * 5.0;       // 기둥마다 떨어지는 속도가 조금씩 다르다
	float flow = tp.y - world_time * speed * PPM * 0.25;
	float streak = hash21(vec2(col_id, floor(flow / 6.0)));
	float fall = smoothstep(0.0, 0.08, UV.y);                  // 턱을 넘으며 가속

	vec3 deep = vec3(0.22, 0.52, 0.74);
	vec3 mid = vec3(0.45, 0.75, 0.9);
	vec3 light = vec3(0.82, 0.94, 1.0);
	vec3 col = streak < 0.4 ? deep : (streak < 0.8 ? mid : light);
	if (streak > 0.96) col = vec3(1.0);

	// 가장자리는 들쭉날쭉 투명
	float spread = mix(0.72, 1.0, smoothstep(0.0, 0.6, UV.y));
	float cx = abs(UV.x - 0.5) * 2.0;
	if (cx > spread) discard;
	float edge = (spread - cx) * 0.5 * width_m;
	float ragged = 0.12 + 0.3 * hash21(vec2(floor(flow / 8.0), floor(UV.x * 5.0)));
	if (edge < ragged) discard;

	// 아래쪽은 부서지는 흰 물보라
	float foam = smoothstep(0.86, 1.0, UV.y) * step(0.35, hash21(tp + floor(world_time * 10.0))) + step(0.992, hash21(tp + floor(world_time * 12.0)));
	col = mix(col, vec3(1.0), clamp(foam, 0.0, 1.0));

	// 밤에는 하늘빛을 받아 푸르게 가라앉는다
	col = mix(col, col * vec3(0.45, 0.55, 0.9), night * 0.7);

	ALBEDO = slow_tint(col);
	ALPHA = mix(0.78, 0.95, step(0.45, streak)) * fall + 0.05;
	// 반짝이는 물방울: 드문드문 한 픽셀이 번쩍
	float glint = step(0.993, hash21(tp + vec2(floor(world_time * 9.0) * 7.0, 0.0)));
	EMISSION = vec3(glint) * mix(1.8, 0.9, night);
	cloud = 1.0;
}

void light() {
	DIFFUSE_LIGHT += LIGHT_COLOR / PI * (0.55 + 0.45 * clamp(dot(NORMAL, LIGHT), 0.0, 1.0)) * ATTENUATION;
}
