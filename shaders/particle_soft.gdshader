// 입자 공용: 물안개·물보라·반딧불·꽃잎. 카메라를 보는 판, 가운데가 진한 둥근 점을 픽셀로 끊어 그린다.
shader_type spatial;
render_mode blend_add, unshaded, depth_draw_never, cull_disabled;

uniform float px_size = 6.0;     // 입자 한 장을 몇 픽셀 격자로 끊을지
uniform float softness = 1.0;    // 1 = 둥근 점, 0 = 사각 픽셀
uniform float intensity = 1.0;
uniform bool additive_glow = true;

void vertex() {
	// 빌보드
	MODELVIEW_MATRIX = VIEW_MATRIX * mat4(INV_VIEW_MATRIX[0], INV_VIEW_MATRIX[1], INV_VIEW_MATRIX[2], MODEL_MATRIX[3]);
	MODELVIEW_MATRIX = MODELVIEW_MATRIX * mat4(vec4(length(MODEL_MATRIX[0].xyz), 0.0, 0.0, 0.0), vec4(0.0, length(MODEL_MATRIX[1].xyz), 0.0, 0.0), vec4(0.0, 0.0, length(MODEL_MATRIX[2].xyz), 0.0), vec4(0.0, 0.0, 0.0, 1.0));
}

void fragment() {
	vec2 q = (floor(UV * px_size) + 0.5) / px_size - 0.5;
	float d = length(q) * 2.0;
	float a = mix(step(d, 1.0), clamp(1.0 - d, 0.0, 1.0), softness);
	ALBEDO = COLOR.rgb * intensity;
	ALPHA = a * COLOR.a;
	EMISSION = COLOR.rgb * intensity * a * COLOR.a;
}
