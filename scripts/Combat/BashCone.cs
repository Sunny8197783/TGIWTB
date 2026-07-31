using Godot;

namespace PixelMmo.Combat;

/// <summary>
/// 강타 원뿔 — 돌진 방향으로 뻗는 꼬깔(원뿔). 앞으로 쭉 늘어났다가 사라진다.
/// 반투명 채움 + 밝은 외곽선 + 내부 리브선. 도형(삼각형)만. (규칙 5)
/// </summary>
public partial class BashCone : Node2D
{
    private float _angle, _length, _halfWidth, _life, _elapsed;
    private Color _color;

    public void Setup(Vector2 direction, float length, float halfWidth, Color color, float life)
    {
        _angle = direction.Angle();
        _length = length;
        _halfWidth = halfWidth;
        _color = color;
        _life = life;
        ZIndex = 52;
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
        float alpha = 1f - t;

        // 앞으로 쭉 늘어나며 끝이 뾰족해진다 (꼬깔).
        float len = _length * (0.6f + ease * 0.55f);
        float hw = _halfWidth * (1f - t * 0.35f);

        Vector2 fwd = Vector2.Right.Rotated(_angle);
        Vector2 side = Vector2.Right.Rotated(_angle + Mathf.Pi / 2f);

        Vector2 apex = fwd * len;              // 뾰족한 끝
        Vector2 baseL = side * hw;             // 밑변 두 귀
        Vector2 baseR = -side * hw;

        // 채움
        DrawColoredPolygon(new[] { apex, baseL, baseR },
            new Color(_color.R, _color.G, _color.B, alpha * 0.35f));
        // 외곽선
        var edge = new Color(1f, 1f, 1f, alpha * 0.9f);
        DrawPolyline(new[] { baseL, apex, baseR }, edge, 1.5f);
        // 내부 리브선 — 원뿔 느낌을 살린다.
        var rib = new Color(_color.R, _color.G, _color.B, alpha * 0.7f);
        DrawLine(Vector2.Zero, apex, rib, 1.2f);
        DrawLine(side * (hw * 0.5f), apex, rib, 1f);
        DrawLine(-side * (hw * 0.5f), apex, rib, 1f);
    }
}
