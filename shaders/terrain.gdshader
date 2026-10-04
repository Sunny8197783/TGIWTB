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
const int SNOW = 10;
const int TULIP = 11;
const int LAVENDER = 12;
const int MOSS = 13;
const int ROCKY = 14;
const int STONE = 15;
const int GINKGO = 16;
const int WHEAT = 17;
const int ICE = 18;
const float ROW_W = 1.6; // 꽃밭·밀밭 한 줄 (m) — World/GrassBuilder.RowWidth 와 같다

global uniform sampler2D height_map : filter_linear;
// 소품 밑동·숲 바닥 그늘 (World/PropBuilder.BakeAo)
global uniform sampler2D ao_map : filter_linear;

varying float dapple;

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

// 튤립 줄 색 — GrassBuilder 의 꽃송이와 같은 계산이라 풀이 끝나는 거리에서 색이 바뀌지 않는다
vec3 tulip_color(vec2 xz) {
	int row = int(floor(xz.y / ROW_W));
	int block = int(floor(xz.x / 48.0)) * 31 + int(floor(xz.y / 40.0)) * 17;
	uint idx = (uint(row) * 2654435761u + uint(block) * 40503u) % 6u;
	if (idx == 0u) return vec3(0.95, 0.25, 0.3);
	if (idx == 1u) return vec3(0.98, 0.85, 0.25);
	if (idx == 2u) return vec3(0.98, 0.96, 0.9);
	if (idx == 3u) return vec3(0.95, 0.5, 0.7);
	if (idx == 4u) return vec3(0.65, 0.35, 0.85);
	return vec3(0.98, 0.55, 0.2);
}

// 멀리서 픽셀 무늬가 지글거리지 않도록 바꿔 칠 평균색
vec3 ground_mean(int t, vec2 xz) {
	if (t == GRASS) return vec3(0.34, 0.64, 0.22);
	if (t == FOREST) return vec3(0.22, 0.42, 0.2);
	if (t == MEADOW) return vec3(0.46, 0.7, 0.26);
	if (t == DIRT) return vec3(0.68, 0.53, 0.35);
	if (t == COBBLE) return vec3(0.58, 0.56, 0.53);
	if (t == SAND) return vec3(0.92, 0.85, 0.64);
	if (t == SAKURA) return vec3(0.62, 0.62, 0.42);
	if (t == AUTUMN) return vec3(0.72, 0.42, 0.18);
	if (t == SNOW) return vec3(0.9, 0.93, 0.98);
	if (t == TULIP) return mix(vec3(0.26, 0.5, 0.22), tulip_color(xz), 0.6);
	if (t == LAVENDER) return vec3(0.5, 0.42, 0.72);
	if (t == MOSS) return vec3(0.2, 0.4, 0.2);
	if (t == ROCKY) return vec3(0.5, 0.5, 0.52);
	if (t == STONE) return vec3(0.66, 0.64, 0.6);
	if (t == GINKGO) return vec3(0.82, 0.7, 0.26);
	if (t == WHEAT) return vec3(0.84, 0.7, 0.34);
	if (t == ICE) return vec3(0.7, 0.84, 0.92);
	return vec3(0.58, 0.55, 0.5);
}

vec3 ground_color(int t, vec2 tp, float big) {
	float h = hash21(tp);
	float h2 = hash21(floor(tp * 0.5));
	if (t == GRASS) return grass_color(tp, big, vec3(0.34, 0.64, 0.22));
	if (t == FOREST) {
		// 이끼 얼룩: 짙은 초록과 볕 든 연두가 큰 덩어리로 번갈아
		// 부드러운 잡음을 3단으로 끊는다 (픽셀아트식 색 띠). 값 잡음을 그대로 쓰면 격자 모양이 보였다
		float moss = floor(clamp((fbm3(tp / PPM * 0.22) - 0.3) * 2.5, 0.0, 0.99) * 3.0) / 2.0;
		vec3 c = grass_color(tp, big, mix(vec3(0.16, 0.36, 0.2), vec3(0.3, 0.5, 0.2), moss));
		// 낙엽은 흩뿌린 점이 아니라 무더기로 모인다
		float litter = vnoise(tp * 0.06 + 40.0);
		if (litter > 0.64 && hash21(floor(tp * 0.5) + 13.0) > 0.6) {
			float k = hash21(floor(tp * 0.5) + 3.0);
			c = k < 0.4 ? vec3(0.55, 0.36, 0.18) : k < 0.7 ? vec3(0.7, 0.46, 0.2) : vec3(0.4, 0.27, 0.16);
		}
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
	vec2 xz = tp / vec2(PPM, PPM_Z);
	if (t == SNOW) {
		// 눈: 바람이 빚은 물결 무늬(밝은 등성이·푸른 골)를 3단으로, 드문드문 반짝이
		float ripple = vnoise(tp * vec2(0.035, 0.16) + vec2(0.0, vnoise(tp * 0.02) * 6.0));
		float k = floor(clamp(ripple * 1.4 - 0.2 + (big - 0.5) * 0.6, 0.0, 0.99) * 3.0) / 2.0;
		vec3 c = mix(vec3(0.74, 0.8, 0.92), vec3(0.97, 0.98, 1.0), k);
		if (h > 0.993) c = vec3(1.0);
		else if (h < 0.03) c *= 0.93;
		return c;
	}
	if (t == ICE) {
		// 얼음: 맑은 하늘색, 흰 금이 몇 줄
		vec3 c = mix(vec3(0.62, 0.8, 0.9), vec3(0.78, 0.9, 0.96), vnoise(tp * 0.03));
		float crack = abs(vnoise(tp * 0.02 + 7.0) - 0.5);
		if (crack < 0.012) c = vec3(0.95, 0.98, 1.0);
		return c;
	}
	if (t == TULIP || t == LAVENDER || t == WHEAT) {
		// 밭: 줄마다 작물, 줄 사이 고랑은 흙
		float row = fract(xz.y / ROW_W);
		if (row > 0.75) return vec3(0.5, 0.36, 0.24) * (0.9 + 0.2 * h);
		if (t == WHEAT) {
			vec3 c = vec3(0.86, 0.72, 0.36) * (0.9 + 0.2 * big);
			float stalk = hash21(vec2(tp.x, floor(tp.y * 0.34)));
			if (stalk > 0.8) c *= 1.12; else if (stalk < 0.15) c *= 0.82;
			return c;
		}
		vec3 leaf = grass_color(tp, big, vec3(0.24, 0.48, 0.22));
		vec3 bloom = t == TULIP ? tulip_color(xz) : mix(vec3(0.55, 0.4, 0.82), vec3(0.68, 0.52, 0.9), h);
		return hash21(floor(tp * 0.5) + 3.0) > 0.45 ? bloom : leaf;
	}
	if (t == MOSS) {
		// 고목의 숲 이끼: 짙은 초록에 볕 든 연두 얼룩, 검은 흙 점
		float moss = floor(clamp((fbm3(tp / PPM * 0.3) - 0.25) * 2.4, 0.0, 0.99) * 3.0) / 2.0;
		vec3 c = mix(vec3(0.14, 0.32, 0.18), vec3(0.32, 0.52, 0.22), moss);
		if (h > 0.97) c = vec3(0.12, 0.16, 0.1);
		else if (h < 0.03) c *= 1.2;
		return c;
	}
	if (t == ROCKY) {
		// 자갈 비탈: 크기가 들쭉날쭉한 돌, 줄눈 없이 돌마다 밝기만 다르다
		vec2 q = tp + floor(vec2(vnoise(tp * 0.2), vnoise(tp * 0.2 + 5.0)) * 3.0);
		vec2 blk = floor(q / vec2(4.0, 3.0));
		vec3 c = vec3(0.5, 0.5, 0.54) * (0.78 + 0.34 * hash21(blk));
		if (hash21(blk + 3.0) > 0.7) c = mix(c, vec3(0.92, 0.94, 0.98), 0.7); // 돌 틈에 남은 눈
		return c;
	}
	if (t == STONE) {
		// 돌계단·판석: 큰 돌판, 줄눈은 어둡게, 윗변은 밝게
		vec2 p = tp;
		float row = floor(p.y / 9.0);
		p.x += hash21(vec2(row, 3.0)) * 14.0;
		vec2 f = vec2(mod(p.x, 14.0), mod(p.y, 9.0));
		vec3 c = vec3(0.68, 0.66, 0.62) * (0.86 + 0.2 * hash21(vec2(floor(p.x / 14.0), row)));
		if (f.x < 1.0 || f.y < 1.0) c = vec3(0.36, 0.34, 0.32);
		else if (f.y < 2.0) c *= 1.12;
		return c;
	}
	if (t == GINKGO) {
		vec3 c = grass_color(tp, big, vec3(0.48, 0.56, 0.24));
		float drift = vnoise(tp * 0.05 + 11.0);
		if (hash21(floor(tp * 0.5) + 9.0) > mix(0.9, 0.3, smoothstep(0.3, 0.75, drift)))
			c = mix(vec3(0.98, 0.84, 0.28), vec3(0.9, 0.66, 0.16), step(0.6, hash21(tp + 2.0)));
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
	// 지층: 층마다 색(차갑고 따뜻하게 번갈아), 윗변은 볕 받는 턱, 아랫변은 그늘
	float layer = p.y * 0.7 + vnoise(vec2(horiz * 0.35, 3.0)) * 1.2;
	float band = floor(layer);
	float f = fract(layer);
	vec3 c = mix(vec3(0.36, 0.36, 0.42), vec3(0.52, 0.5, 0.52), hash21(vec2(band, 7.0)));
	c *= mix(vec3(1.0), vec3(1.07, 1.0, 0.9), hash21(vec2(band, 9.0)));
	if (f > 0.9) c *= 1.2;
	else if (f < 0.12) c *= 0.68;
	// 세로 틈 (3픽셀 칸마다 드문드문)
	if (hash21(vec2(floor(tp.x / 3.0), band)) > 0.8 && mod(tp.x, 3.0) < 1.0) c *= 0.64;
	float h = hash21(tp);
	if (h > 0.97) c *= 1.12;
	else if (h < 0.04) c *= 0.85;
	// 턱 위 이끼·풀 덩어리
	if (f > 0.84 && vnoise(tp * 0.08) > 0.55) c = mix(c, vec3(0.26, 0.44, 0.2), 0.85);
	return c;
}

void fragment() {
	vec2 tp = ground_texel(wpos.xz);
	int t = ground_at(wpos.xz, tp);
	// 멀어서 한 텍셀이 한 픽셀보다 작아지면 무늬 대신 평균색 (지글거림 방지). 아주 멀면 무늬 계산을 아예 건너뛴다 (전망 화면 절반이 먼 땅)
	float fp = length(fwidth(wpos.xz)) * PPM;
	bool far = fp > 3.0;
	vec3 col = ground_mean(t, wpos.xz);
	if (!far) {
		float big = fbm3(wpos.xz * 0.06);
		col = mix(ground_color(t, tp, big), col, smoothstep(1.4, 3.0, fp));
	}

	// 가파른 면은 암벽
	float slope = 1.0 - wnorm.y;
	float jitter = (hash21(tp + 71.0) - 0.5) * 0.06;
	float rock = smoothstep(0.40, 0.46, slope + jitter);
	vec3 rc = rock_color(wpos, wnorm);
	// 높은 산 바위의 덜 가파른 곳엔 눈이 쌓인다
	if (wpos.y > 52.0 + 8.0 * vnoise(tp * 0.01)) rc = mix(rc, vec3(0.9, 0.93, 0.98), step(slope, 0.62 + 0.1 * hash21(tp)));
	// 절벽 위 가장자리: 풀이 처마처럼 넘어온다 (오르막 쪽 0.7m 가 지금 높이와 비슷하면 꼭대기 근처).
	// 오르막 = 법선의 수평 성분 반대쪽 — '북쪽'으로만 보면 동서로 누운 강둑에 톱니가 생겼다
	vec2 huv = (wpos.xz + vec2(0.5)) / (map_size + 1.0);
	float lip = 0.0;
	if (!far && rock > 0.0) {
		vec2 uphill = -normalize(wnorm.xz + vec2(1e-4));
		float h_up = texture(height_map, huv + uphill * 0.7 / (map_size + 1.0)).r;
		lip = step(h_up - wpos.y, 0.22 + 0.25 * hash21(vec2(tp.x, 5.0)));
	}
	col = mix(col, rc, rock * (1.0 - lip * 0.85));
	// 절벽 밑동 그늘: 바로 북쪽이 훌쩍 높으면 땅을 어둡게 (3단)
	if (!far) {
		float h_back = texture(height_map, huv - vec2(0.0, 1.6) / (map_size + 1.0)).r;
		float cliff_ao = floor(clamp((h_back - wpos.y - 1.0) / 3.0, 0.0, 1.0) * 3.0) / 3.0;
		col *= 1.0 - 0.28 * cliff_ao * (1.0 - rock);
	}

	// 물가는 젖어서 짙다
	float w = texture(water_map, (wpos.xz + 0.5) / (map_size + 1.0)).r;
	if (w > -100.0) {
		float wet = smoothstep(0.35, 0.0, wpos.y - w);
		col *= mix(1.0, 0.72, wet);
	}

	// 땅 그늘: 4단으로 끊고 단 경계의 좁은 띠만 바둑판으로 섞는다 (넓게 섞으면 격자 무늬가 보였다)
	float ao = texture(ao_map, wpos.xz / map_size).r * 4.0;
	float ao_band = floor(ao);
	if (ao - ao_band > 0.82 && mod(tp.x + tp.y, 2.0) < 1.0) ao_band += 1.0;
	float ao_q = ao_band / 4.0;
	col *= 1.0 - clamp(ao_q, 0.0, 0.6) * (1.0 - rock);

	cloud = cloud_shade(wpos.xz, world_time, wind);
	// 숲 그늘에 나뭇잎 사이로 드는 햇빛 조각 (바람에 천천히 흔들린다). 그늘 안에서만 보인다 — light() 참고
	bool woods = t == FOREST || t == SAKURA || t == AUTUMN || t == MOSS || t == GINKGO;
	dapple = woods ? step(0.68, vnoise(tp * 0.05 + wind * world_time * 1.5)) : 0.0;
	ALBEDO = slow_tint(col);
	ROUGHNESS = 1.0;
}

void light() {
	float ndl = clamp(dot(NORMAL, LIGHT), 0.0, 1.0);
	// 3단계로 눌러 만화처럼. 완전히 계단지면 언덕에 등고선이 생겨서 60% 만 섞는다.
	float q = floor(ndl * 3.0 + 0.5) / 3.0;
	float lit = 0.35 + 0.65 * mix(ndl, q, 0.6);
	float shade = smoothstep(0.25, 0.75, ATTENUATION);
	if (LIGHT_IS_DIRECTIONAL) shade = max(shade, dapple * 0.8) * cloud;
	// 등불: 거리 감쇠를 4단으로 끊어 픽셀아트식 빛 웅덩이 (둥근 계단)
	// Godot 점광원 감쇠는 등불 높이(약 2m)에서 이미 0.3 남짓 — 0..1 로 늘린 뒤 끊는다
	// 바깥으로 갈수록 빨리 어두워지게 제곱한 뒤 3단으로 끊는다
	else { float a = clamp(ATTENUATION * 3.0, 0.0, 1.0); shade = floor(a * a * 3.0 + 0.5) / 3.0; lit = 1.0; }
	DIFFUSE_LIGHT += LIGHT_COLOR / PI * lit * shade;
}
