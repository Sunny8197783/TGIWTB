// 픽셀 스프라이트 — 소품·나무·건물·캐릭터 공통.
// 판은 카메라를 향해 서 있지만, 빛은 '위에서 비스듬히 받는 면'으로 계산한다.
// 판의 실제 법선(카메라 쪽)으로 계산하면 해가 북쪽에 있을 때 모든 나무가 시커멓게 된다.
shader_type spatial;
render_mode diffuse_lambert, specular_disabled, cull_disabled, depth_draw_opaque;

#include "res://shaders/common.gdshaderinc"

global uniform float world_time;
global uniform float night;
global uniform vec2 wind;
// 주인공 가슴의 화면 위치(640x360 픽셀)와 발 z — 앞을 가린 소품을 바둑판으로 비워 주인공을 보여 준다 (Combat/Hero)
global uniform vec2 hero_screen;
global uniform float hero_z;
const vec2 SEE_THROUGH = vec2(15.0, 27.0); // 비우는 타원 반지름 (픽셀). 주인공 그림이 약 30x56

uniform sampler2D albedo_tex : source_color, filter_nearest, repeat_disable;
uniform float sway = 0.0;         // 바람 흔들림 (m). 나무 0.04, 풀 0.03
uniform float glow_at_night = 0.0; // 밝은 난색 픽셀을 밤에 빛나게 (등불)
uniform float flash = 0.0;         // 피격 섬광 세기
uniform vec4 flash_color : source_color = vec4(1.0); // 흰색 = 맞음, 주황 = 공격 예고, 빨강 = 주인공 피격
uniform vec4 tint : source_color = vec4(1.0);
uniform vec2 frame_count = vec2(1.0, 1.0); // 시트 가로/세로 칸 수 (캐릭터 애니메이션)
uniform float frame = 0.0;
uniform bool instance_tint = false; // MultiMesh 인스턴스 색을 곱한다 (풀포기)
// 서 있는 판을 시선 방향으로 카메라 쪽에 당긴다 (m). 화면 위치는 그대로라 픽셀이 안 흔들리고,
// 비탈을 오를 때 발이 앞쪽 땅에 파묻혀 잘리지 않는다. 캐릭터 0.6, 소품 0.
uniform float depth_pull = 0.0;
uniform bool slow_exempt = false; // 주인공: 슬로우 중에도 제 색

varying vec3 wpos;
varying float cloud;
varying float phase;
varying vec4 itint;
varying float iframe;

void vertex() {
	phase = INSTANCE_CUSTOM.x;
	itint = instance_tint ? COLOR : vec4(1.0);
	iframe = INSTANCE_CUSTOM.z; // MultiMesh 인스턴스마다 다른 그림 칸 (풀포기)
	float amount = sway * (INSTANCE_CUSTOM.y > 0.0 ? INSTANCE_CUSTOM.y : 1.0);
	// 위쪽일수록 많이 흔들린다 (UV.y = 0 이 꼭대기)
	float top = 1.0 - UV.y;
	float t = world_time;
	float w = sin(t * 1.7 + phase * 6.28) * 0.6 + sin(t * 3.1 + phase * 11.0) * 0.4;
	VERTEX.x += w * amount * top * top * (wind.x + 0.4);
	wpos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	// POSITION 은 조건 없이 써야 한다 — 한 갈래라도 안 쓰면 그 갈래는 초기화 안 된 값으로 그려진다
	vec3 vp = (MODELVIEW_MATRIX * vec4(VERTEX, 1.0)).xyz;
	vp *= 1.0 - depth_pull / length(vp);
	POSITION = PROJECTION_MATRIX * vec4(vp, 1.0);
}

void fragment() {
	vec2 uv = UV;
	if (frame_count.x * frame_count.y > 1.0) {
		float f = floor(frame + iframe + 0.5);
		vec2 cell = vec2(mod(f, frame_count.x), floor(f / frame_count.x));
		uv = (cell + UV) / frame_count;
	}
	vec4 tex = texture(albedo_tex, uv) * tint * itint;
	if (tex.a < 0.5) discard;
	// 소품(캐릭터·풀포기 아님)이 주인공보다 앞(남쪽)에 서서 겹치면 한 칸 걸러 비운다 — 픽셀 게임식 투시
	if (depth_pull == 0.0 && !instance_tint && wpos.z > hero_z + 0.3) {
		vec2 d = (FRAGCOORD.xy - hero_screen) / SEE_THROUGH;
		float r = dot(d, d);
		vec2 px = mod(floor(FRAGCOORD.xy), 2.0);
		// 가운데는 네 칸에 한 칸만 남기고, 가장자리는 바둑판 — 경계가 딱 잘리지 않게
		bool keep = r < 0.55 ? (px.x + px.y == 0.0) : (px.x == px.y);
		if (r < 1.0 && !keep) discard;
	}

	vec3 col = mix(tex.rgb, flash_color.rgb, flash);
	ALBEDO = slow_exempt ? col : slow_tint(col);
	ROUGHNESS = 1.0;
	// 빛 계산용 가짜 법선: 위쪽 + 약간 카메라 쪽
	NORMAL = normalize((VIEW_MATRIX * vec4(0.0, 0.86, 0.5, 0.0)).xyz);

	if (glow_at_night > 0.0) {
		float lum = dot(tex.rgb, vec3(0.3, 0.59, 0.11));
		float warm = step(tex.b + 0.12, tex.r) * smoothstep(0.62, 0.85, lum);
		EMISSION = tex.rgb * warm * glow_at_night * night * 2.5;
	}
	EMISSION += flash_color.rgb * flash * 0.6;
	cloud = cloud_shade(wpos.xz, world_time, wind);
}

void light() {
	float ndl = clamp(dot(NORMAL, LIGHT), 0.0, 1.0);
	float lit = 0.45 + 0.55 * ndl;
	float shade = smoothstep(0.25, 0.75, ATTENUATION);
	if (LIGHT_IS_DIRECTIONAL) shade *= cloud;
	DIFFUSE_LIGHT += LIGHT_COLOR / PI * lit * shade;
}
