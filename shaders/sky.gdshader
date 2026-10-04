// 하늘 — 낮/노을/밤 색은 DayNight.cs 가 전역값으로 넣어 준다.
shader_type sky;

#include "res://shaders/common.gdshaderinc"

// 전역값을 쓰지 않는다. 전역값은 매 프레임 바뀌고, 하늘 셰이더 입력이 바뀌면 Godot 는 하늘 광원 맵을
// 매 프레임 다시 굽는다(이 노트북에서 프레임 예산을 넘긴 원인). DayNight 가 1/4초마다 아래 값을 넣는다.
uniform float world_time;
uniform float night;
uniform vec3 sun_dir = vec3(0.3, 0.8, 0.5);
uniform vec4 sun_color : source_color = vec4(1.0);
uniform vec4 sky_horizon : source_color = vec4(0.7, 0.85, 1.0, 1.0);
uniform vec4 sky_zenith : source_color = vec4(0.3, 0.55, 0.95, 1.0);

uniform vec3 moon_dir = vec3(-0.3, 0.6, -0.7);
uniform float sunset = 0.0; // 0..1, 해가 지평선 가까울수록 1
uniform float aurora = 0.0; // 0..1, 밤 북쪽 하늘의 오로라 (서리 고원에서 짙다)

void sky() {
	vec3 d = EYEDIR;
	float up = clamp(d.y, 0.0, 1.0);
	vec3 col = mix(sky_horizon.rgb, sky_zenith.rgb, pow(up, 0.55));
	// 지평선 아래는 조금 어둡게 — 바다 끝이 하늘과 붙어 보이게
	if (d.y < 0.0) col = mix(sky_horizon.rgb, sky_horizon.rgb * 0.75, clamp(-d.y * 4.0, 0.0, 1.0));

	vec3 s = normalize(sun_dir);
	float sd = max(dot(d, s), 0.0);
	// 노을: 해 쪽 지평선이 넓게 달아오른다
	float band = exp(-abs(d.y) * 6.0);
	col += sun_color.rgb * pow(sd, 6.0) * band * (0.35 + 0.9 * sunset) * (1.0 - night * 0.8);
	col += sun_color.rgb * pow(sd, 90.0) * 0.8 * (1.0 - night);
	// 해 원반: 지평선 가까울수록 크고 붉게 (지는 해)
	float disc = step(mix(0.9994, 0.9987, sunset), sd);
	vec3 disc_col = mix(sun_color.rgb * 4.0, vec3(4.0, 1.7, 0.8), sunset * 0.75);
	col = mix(col, disc_col, disc * (1.0 - night));
	// 빛 내림: 노을 때 해에서 부채꼴로 퍼지는 빛줄기 (각도 잡음을 끊어서)
	if (sunset > 0.05) {
		vec3 tx = normalize(cross(s, vec3(0.0, 1.0, 0.0)));
		vec3 ty = cross(tx, s);
		float ang = atan(dot(d, ty), dot(d, tx));
		float rays = step(0.62, vnoise(vec2(ang * 10.0, world_time * 0.01)));
		col += sun_color.rgb * rays * pow(sd, 4.0) * 0.3 * sunset * (1.0 - night) * step(0.0, d.y);
	}

	// 픽셀 구름 띠 — 낮에만 또렷하다
	vec2 cp = d.xz / max(d.y + 0.08, 0.05);
	vec2 q = floor(cp * 24.0) / 24.0;
	float cl = fbm3(q * 0.9 + vec2(world_time * 0.004, 0.0));
	float cloud = smoothstep(0.58, 0.66, cl) * smoothstep(0.0, 0.18, d.y);
	vec3 cloud_col = mix(vec3(1.0, 0.98, 0.95), sun_color.rgb * 1.1 + vec3(0.2, 0.05, 0.1), sunset * 0.8);
	cloud_col = mix(cloud_col, vec3(0.16, 0.18, 0.28), night);
	col = mix(col, cloud_col, cloud * 0.85);

	// 별: 방향을 격자로 잘라 드문드문, 반짝이게
	if (night > 0.01) {
		vec3 sp = floor(d * 220.0);
		float h = hash21(sp.xy + sp.z * 31.0);
		float tw = 0.6 + 0.4 * sin(world_time * 2.0 + h * 60.0);
		float star = step(0.9965, h) * smoothstep(0.02, 0.25, d.y) * (1.0 - cloud);
		col += vec3(0.9, 0.95, 1.0) * star * tw * night * 1.6;
		// 은하수 띠
		float mw = exp(-pow(dot(d, normalize(vec3(0.4, 0.2, -1.0))) * 3.0, 2.0));
		col += vec3(0.25, 0.22, 0.4) * mw * fbm3(d.xz * 8.0) * night * 0.5;
	}

	// 오로라: 북쪽 하늘에 일렁이는 커튼. 아래는 청록, 위로 갈수록 보랏빛. 띠를 3단으로 끊는다
	if (aurora > 0.01 && d.z < 0.0) {
		float el = d.y;
		vec2 ap = vec2(atan(d.x, -d.z) * 3.0, 0.0);
		float wave = vnoise(vec2(ap.x * 1.4 + world_time * 0.006, 2.0)) * 0.12;
		float base = 0.10 + wave;
		float curtain = smoothstep(base, base + 0.03, el) * (1.0 - smoothstep(base + 0.08, base + 0.32, el));
		float rays = vnoise(vec2(ap.x * 9.0 + world_time * 0.01, el * 3.0));
		float a = curtain * (0.4 + 0.8 * rays) * smoothstep(0.0, 0.6, -d.z);
		a = floor(a * 3.0) / 3.0;
		vec3 acol = mix(vec3(0.2, 0.95, 0.6), vec3(0.65, 0.35, 0.95), smoothstep(base + 0.05, base + 0.25, el));
		col += acol * a * aurora * night * 0.9 * (1.0 - cloud);
	}

	// 달
	vec3 m = normalize(moon_dir);
	float md = dot(d, m);
	col += vec3(0.9, 0.93, 1.0) * step(0.9990, md) * night * 1.4;
	col += vec3(0.5, 0.6, 0.9) * pow(max(md, 0.0), 40.0) * night * 0.3;

	COLOR = slow_tint(col);
}
