// 투사체 (Combat/Projectile) — 화살은 픽셀로 그린 살·촉·깃, 마법탄은 흰 심 → 색 램프 동그라미.
// 판은 카메라를 보고 가로축이 날아가는 쪽 (Projectile 이 기저를 맞춘다). 무늬는 픽셀 격자에 맞춰 끊는다.
shader_type spatial;
render_mode unshaded, cull_disabled, depth_draw_opaque;

uniform int kind = 0;               // 0 화살, 1 마법탄
uniform vec2 px = vec2(16.0, 4.0);  // 판 하나의 픽셀 수 (가로 = 날아가는 쪽)
uniform vec4 core : source_color = vec4(1.0);
uniform vec4 bright : source_color = vec4(0.75, 0.9, 1.0, 1.0);
uniform vec4 mid : source_color = vec4(0.4, 0.6, 1.0, 1.0);
uniform vec4 deep : source_color = vec4(0.2, 0.22, 0.6, 1.0);
uniform float flicker = 0.0;

void fragment() {
	vec2 p = floor(UV * px);
	vec2 c = (p + 0.5) / px;
	if (kind == 0) {
		// 화살: 오른쪽(+x)이 촉. 가운데 한 줄 살, 끝 2픽셀 촉(회색), 꼬리 3픽셀 깃(흰·빨강)
		float row = p.y - floor(px.y * 0.5);
		float x = p.x;
		bool shaft = abs(row) < 0.5;
		bool head = x >= px.x - 3.0 && abs(row) <= (px.x - 1.0 - x);
		bool fletch = x < 3.0 && abs(row) <= 1.0 && abs(row) > 0.0;
		if (!(shaft || head || fletch)) discard;
		vec3 col = head ? vec3(0.78, 0.8, 0.84) : fletch ? (mod(x, 2.0) < 1.0 ? vec3(0.95) : vec3(0.85, 0.25, 0.22)) : vec3(0.55, 0.38, 0.22);
		ALBEDO = col;
	} else {
		// 마법탄: 둥근 몸에 꼬리가 조금 늘어진다 (진행 반대쪽 = -x)
		vec2 d = (c - vec2(0.62, 0.5)) * vec2(1.0, px.y / px.x);
		d.x *= d.x < 0.0 ? 0.55 : 1.0;
		float r = length(d) * 2.0 / (px.y / px.x);
		r += (fract(sin(dot(p, vec2(12.9, 78.2)) + flicker) * 43758.5) - 0.5) * 0.12;
		if (r > 1.0) discard;
		vec3 col = r < 0.3 ? core.rgb : r < 0.55 ? bright.rgb : r < 0.8 ? mid.rgb : deep.rgb;
		ALBEDO = col;
		EMISSION = col * (r < 0.55 ? 1.6 : 0.4);
	}
}
