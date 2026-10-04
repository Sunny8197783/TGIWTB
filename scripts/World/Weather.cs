using System.Collections.Generic;
using Godot;
using PixelMmo.Core;

namespace PixelMmo.World;

/// <summary>
/// 지역마다 공기 중에 떠다니는 것: 벚꽃잎·낙엽·눈송이 (고스트 오브 쓰시마 — "모든 것이 바람에 움직인다").
/// 주인공 둘레 상자에서 떨어지고, 지역을 넘나들면 AmountRatio 로 천천히 옅어졌다 짙어진다.
/// 화면이 북쪽으로 길게 열리니 상자는 조금 북쪽으로 치우친다.
/// </summary>
public partial class Weather : Node3D
{
    private sealed class Fx
    {
        public GpuParticles3D Emitter;
        public Dictionary<string, float> Zones;
        public float Ratio;
    }

    private const float Fade = 0.4f;   // 1초에 옮겨 가는 세기
    private static readonly Vector3 Ahead = new(0f, 6f, -5f);
    private readonly WorldData _world;
    private readonly List<Fx> _fx = new();

    public Weather(WorldData world)
    {
        Name = "Weather";
        _world = world;
    }

    public Weather() { }

    public override void _Ready()
    {
        Add(new Dictionary<string, float> { ["벚꽃 골짜기"] = 1f, ["하나미 마을"] = 0.7f },
            amount: 150, life: 9f, fall: new Vector2(0.35f, 0.7f), scale: new Vector2(0.07f, 0.1f), turbulence: 0.9f,
            new Color(1f, 0.82f, 0.88f), new Color(0.96f, 0.6f, 0.74f), new Color(1f, 0.95f, 0.96f));
        Add(new Dictionary<string, float> { ["단풍 협곡"] = 1f, ["고목의 숲"] = 0.35f },
            amount: 100, life: 8f, fall: new Vector2(0.6f, 1.1f), scale: new Vector2(0.08f, 0.12f), turbulence: 1.4f,
            new Color(0.95f, 0.45f, 0.15f), new Color(0.85f, 0.22f, 0.12f), new Color(0.98f, 0.78f, 0.25f));
        Add(new Dictionary<string, float> { ["서리 고원"] = 1f, ["눈꽃 마을"] = 0.8f },
            amount: 260, life: 8f, fall: new Vector2(0.7f, 1.3f), scale: new Vector2(0.05f, 0.09f), turbulence: 0.5f,
            new Color(1f, 1f, 1f), new Color(0.9f, 0.94f, 1f), new Color(1f, 1f, 1f));
    }

    private void Add(Dictionary<string, float> zones, int amount, float life, Vector2 fall, Vector2 scale, float turbulence, params Color[] colors)
    {
        var ramp = new Gradient();
        ramp.SetColor(0, colors[0]);
        ramp.SetColor(1, colors[^1]);
        for (int i = 1; i < colors.Length - 1; i++)
            ramp.AddPoint(i / (float)(colors.Length - 1), colors[i]);
        var fadeInOut = new Gradient();
        fadeInOut.SetColor(0, new Color(1, 1, 1, 0));
        fadeInOut.SetColor(1, new Color(1, 1, 1, 0));
        fadeInOut.AddPoint(0.12f, new Color(1, 1, 1, 1));
        fadeInOut.AddPoint(0.85f, new Color(1, 1, 1, 1));
        var pm = new ParticleProcessMaterial
        {
            EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Box,
            EmissionBoxExtents = new Vector3(16f, 3f, 14f),
            Direction = new Vector3(0.3f, -1f, 0.1f),
            Spread = 25f,
            InitialVelocityMin = fall.X,
            InitialVelocityMax = fall.Y,
            // 바람에 실려 동쪽으로 흐른다
            Gravity = new Vector3(0.25f, -0.15f, 0.05f),
            TurbulenceEnabled = true,
            TurbulenceNoiseStrength = turbulence,
            TurbulenceNoiseScale = 4f,
            ScaleMin = scale.X,
            ScaleMax = scale.Y,
            ColorInitialRamp = new GradientTexture1D { Gradient = ramp },
            ColorRamp = new GradientTexture1D { Gradient = fadeInOut },
        };
        var mat = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/particle_dust.gdshader") };
        mat.SetShaderParameter("px_size", 3f);
        var e = new GpuParticles3D
        {
            Amount = amount,
            Lifetime = life,
            Preprocess = life,
            ProcessMaterial = pm,
            DrawPass1 = new QuadMesh { Size = Vector2.One, Material = mat },
            LocalCoords = false,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            VisibilityAabb = new Aabb(new Vector3(-26, -14, -24), new Vector3(52, 22, 48)),
            AmountRatio = 0f,
        };
        e.AddToGroup(GameRoot.WorldParticles);
        AddChild(e);
        _fx.Add(new Fx { Emitter = e, Zones = zones });
    }

    public override void _Process(double delta)
    {
        var target = GameRoot.Instance.Rig.Target;
        string zone = _world.ZoneAt(target.X, target.Z);
        float step = (float)delta * Fade;
        foreach (var fx in _fx)
        {
            float want = zone != null && fx.Zones.TryGetValue(zone, out float v) ? v : 0f;
            fx.Ratio = Mathf.MoveToward(fx.Ratio, want, step);
            fx.Emitter.AmountRatio = fx.Ratio;
            fx.Emitter.Emitting = fx.Ratio > 0.001f;
            fx.Emitter.GlobalPosition = target + Ahead;
        }
    }
}
