using Godot;

namespace PixelMmo.Combat;

/// <summary>
/// 충격파 — 반경 r0→r1 로 퍼지며 선이 얇아지고 사라진다.
/// 강타·함성 같은 '큰 한 방'에 붙인다. 도형(호)만. (규칙 5)
/// </summary>
public partial class ShockRing : Node2D
{
    private static readonly float StartRadius = 3f;

    private float _r1, _life, _elapsed;
    private Color _color;

    public void Setup(float radius, Color color, float life)
    {
        _r1 = radius; _color = color; _life = life;
        ZIndex = 55;
    }

    public override void _Process(double delta)
    {
        _elapsed += (float)delta;
        QueueRedraw();
        if (_life <= 0f || _elapsed >= _life)
            QueueFree();
    }

    public override void _Draw()
    {
        float t = _life > 0f ? Mathf.Clamp(_elapsed / _life, 0f, 1f) : 1f;
        float ease = 1f - (1f - t) * (1f - t);
        float r = Mathf.Lerp(StartRadius, _r1, ease);
        float width = Mathf.Lerp(3.5f, 0.5f, ease);
        var c = new Color(_color.R, _color.G, _color.B, 1f - t);
        DrawArc(Vector2.Zero, r, 0f, Mathf.Tau, 32, c, width);
    }
}
