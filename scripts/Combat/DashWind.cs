using Godot;

namespace PixelMmo.Combat;

/// <summary>
/// 대시 바람 가르기 — 진행 방향 앞에서 공기가 두 갈래로 갈라지는 활 모양 파문 +
/// 뒤로 뻗는 속도선. 얇은 선이 벌어지며 사라진다. 도형만. (규칙 5)
/// </summary>
public partial class DashWind : Node2D
{
    private float _angle, _life, _elapsed;
    private Color _color;

    public void Setup(Vector2 direction, Color color, float life)
    {
        _angle = direction.Angle();
        _color = color;
        _life = life;
        ZIndex = 45;   // 캐릭터 뒤쪽 느낌으로 살짝 낮게
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
        var c = new Color(_color.R, _color.G, _color.B, alpha);

        Vector2 fwd = Vector2.Right.Rotated(_angle);
        Vector2 side = Vector2.Right.Rotated(_angle + Mathf.Pi / 2f);

        // 앞에서 갈라지는 두 활(공기가 쪼개짐). 진행할수록 앞으로 나가며 벌어진다.
        float lead = 6f + ease * 18f;
        float spread = 4f + ease * 12f;
        for (int s = -1; s <= 1; s += 2)
        {
            Vector2 tip = fwd * lead + side * (spread * s);
            Vector2 mid = fwd * (lead * 0.4f) + side * (spread * 0.5f * s);
            Vector2 tail = -fwd * (10f + ease * 10f) + side * (spread * 1.3f * s);
            DrawPolyline(new[] { tip, mid, tail }, c, 1.6f);
        }

        // 뒤로 뻗는 속도선 몇 가닥.
        var streak = new Color(1f, 1f, 1f, alpha * 0.7f);
        for (int i = -1; i <= 1; i++)
        {
            Vector2 off = side * (i * 4f);
            Vector2 a = off - fwd * (2f + ease * 8f);
            Vector2 b = off - fwd * (14f + ease * 22f);
            DrawLine(a, b, streak, 1.2f);
        }
    }
}
