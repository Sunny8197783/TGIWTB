// 돌진 잔상 — 캐릭터 시트의 한 칸을 한 색으로 칠해 반투명하게 남긴다.
// 스프라이트 셰이더는 알파 0.5 미만을 버리므로(픽셀 경계가 선명하도록) 잔상은 따로 그린다.
shader_type spatial;
render_mode unshaded, blend_add, depth_draw_never, cull_disabled, shadows_disabled;

uniform sampler2D albedo_tex : source_color, filter_nearest, repeat_disable;
uniform vec2 frame_count = vec2(1.0, 1.0);
uniform float frame = 0.0;
uniform vec4 color : source_color = vec4(0.35, 0.8, 1.0, 1.0);
uniform float fade = 1.0;
uniform float depth_pull = 0.6;

void vertex() {
	vec3 vp = (MODELVIEW_MATRIX * vec4(VERTEX, 1.0)).xyz;
	vp *= 1.0 - depth_pull / length(vp);
	POSITION = PROJECTION_MATRIX * vec4(vp, 1.0);
}

void fragment() {
	float f = floor(frame + 0.5);
	vec2 cell = vec2(mod(f, frame_count.x), floor(f / frame_count.x));
	vec4 tex = texture(albedo_tex, (cell + UV) / frame_count);
	if (tex.a < 0.5) discard;
	// 몸 윤곽 안쪽을 조금 밝게: 원래 그림의 밝기를 살짝 남겨 형태가 읽히게
	float lum = dot(tex.rgb, vec3(0.3, 0.59, 0.11));
	ALBEDO = color.rgb * (0.55 + 0.6 * lum) * fade;
}
