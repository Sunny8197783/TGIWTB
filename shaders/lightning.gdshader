// 번개 줄기: 카메라를 보는 띠. UV.y 0 = 하늘, 1 = 땅. 가운데 흰 심 + 바깥 색. 몇 프레임마다 모양이 바뀐다(seed).
shader_type spatial;
render_mode unshaded, blend_add, depth_draw_never, cull_disabled, shadows_disabled;

#include "res://shaders/common.gdshaderinc"

uniform float seed = 0.0;
uniform float fade = 1.0;
uniform float px_w = 32.0;      // 띠 너비 픽셀 수
uniform float px_h = 256.0;     // 띠 길이 픽셀 수
uniform vec4 core : source_color = vec4(1.0);
uniform vec4 glow : source_color = vec4(0.6, 0.8, 1.0, 1.0);

void vertex() {
	// 세로축은 세계 위쪽 고정, 가로만 카메라를 본다
	vec3 up = vec3(0.0, 1.0, 0.0);
	vec3 right = normalize(cross(up, INV_VIEW_MATRIX[2].xyz));
	vec3 origin = MODEL_MATRIX[3].xyz;
	vec3 w = origin + right * VERTEX.x + up * VERTEX.y;
	POSITION = PROJECTION_MATRIX * VIEW_MATRIX * vec4(w, 1.0);
}

void fragment() {
	vec2 p = floor(UV * vec2(px_w, px_h));
	float y = p.y;
	// 지그재그: 12픽셀마다 꺾이는 점을 잇는다
	float seg = floor(y / 12.0);
	float f = fract(y / 12.0);
	float a = (hash21(vec2(seg, seed)) - 0.5) * px_w * 0.6;
	float b = (hash21(vec2(seg + 1.0, seed)) - 0.5) * px_w * 0.6;
	float center = px_w * 0.5 + mix(a, b, f) * (1.0 - UV.y * 0.3);
	float d = abs(p.x - center);
	if (d > 5.0) discard;
	// 흰 심 3픽셀 + 색 번짐 2단
	vec3 col = d < 1.5 ? core.rgb * 2.6 : d < 3.5 ? glow.rgb * 1.8 : glow.rgb * 0.7;
	ALBEDO = col * fade;
}
