using Godot;

namespace PixelMmo.Combat;

/// <summary>처치 시 방사되는 파티클. 개수는 CombatTuning.DeathParticles. (§C-6)</summary>
public partial class DeathBurst : Node2D
{
    private static readonly float Life = 0.45f;
    private static readonly float SpeedMin = 40f;
    private static readonly float SpeedMax = 110f;
    private static readonly float ParticleSize = 3f;

    private readonly System.Collections.Generic.List<ColorRect> _parts = new();
    private readonly System.Collections.Generic.List<Vector2> _velocities = new();
    private float _elapsed;

    public void Setup(Color color)
    {
        var rng = new RandomNumberGenerator();
        rng.Randomize();

        int count = Mathf.RoundToInt(CombatTuning.DeathParticles);
        for (int i = 0; i < count; i++)
        {
            // 균등하게 방사하되 약간 흔들어서 기계적으로 보이지 않게.
            float angle = Mathf.Tau * i / Mathf.Max(1, count) + rng.RandfRange(-0.25f, 0.25f);
            float speed = rng.RandfRange(SpeedMin, SpeedMax);

            var part = new ColorRect
            {
                Size = new Vector2(ParticleSize, ParticleSize),
                Position = new Vector2(-ParticleSize * 0.5f, -ParticleSize * 0.5f),
                Color = color,
                MouseFilter = Control.MouseFilterEnum.Ignore,
            };
            AddChild(part);

            _parts.Add(part);
            _velocities.Add(Vector2.Right.Rotated(angle) * speed);
        }

        ZIndex = 90;
    }

    public override void _Process(double delta)
    {
        _elapsed += (float)delta;
        float t = Mathf.Clamp(_elapsed / Life, 0f, 1f);
        float eased = 1f - (1f - t) * (1f - t);

        for (int i = 0; i < _parts.Count; i++)
        {
            _parts[i].Position = _velocities[i] * (Life * eased)
                - Vector2.One * (ParticleSize * 0.5f);
        }

        Modulate = new Color(1f, 1f, 1f, 1f - t);

        if (t >= 1f)
            QueueFree();
    }
}
