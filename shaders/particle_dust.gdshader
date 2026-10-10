// 흙먼지 — 발걸음·돌진·착지. 빛나면 안 되니 가산이 아니라 섞어 그린다. 둥근 점을 픽셀로 끊는다.
shader_type spatial;
render_mode blend_mix, unshaded, depth_draw_never, cull_disabled, shadows_disabled;

global uniform vec4 sun_color : source_color;
global uniform float night;

uniform float px_size = 5.0;

void vertex() {
	MODELVIEW_MATRIX = VIEW_MATRIX * mat4(INV_VIEW_MATRIX[0], INV_VIEW_MATRIX[1], INV_VIEW_MATRIX[2], MODEL_MATRIX[3]);
	MODELVIEW_MATRIX = MODELVIEW_MATRIX * mat4(vec4(length(MODEL_MATRIX[0].xyz), 0.0, 0.0, 0.0), vec4(0.0, length(MODEL_MATRIX[1].xyz), 0.0, 0.0), vec4(0.0, 0.0, length(MODEL_MATRIX[2].xyz), 0.0), vec4(0.0, 0.0, 0.0, 1.0));
}

void fragment() {
	vec2 q = (floor(UV * px_size) + 0.5) / px_size - 0.5;
	if (length(q) * 2.0 > 1.0) discard;
	// 햇빛 색을 받아 노을엔 주황, 밤엔 어둡게
	ALBEDO = COLOR.rgb * mix(sun_color.rgb * 0.9 + 0.2, vec3(0.35, 0.4, 0.55), night);
	ALPHA = COLOR.a;
}
