// 지형 — 텍스처 없이 픽셀 격자 위에 지면 종류별 무늬를 그린다.
// 같은 타일 텍스처를 반복하면 넓은 들판에서 반복이 눈에 띈다. 무늬를 좌표 해시로 만들면 반복이 없다.
shader_type spatial;
render_mode diffuse_lambert, specular_disabled, cull_back;

#include "res://shaders/common.gdshaderinc"

global uniform float world_time;
global uniform float night;
global uniform vec2 wind;
global uniform vec2 map_size;
global uniform sampler2D ground_map : filter_nearest;
global uniform sampler2D water_map : filter_linear;

varying vec3 wpos;
varying vec3 wnorm;
varying float cloud;

// 지면 종류 (tools/mapgen/mapgen.py 와 같은 번호)
const int GRASS = 0;
const int FOREST = 1;
const int MEADOW = 2;
const int DIRT = 3;
const int COBBLE = 4;
const int SAND = 5;
const int SAKURA = 6;
const int AUTUMN = 7;
const int GRAVEL = 8;
const int PEBBLE = 9;

void vertex() {
	wpos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	wnorm = normalize((MODEL_MATRIX * vec4(NORMAL, 0.0)).xyz);
}

int ground_at(vec2 xz, vec2 tp) {
	// 칸 경계를 잡음으로 흔들어 1m 계단이 보이지 않게 한다. 흔드는 양도 픽셀 격자에 맞춘다.
	vec2 jit = (vec2(vnoise(tp * 0.09), vnoise(tp * 0.09 + 17.3)) - 0.5) * 1.3;
	ivec2 cell = ivec2(floor(xz + jit));
	cell = clamp(cell, ivec2(0), ivec2(map_size) - 1);
	return int(texelFetch(ground_map, cell, 0).r * 255.0 + 0.5);
}

vec3 grass_color(vec2 tp, float big, vec3 base) {
	float h = hash21(tp);
	vec3 c = base * (0.9 + 0.2 * big);
	// 풀잎: 세로 2픽셀짜리 밝은 획과 어두운 획
	float blade = hash21(vec2(tp.x, floor(tp.y * 0.5)));
	if (blade > 0.9) c *= 1.16;
	else if (blade < 0.08) c *= 0.84;
	if (h > 0.985) c = mix(c, vec3(0.95, 0.95, 0.55), 0.5);
	return c;
}

vec3 ground_color(int t, vec2 tp, float big) {
	float h = hash21(tp);
	float h2 = hash21(floor(tp * 0.5));
	if (t == GRASS) return grass_color(tp, big, vec3(0.34, 0.64, 0.22));
	if (t == FOREST) {
		vec3 c = grass_color(tp, big, vec3(0.22, 0.42, 0.22));
		if (h2 > 0.93) c = mix(c, vec3(0.45, 0.32, 0.18), 0.7); // 낙엽
		return c;
	}
	if (t == MEADOW) {
		vec3 c = grass_color(tp, big, vec3(0.44, 0.70, 0.24));
		float f = hash21(floor(tp * 0.5) + 31.0);
		if (f > 0.965) {
			float k = hash21(floor(tp * 0.5) + 7.0);
			c = k < 0.3 ? vec3(0.98, 0.97, 0.9) : k < 0.55 ? vec3(0.99, 0.85, 0.3) : k < 0.8 ? vec3(0.97, 0.55, 0.7) : vec3(0.65, 0.6, 0.98);
		}
		return c;
	}
	if (t == DIRT) {
		vec3 c = vec3(0.68, 0.53, 0.35) * (0.92 + 0.14 * big);
		if (h > 0.95) c *= 1.18;
		else if (h < 0.06) c *= 0.8;
		if (h2 > 0.97) c = vec3(0.78, 0.72, 0.62); // 자갈
		return c;
	}
	if (t == COBBLE) {
		// 엇갈린 돌 격자 (돌 하나 10x7 픽셀)
		vec2 p = tp;
		float row = floor(p.y / 7.0);
		p.x += mod(row, 2.0) * 5.0;
		vec2 cellc = floor(vec2(p.x / 10.0, row));
		vec2 f = vec2(mod(p.x, 10.0), mod(p.y, 7.0));
		float tint = hash21(cellc);
		vec3 c = vec3(0.62, 0.6, 0.57) * (0.86 + 0.22 * tint);
		if (f.x < 1.0 || f.y < 1.0) c = vec3(0.38, 0.36, 0.34);
		else if (f.y < 2.0) c *= 1.1;
		return c;
	}
	if (t == SAND) {
		vec3 c = vec3(0.92, 0.85, 0.64) * (0.95 + 0.08 * big);
		if (h > 0.96) c *= 0.9;
		if (h < 0.02) c = vec3(0.98, 0.96, 0.9);
		return c;
	}
	if (t == SAKURA) {
		vec3 c = grass_color(tp, big, vec3(0.40, 0.62, 0.26));
		// 꽃잎은 뭉텅이로 쌓인다 — 고르게 뿌리면 지지직거리는 잡음처럼 보인다
		float drift = vnoise(tp * 0.045);
		float p = hash21(floor(tp * 0.5) + 3.0);
		if (p > mix(0.97, 0.35, smoothstep(0.35, 0.8, drift)))
			c = mix(vec3(0.99, 0.80, 0.87), vec3(0.93, 0.56, 0.70), step(0.6, hash21(tp + 9.0)));
		return c;
	}
	if (t == AUTUMN) {
		vec3 c = grass_color(tp, big, vec3(0.5, 0.52, 0.24));
		float p = hash21(floor(tp * 0.5) + 5.0);
		if (p > 0.55) {
			float k = hash21(floor(tp * 0.5) + 13.0);
			c = k < 0.4 ? vec3(0.9, 0.42, 0.14) : k < 0.75 ? vec3(0.78, 0.2, 0.12) : vec3(0.95, 0.72, 0.2);
		}
		return c;
	}
	// 자갈 바닥과 물가 조약돌: 3x3 픽셀 덩어리마다 다른 회색
	vec2 blk = floor(tp / 3.0);
	float k = hash21(blk);
	vec3 base = t == GRAVEL ? vec3(0.5, 0.48, 0.44) : vec3(0.66, 0.63, 0.57);
	return base * (0.8 + 0.35 * k);
}

vec3 rock_color(vec3 p, vec3 n) {
	// 절벽은 세로면이라 xz 로 무늬를 그리면 늘어난다. 수평 방향 + 높이로 그린다.
	float horiz = abs(n.x) > abs(n.z) ? p.z : p.x;
	vec2 tp = floor(vec2(horiz, -p.y) * PPM);
	float band = floor(p.y * 2.2 + vnoise(vec2(horiz * 0.4, 3.0)) * 1.4);
	// 차가운 회청색 바위, 층마다 밝기가 다르다. 따뜻한 베이지는 모래로 읽혔다.
	vec3 c = mix(vec3(0.42, 0.44, 0.5), vec3(0.58, 0.56, 0.58), hash21(vec2(band, 7.0)));
	float crack = hash21(vec2(floor(tp.x / 3.0), tp.y));
	if (crack > 0.93) c *= 0.72;
	if (hash21(tp) > 0.96) c *= 1.15;
	// 층 사이 이끼 줄
	if (fract(p.y * 2.2 + vnoise(vec2(horiz * 0.4, 3.0)) * 1.4) < 0.08) c = mix(c, vec3(0.3, 0.46, 0.24), 0.6);
	return c;
}

void fragment() {
	vec2 tp = ground_texel(wpos.xz);
	float big = fbm3(wpos.xz * 0.06);
	int t = ground_at(wpos.xz, tp);
	vec3 col = ground_color(t, tp, big);

	// 가파른 면은 암벽
	float slope = 1.0 - wnorm.y;
	float jitter = (hash21(tp + 71.0) - 0.5) * 0.06;
	float rock = smoothstep(0.40, 0.46, slope + jitter);
	col = mix(col, rock_color(wpos, wnorm), rock);

	// 물가는 젖어서 짙다
	float w = texture(water_map, (wpos.xz + 0.5) / (map_size + 1.0)).r;
	if (w > -100.0) {
		float wet = smoothstep(0.35, 0.0, wpos.y - w);
		col *= mix(1.0, 0.72, wet);
	}

	cloud = cloud_shade(wpos.xz, world_time, wind);
	ALBEDO = slow_tint(col);
	ROUGHNESS = 1.0;
}

void light() {
	float ndl = clamp(dot(NORMAL, LIGHT), 0.0, 1.0);
	// 3단계로 눌러 만화처럼. 완전히 계단지면 언덕에 등고선이 생겨서 60% 만 섞는다.
	float q = floor(ndl * 3.0 + 0.5) / 3.0;
	float lit = 0.35 + 0.65 * mix(ndl, q, 0.6);
	float shade = smoothstep(0.25, 0.75, ATTENUATION);
	if (LIGHT_IS_DIRECTIONAL) shade *= cloud;
	DIFFUSE_LIGHT += LIGHT_COLOR / PI * lit * shade;
}
