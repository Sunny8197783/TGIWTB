using Godot;

namespace PixelMmo.Combat;

/// <summary>
/// 타격 스파크 — 맞은 지점에서 짧은 파편선이 방사되고 순간 링이 퍼진다.
/// 모든 유효타에 붙어 '맞았다'를 또렷하게. 도형(선+호)만. (규칙 5)
/// </summary>
public partial class HitSpark : Node2D
{
    private struct Shard { public Vector2 Dir; public float Len; }

    private Shard[] _shards;
    private bool _heavy;
    private float _life, _elapsed;
    private readonly RandomNumberGenerator _rng = new();

    public void Setup(bool heavy, float life)
    {
        _heavy = heavy;
        _life = life;
        _rng.Randomize();

        int n = heavy ? 7 : 5;
        _shards = new Shard[n];
        for (int i = 0; i < n; i++)
        {
            float a = _rng.RandfRange(0f, Mathf.Tau);
            _shards[i] = new Shard
            {
                Dir = Vector2.Right.Rotated(a),
                Len = _rng.RandfRange(6f, 14f) * (heavy ? 1.5f : 1f),
            };
        }
        ZIndex = 60;
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

        var spark = new Color(1f, 0.95f, 0.7f, alpha);
        foreach (var s in _shards)
        {
            Vector2 a = s.Dir * (2f + ease * s.Len * 0.4f);
            Vector2 b = s.Dir * (s.Len * (0.5f + ease));
            DrawLine(a, b, spark, 1.5f);
        }

        // 순간 확장 링
        float ring = Mathf.Lerp(2f, _heavy ? 16f : 11f, ease);
        DrawArc(Vector2.Zero, ring, 0f, Mathf.Tau, 18, new Color(1f, 1f, 1f, alpha * 0.7f), 1.5f);
    }
}
