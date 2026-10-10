// 화면 전체 후처리 (PixelView 의 표시면). 저해상도 픽셀 단위로 계산해 효과도 픽셀로 끊긴다.
//  flash    : 임팩트 프레임 (패링·강타 순간 한두 프레임 하얗게)
//  vignette : 가장자리 색 — 슬로우는 보라, 피격은 빨강
//  ring     : 완벽 회피 순간 주인공에게서 퍼지는 고리
shader_type canvas_item;

uniform vec2 low_res = vec2(642.0, 362.0);
uniform float flash = 0.0;
uniform vec4 flash_color : source_color = vec4(1.0);
uniform float vignette = 0.0;
uniform vec4 vignette_color : source_color = vec4(0.35, 0.15, 0.55, 1.0);
uniform vec2 ring_center = vec2(0.5, 0.5);
uniform float ring_radius = -1.0;  // 화면 높이 비율, 음수면 끔
uniform float ring_width = 0.02;

void fragment() {
	vec2 px = (floor(UV * low_res) + 0.5) / low_res;
	vec4 c = texture(TEXTURE, UV);
	vec2 aspect = vec2(low_res.x / low_res.y, 1.0);
	// 가장자리: 타원 거리를 몇 단으로 끊는다 (픽셀아트식 계단 비네트)
	// 화면 가장자리·모서리에만 3단으로. 안쪽은 건드리지 않는다.
	float e = length(px - 0.5) * 2.0; // 화면 모양 타원: 변의 가운데 1.0, 모서리 1.41
	float band = floor(clamp((e - 0.9) * 1.8, 0.0, 1.0) * 3.0) / 3.0;
	c.rgb = mix(c.rgb, vignette_color.rgb * 0.5, band * vignette * 0.8);
	if (ring_radius > 0.0) {
		float d = length((px - ring_center) * aspect);
		float r = abs(d - ring_radius);
		if (r < ring_width) c.rgb = mix(c.rgb, vec3(1.0) - c.rgb * 0.6, 0.85);
		else if (r < ring_width * 2.2) c.rgb += vec3(0.25, 0.2, 0.45) * 0.6;
	}
	c.rgb = mix(c.rgb, flash_color.rgb, flash);
	COLOR = c;
}
