// 물 — 깊이로 색을 정하고, 물가 거품과 픽셀 반짝임을 얹는다.
// 반짝임은 EMISSION 으로 내보내서 글로우(블룸)가 잡게 한다 — "반짝이는 물"의 핵심.
shader_type spatial;
render_mode blend_mix, depth_draw_opaque, cull_back, specular_schlick_ggx;

#include "res://shaders/common.gdshaderinc"

global uniform float world_time;
global uniform float night;
global uniform vec3 sun_dir;
global uniform vec4 sun_color : source_color;
global uniform vec4 sky_horizon : source_color;
global uniform vec2 wind;
global uniform vec2 map_size;

uniform vec3 shallow_color : source_color = vec3(0.36, 0.82, 0.78);
uniform vec3 mid_color : source_color = vec3(0.14, 0.55, 0.74);
uniform vec3 deep_color : source_color = vec3(0.06, 0.24, 0.48);
// 바다 바깥 평면처럼 지형 정보가 없는 곳은 깊다고 친다.
uniform bool assume_deep = false;
uniform bool debug_depth = false;

varying vec3 wpos;
varying float cloud;
varying float vdepth;

const float MAX_DEPTH = 4.0; // WaterBuilder.MaxDepth

void vertex() {
	vec3 w = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	// 아주 작은 물결 — 저해상도라 크면 수면이 출렁여 보인다
	float t = world_time;
	VERTEX.y += (sin(w.x * 0.7 + t * 1.3) + sin(w.z * 0.9 - t * 1.1)) * 0.025;
	wpos = w;
	vdepth = COLOR.r * MAX_DEPTH;
}

void fragment() {
	vec2 tp = ground_texel(wpos.xz);
	float t = world_time;
	float depth = assume_deep ? MAX_DEPTH : vdepth;

	float d01 = smoothstep(0.0, 3.2, depth);
	vec3 col = mix(shallow_color, mid_color, smoothstep(0.0, 0.5, d01));
	col = mix(col, deep_color, smoothstep(0.5, 1.0, d01));

	// 얕은 곳 물빛 무늬(코스틱) — 두 겹의 흐르는 잡음이 겹치는 곳을 밝힌다
	vec2 cp = wpos.xz;
	float c1 = vnoise(cp * 1.7 + vec2(t * 0.35, t * 0.2));
	float c2 = vnoise(cp * 1.9 - vec2(t * 0.25, t * 0.3) + 5.0);
	float caustic = smoothstep(0.08, 0.0, abs(c1 - c2));
	col += caustic * (1.0 - d01) * 0.18;

	// 물가 거품: 해안선을 따라 들고나는 흰 띠
	float wave = sin(depth * 14.0 - t * 2.2 + vnoise(cp * 0.8) * 4.0) * 0.5 + 0.5;
	float foam_edge = smoothstep(0.18, 0.02, depth + (vnoise(cp * 3.0 + t * 0.4) - 0.5) * 0.08);
	float foam_band = step(0.62, wave) * smoothstep(0.22, 0.05, depth);
	float foam = max(foam_edge, foam_band * 0.85);
	foam = step(0.5, foam);

	// 햇빛 반사 방향 근처일수록 반짝임이 잦다
	vec3 view_dir = normalize(CAMERA_POSITION_WORLD - wpos);
	vec3 refl = reflect(-view_dir, vec3(0.0, 1.0, 0.0));
	float sun_align = max(dot(refl, normalize(sun_dir)), 0.0);
	float twinkle_cell = hash21(tp + floor(t * 3.0 + hash21(tp) * 7.0) * 13.1);
	// 반짝임은 해(밤에는 달) 반사 길을 따라서만 촘촘하다. 전면에 뿌리면 화면이 지지직거린다.
	float path = pow(sun_align, 18.0);
	float sparkle_rate = mix(0.9992, 0.955, path);
	float sparkle = step(sparkle_rate, twinkle_cell) * (1.0 - foam) * (0.3 + 0.7 * d01);

	// 물결 반짝: 짧은 가로 획(2~5픽셀)이 드문드문 켜졌다 꺼진다 — 픽셀아트 물의 기본 무늬.
	// 긴 곡선은 긁힌 자국처럼 보였다.
	vec2 rc = floor(vec2(tp.x / 7.0, tp.y / 3.0));
	float rh = hash21(rc + vec2(floor(t * 0.7 + hash21(rc) * 5.0) * 3.7, 0.0));
	float dash_len = 2.0 + floor(hash21(rc + 9.1) * 4.0);
	float in_cell = step(mod(tp.x, 7.0), dash_len) * step(abs(mod(tp.y, 3.0) - 1.0), 0.0);
	float ripple = step(0.82, rh) * in_cell;
	col = mix(col, col * 1.18 + vec3(0.06, 0.08, 0.09), ripple * (0.5 + 0.5 * d01));
	// 반사 길 자체도 은은하게 밝힌다
	col += sun_color.rgb * path * 0.18 * (1.0 - night * 0.5);

	// 하늘빛 반사 — 비스듬히 볼수록 하늘색
	float fres = pow(1.0 - clamp(view_dir.y, 0.0, 1.0), 3.0);
	col = mix(col, sky_horizon.rgb, fres * 0.45);

	col = mix(col, vec3(0.95, 0.98, 1.0), foam);
	cloud = cloud_shade(wpos.xz, world_time, wind);

	ALBEDO = col;
	ROUGHNESS = 0.18;
	METALLIC = 0.0;
	SPECULAR = 0.35;
	ALPHA = mix(0.6, 0.94, d01) + foam;
	// 밤에는 반짝임을 달빛/등불 색으로 약하게
	vec3 glint = mix(sun_color.rgb * 2.2, vec3(0.7, 0.8, 1.2) * 1.2, night);
	EMISSION = sparkle * glint * cloud;
	if (debug_depth) { ALBEDO = vec3(d01); ALPHA = 1.0; EMISSION = vec3(0.0); }
}

void light() {
	float ndl = clamp(dot(NORMAL, LIGHT), 0.0, 1.0);
	float shade = smoothstep(0.25, 0.75, ATTENUATION);
	if (LIGHT_IS_DIRECTIONAL) shade *= cloud;
	DIFFUSE_LIGHT += LIGHT_COLOR / PI * (0.6 + 0.4 * ndl) * shade;
	// 픽셀 느낌의 딱 떨어지는 하이라이트
	vec3 hv = normalize(LIGHT + VIEW);
	float spec = pow(max(dot(NORMAL, hv), 0.0), 120.0);
	SPECULAR_LIGHT += LIGHT_COLOR / PI * step(0.5, spec) * 0.6 * shade;
}
