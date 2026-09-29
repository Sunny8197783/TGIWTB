// 불꽃·파편 — 날아가는 방향으로 길쭉한 픽셀 줄. GpuParticles3D.TransformAlign = ZBillboardYToVelocity 와 함께 쓴다.
// 색은 입자 색(ColorRamp). 가산 + 글로우 문턱을 넘겨 번쩍인다.
shader_type spatial;
render_mode unshaded, blend_add, depth_draw_never, cull_disabled, shadows_disabled;

uniform float energy = 2.4;

void fragment() {
	// 머리(위)가 밝고 꼬리로 갈수록 어둡다
	float k = 1.0 - UV.y;
	ALBEDO = COLOR.rgb * energy * (0.35 + 0.65 * step(0.45, k)) * COLOR.a;
}
