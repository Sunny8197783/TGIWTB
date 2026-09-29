// 셰이더 공통 — 해시/노이즈, 픽셀 격자, 구름 그림자.
// 월드 1m = 32 픽셀(PIXELS_PER_METER). 무늬를 이 격자에 맞춰야 저해상도 화면에서 픽셀아트로 읽힌다.

const float PPM = 32.0;

// 땅은 38° 로 비스듬히 보여서 남북 방향이 cos 가 아니라 sin(38°)=0.616 배로 눌린다.
// 무늬 격자를 남북으로 그만큼 성기게 잡아야 화면에서 정사각 픽셀이 된다(안 그러면 세로 줄무늬).
const float PPM_Z = 32.0 * 0.616;

vec2 ground_texel(vec2 xz) {
	return floor(xz * vec2(PPM, PPM_Z));
}

float hash21(vec2 p) {
	p = fract(p * vec2(123.34, 456.21));
	p += dot(p, p + 45.32);
	return fract(p.x * p.y);
}

vec2 hash22(vec2 p) {
	float n = hash21(p);
	return vec2(n, hash21(p + n + 17.0));
}

float vnoise(vec2 p) {
	vec2 i = floor(p);
	vec2 f = fract(p);
	vec2 u = f * f * (3.0 - 2.0 * f);
	float a = hash21(i);
	float b = hash21(i + vec2(1.0, 0.0));
	float c = hash21(i + vec2(0.0, 1.0));
	float d = hash21(i + vec2(1.0, 1.0));
	return mix(mix(a, b, u.x), mix(c, d, u.x), u.y);
}

float fbm3(vec2 p) {
	return vnoise(p) * 0.55 + vnoise(p * 2.03 + 11.7) * 0.3 + vnoise(p * 4.11 + 3.1) * 0.15;
}

// 흘러가는 구름 그림자. 1 = 해, 0.55 = 구름 밑.
float cloud_shade(vec2 xz, float t, vec2 wind_dir) {
	vec2 p = xz * 0.018 + wind_dir * t * 0.012;
	float c = fbm3(p);
	return mix(1.0, 0.62, smoothstep(0.52, 0.66, c));
}

// 완벽 회피 슬로우: 세계는 채도가 빠지고 보랏빛으로 가라앉는다. 주인공 스프라이트는 빠진다(slow_exempt).
global uniform float time_slow;
vec3 slow_tint(vec3 c) {
	float l = dot(c, vec3(0.3, 0.59, 0.11));
	vec3 grey = vec3(l) * vec3(0.8, 0.74, 1.08);
	return mix(c, grey, time_slow * 0.78);
}
