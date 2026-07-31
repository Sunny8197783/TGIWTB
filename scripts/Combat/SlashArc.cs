using Godot;

namespace PixelMmo.Combat;

/// <summary>
/// 베기 궤적 — 스윙이 지나간 부채꼴을 초승달 잔상으로 그리고 빠르게 사라진다.
/// 살짝 바깥으로 번지며 페이드. 도형(호)만. (규칙 5)
/// </summary>
public partial class SlashArc : Node2D
{
    private float _base, _from, _to, _inner, _outer, _life, _elapsed;
    private Color _color;

    public void Setup(float baseAngle, float fromOff, float toOff,
        float inner, float outer, Color color, float life)
    {
        _base = baseAngle; _from = fromOff; _to = toOff;
        _inner = inner; _outer = outer; _color = color; _life = life;
        ZIndex = 50;
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
        float grow = 1f + ease * 0.25f;                 // 바깥으로 번짐
        float alpha = 1f - t;

        float mid = (_inner + _outer) * 0.5f * grow;
        float width = (_outer - _inner) * (1f - t * 0.4f);

        // 본체(색) + 밝은 흰색 코어 — 코어가 있어야 어두운 바닥에서도 또렷하다.
        DrawArc(Vector2.Zero, mid, _base + _from, _base + _to, 24,
            new Color(_color.R, _color.G, _color.B, alpha * 0.85f), width);
        DrawArc(Vector2.Zero, mid, _base + _from, _base + _to, 24,
            new Color(1f, 1f, 1f, alpha * 0.9f), width * 0.4f);

        // 스윙 끝쪽 밝은 앞날
        DrawArc(Vector2.Zero, _outer * grow, _base + _to - 0.22f, _base + _to, 6,
            new Color(1f, 1f, 1f, alpha), 2.5f);
    }
}
