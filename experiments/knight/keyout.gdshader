shader_type canvas_item;
// 배경색을 투명 처리하는 임시 셰이더 (빠른 확인용).
// 최종본은 이미지 편집기에서 진짜 알파로 배경을 지우는 걸 권장.
// 사용: Sprite2D → CanvasItem → Material → New ShaderMaterial → 이 셰이더 지정
//       → key_color 를 실제 배경색에 맞춰 조정.

uniform vec4 key_color : source_color = vec4(1.0, 0.9, 0.93, 1.0); // 배경색
uniform float threshold : hint_range(0.0, 1.0) = 0.15;

void fragment() {
	vec4 c = texture(TEXTURE, UV);
	if (distance(c.rgb, key_color.rgb) < threshold)
		c.a = 0.0; // 배경색과 가까우면 투명
	COLOR = c;
}
