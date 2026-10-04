// 픽셀 스프라이트 — 소품·나무·건물·캐릭터 공통.
// 판은 카메라를 향해 서 있지만 빛은 판의 법선이 아니라 그림의 '부피'로 받는다:
// tools/normals.py 가 외곽선에서 잰 거리로 둥근 법선 지도(_n.png)를 굽는다. 해 쪽이 밝고 반대쪽이 어둡고,
// 해를 등진 가장자리는 테두리 빛으로 빛난다(역광 실루엣). 법선 지도가 없으면 '위로 비스듬한 면' 하나.
shader_type spatial;
render_mode diffuse_lambert, specular_disabled, cull_disabled, depth_draw_opaque;

#include "res://shaders/common.gdshaderinc"

global uniform float world_time;
global uniform float night;
global uniform vec2 wind;
// 주인공 가슴의 화면 위치(640x360 픽셀)와 발 z — 앞을 가린 소품을 바둑판으로 비워 주인공을 보여 준다 (Combat/Hero)
global uniform vec2 hero_screen;
global uniform float hero_z;
// 서 있는 판의 세로 보정: 38° 기준으로 늘려 세운 판을 지금 카메라 피치에 맞춘다 (전망에서 고개를 들면 덜 늘린다)
global uniform float upright_fix;
// 땅 그늘 지도 (World/PropBuilder.BakeAo) — 풀포기·캐릭터가 나무 밑에 들어가면 같이 어두워진다
global uniform sampler2D ao_map : filter_linear;
global uniform vec2 map_size;
const vec2 SEE_THROUGH = vec2(15.0, 27.0); // 비우는 타원 반지름 (픽셀). 주인공 그림이 약 30x56

// 밉맵: 가까이(1텍셀 = 1픽셀)는 0단 그대로, 멀리서만 흐린 단으로 — 먼 숲이 지글거리지 않게
uniform sampler2D albedo_tex : source_color, filter_nearest_mipmap, repeat_disable;
uniform sampler2D normal_tex : hint_normal, filter_nearest_mipmap, repeat_disable;
uniform bool has_normal = false;
uniform float sway = 0.0;         // 바람 흔들림 (m). 나무 0.04, 풀 0.03
uniform float glow_at_night = 0.0; // 밝은 난색 픽셀을 밤에 빛나게 (등불)
uniform float flash = 0.0;         // 피격 섬광 세기
uniform vec4 flash_color : source_color = vec4(1.0); // 흰색 = 맞음, 주황 = 공격 예고, 빨강 = 주인공 피격
uniform vec4 tint : source_color = vec4(1.0);
uniform vec2 frame_count = vec2(1.0, 1.0); // 시트 가로/세로 칸 수 (캐릭터 애니메이션)
uniform float frame = 0.0;
uniform bool instance_tint = false; // MultiMesh 인스턴스 색을 곱한다 (풀포기)
// MultiMesh 인스턴스 색 = 아틀라스 안의 제 칸 (u0, u1, 위 v). 한 종류의 그림 여러 장을 한 장에 이어 붙였다 (PropBuilder)
uniform bool atlas = false;
// 서 있는 판을 시선 방향으로 카메라 쪽에 당긴다 (m). 화면 위치는 그대로라 픽셀이 안 흔들리고,
// 비탈을 오를 때 발이 앞쪽 땅에 파묻혀 잘리지 않는다. 캐릭터 0.6, 소품 0.
uniform float depth_pull = 0.0;
uniform bool slow_exempt = false; // 주인공: 슬로우 중에도 제 색
// 그림 아래쪽을 땅 그늘로 살짝 어둡게 (판이 땅에 '붙어' 보이게). 캐릭터·풀은 0
uniform float contact_ao = 0.0;

varying vec3 wpos;
varying float cloud;
varying float phase;
varying vec4 itint;
varying vec4 irect;
varying float iframe;
varying float mirror;
varying float rim_mask;
varying float base_h;
varying float ground_ao;

const float MIP_BIAS = -1.0;

void vertex() {
	phase = INSTANCE_CUSTOM.x;
	itint = instance_tint ? COLOR : vec4(1.0);
	irect = COLOR;
	iframe = INSTANCE_CUSTOM.z; // MultiMesh 인스턴스마다 다른 그림 칸 (풀포기)
	mirror = sign(MODEL_MATRIX[0].x);
	base_h = VERTEX.y * length(MODEL_MATRIX[1].xyz); // 땅에서 잰 높이 (m)
	// 제 그림 밑동 그늘은 contact_ao 가 맡는다 (나무가 제 타원에 두 번 어두워지지 않게)
	ground_ao = contact_ao > 0.0 ? 0.0 : textureLod(ao_map, MODEL_MATRIX[3].xz / map_size, 0.0).r;
	VERTEX.y *= upright_fix;
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
	if (atlas) {
		uv = vec2(mix(irect.x, irect.y, UV.x), mix(irect.z, 1.0, UV.y));
	} else if (frame_count.x * frame_count.y > 1.0) {
		float f = floor(frame + iframe + 0.5);
		vec2 cell = vec2(mod(f, frame_count.x), floor(f / frame_count.x));
		uv = (cell + UV) / frame_count;
	}
	vec4 tex = texture(albedo_tex, uv, MIP_BIAS) * tint * itint;
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
	// 땅에 닿은 아래쪽 0.5m 남짓을 어둡게 (3단으로 끊어 픽셀아트식)
	if (contact_ao > 0.0) {
		float k = floor(clamp(base_h / 0.6, 0.0, 1.0) * 3.0) / 3.0;
		col *= 1.0 - contact_ao * (1.0 - k);
	}
	col *= 1.0 - floor(clamp(ground_ao, 0.0, 0.6) * 4.0) / 4.0;
	ALBEDO = slow_exempt ? col : slow_tint(col);
	ROUGHNESS = 1.0;

	// 법선: 그림 공간(오른쪽 x, 위 y, 앞 z) → 월드. 평평한 속(0,0,1)은 '위로 37° 든 면' — 해가 북쪽에 있어도 새카맣지 않게
	vec3 ns = has_normal ? normalize(texture(normal_tex, uv, MIP_BIAS).xyz * 2.0 - 1.0) : vec3(0.0, 0.0, 1.0);
	vec3 T = vec3(mirror, 0.0, 0.0);
	vec3 B = vec3(0.0, 0.8, -0.6);
	vec3 N0 = vec3(0.0, 0.6, 0.8);
	vec3 nw = normalize(T * ns.x + B * ns.y + N0 * ns.z);
	NORMAL = normalize((VIEW_MATRIX * vec4(nw, 0.0)).xyz);
	rim_mask = has_normal ? clamp((1.0 - ns.z) * 2.2, 0.0, 1.0) : 0.0;

	if (glow_at_night > 0.0) {
		float lum = dot(tex.rgb, vec3(0.3, 0.59, 0.11));
		float warm = step(tex.b + 0.12, tex.r) * smoothstep(0.62, 0.85, lum);
		EMISSION = tex.rgb * warm * glow_at_night * night * 2.5;
	}
	EMISSION += flash_color.rgb * flash * 0.6;
	cloud = cloud_shade(wpos.xz, world_time, wind);
}

void light() {
	float d = clamp(dot(NORMAL, LIGHT), 0.0, 1.0);
	// 3단 툰: 부드러운 음영을 색 띠로 끊는다 (완전히 끊으면 얼룩져서 70% 만)
	float band = floor(d * 3.0 + 0.5) / 3.0;
	float lit = 0.4 + 0.6 * mix(d, band, 0.7);
	float shade = smoothstep(0.25, 0.75, ATTENUATION);
	if (LIGHT_IS_DIRECTIONAL) {
		shade *= cloud;
		// 테두리 빛: 해를 향한 가장자리만. 해가 등 뒤(북쪽)에 있으면 세게 — 노을 역광에 실루엣이 금빛으로.
		// 제 그림자 입체에 가려도 테두리는 빛난다 (빛이 몸을 스쳐 지나가는 자리)
		float back = clamp(-dot(LIGHT, VIEW), 0.0, 1.0);
		float rim = step(0.45, rim_mask * d) * (0.6 + 2.6 * back);
		DIFFUSE_LIGHT += LIGHT_COLOR / PI * rim * cloud;
	}
	DIFFUSE_LIGHT += LIGHT_COLOR / PI * lit * shade;
}
