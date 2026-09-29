using System.Collections.Generic;
using Godot;
using PixelMmo.Core;

namespace PixelMmo.World;

/// <summary>
/// 밤 풍경: 등불이 땅을 비추고 반딧불이 떠다닌다.
/// 가로등·석등은 섬에 40개가 넘지만 점광원은 비싸다(Mobile 은 물체 하나당 8개까지) —
/// 카메라 가까이 있는 것 몇 개에만 실제 빛을 켜고 나머지는 그림만 빛난다(sprite 셰이더 glow_at_night).
/// </summary>
public partial class NightLights : Node3D
{
    private const int Pool = 10;
    private const float Reach = 30f;          // 이 안의 등불만 빛을 켠다 (m)
    private const float Refresh = 0.2f;       // 가까운 등불을 다시 고르는 간격 (초)
    private const float Energy = 1.1f;
    private const float Range = 3.4f;
    private const float LightHeight = 1.6f;   // 빛이 닿는 거리(Range)보다 충분히 낮게
    private static readonly Color Warm = new(1f, 0.8f, 0.48f);

    private readonly List<Vector3> _sources = new();
    private readonly OmniLight3D[] _lights = new OmniLight3D[Pool];
    private GpuParticles3D _fireflies;
    private float _timer;

    public NightLights(WorldData world)
    {
        Name = "NightLights";
        foreach (var p in world.Props)
        {
            if (!PropCatalog.Kinds.TryGetValue(p.Type, out var kind) || kind.Glow <= 0f)
                continue;
            // 빛 웅덩이가 땅에 닿아야 하므로 실제 등불 높이(가로등은 4m 가까이)가 아니라 낮게 단다
            float h = Mathf.Min(kind.Size.Y / Px.PerMeter * Px.UprightStretch * 0.8f, LightHeight);
            _sources.Add(new Vector3(p.X, world.HeightAt(p.X, p.Z) + h, p.Z + 0.3f));
        }
    }

    public NightLights() { }

    public override void _Ready()
    {
        for (int i = 0; i < Pool; i++)
        {
            _lights[i] = new OmniLight3D
            {
                LightColor = Warm,
                OmniRange = Range,
                OmniAttenuation = 1.6f,
                ShadowEnabled = false,
                Visible = false,
            };
            AddChild(_lights[i]);
        }
        _fireflies = MakeFireflies();
        AddChild(_fireflies);
    }

    public override void _Process(double delta)
    {
        var root = GameRoot.Instance;
        float night = root.DayCycle.Night;
        Vector3 center = root.Rig.Target;
        _fireflies.GlobalPosition = center + Vector3.Up * 0.8f;
        _fireflies.AmountRatio = Mathf.Clamp((night - 0.4f) * 2f, 0f, 1f);

        _timer -= (float)delta;
        if (_timer > 0f)
            return;
        _timer = Refresh;
        if (night < 0.05f)
        {
            foreach (var l in _lights)
                l.Visible = false;
            return;
        }
        // 가까운 순서로 Pool 개
        _sources.Sort((a, b) => a.DistanceSquaredTo(center).CompareTo(b.DistanceSquaredTo(center)));
        for (int i = 0; i < Pool; i++)
        {
            bool on = i < _sources.Count && _sources[i].DistanceTo(center) < Reach;
            _lights[i].Visible = on;
            if (!on)
                continue;
            _lights[i].GlobalPosition = _sources[i];
            _lights[i].LightEnergy = Energy * night;
        }
    }

    /// <summary>주인공 둘레를 느리게 떠도는 노란 빛 알갱이. 밤에만 (AmountRatio).</summary>
    private static GpuParticles3D MakeFireflies()
    {
        var pm = new ParticleProcessMaterial
        {
            EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Box,
            EmissionBoxExtents = new Vector3(11f, 1.2f, 8f),
            Direction = Vector3.Up,
            Spread = 180f,
            InitialVelocityMin = 0.1f,
            InitialVelocityMax = 0.4f,
            Gravity = Vector3.Zero,
            TurbulenceEnabled = true,
            TurbulenceNoiseStrength = 0.6f,
            TurbulenceNoiseScale = 3f,
            ScaleMin = 0.09f,
            ScaleMax = 0.13f,
            Color = new Color(0.85f, 1f, 0.45f),
        };
        // 켜졌다 꺼지는 반딧불 깜빡임
        var blink = new Gradient();
        blink.SetColor(0, new Color(1, 1, 1, 0));
        blink.SetColor(1, new Color(1, 1, 1, 0));
        blink.AddPoint(0.25f, new Color(1, 1, 1, 1));
        blink.AddPoint(0.45f, new Color(1, 1, 1, 0.2f));
        blink.AddPoint(0.65f, new Color(1, 1, 1, 1));
        pm.ColorRamp = new GradientTexture1D { Gradient = blink };

        var mat = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/particle_soft.gdshader") };
        mat.SetShaderParameter("px_size", 3f);
        mat.SetShaderParameter("softness", 0f);
        mat.SetShaderParameter("intensity", 3f);
        var e = new GpuParticles3D
        {
            Amount = 90,
            Lifetime = 5f,
            Preprocess = 5f,
            ProcessMaterial = pm,
            DrawPass1 = new QuadMesh { Size = Vector2.One, Material = mat },
            LocalCoords = false,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            VisibilityAabb = new Aabb(new Vector3(-20, -4, -16), new Vector3(40, 10, 32)),
            AmountRatio = 0f,
        };
        e.AddToGroup(GameRoot.WorldParticles);
        return e;
    }
}
