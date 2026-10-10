using Godot;

namespace PixelMmo.Render;

/// <summary>
/// 흙먼지 한 뭉치. 한 번 터지는 입자 방출기를 몇 개 돌려 쓴다 — 발걸음마다 노드를 만들면 쌓인다.
/// </summary>
public partial class Dust : Node3D
{
    private const int Pool = 8;
    private static Dust _instance;
    private readonly GpuParticles3D[] _emitters = new GpuParticles3D[Pool];
    private int _next;

    public override void _EnterTree() => _instance = this;

    public override void _Ready()
    {
        Name = "Dust";
        var mat = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/particle_dust.gdshader") };
        var quad = new QuadMesh { Size = Vector2.One, Material = mat };
        var fade = new Gradient();
        fade.SetColor(0, new Color(0.78f, 0.72f, 0.62f, 0.75f));
        fade.SetColor(1, new Color(0.78f, 0.72f, 0.62f, 0f));
        var shrink = new Curve();
        shrink.AddPoint(new Vector2(0f, 0.6f));
        shrink.AddPoint(new Vector2(0.25f, 1f));
        shrink.AddPoint(new Vector2(1f, 0.3f));
        for (int i = 0; i < Pool; i++)
        {
            var pm = new ParticleProcessMaterial
            {
                EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Sphere,
                EmissionSphereRadius = 0.12f,
                Direction = new Vector3(0f, 1f, 0f),
                Spread = 80f,
                InitialVelocityMin = 0.4f,
                InitialVelocityMax = 1.2f,
                Gravity = new Vector3(0f, 0.3f, 0f),
                DampingMin = 3f,
                DampingMax = 5f,
                ScaleMin = 0.2f,
                ScaleMax = 0.36f,
                ScaleCurve = new CurveTexture { Curve = shrink },
                ColorRamp = new GradientTexture1D { Gradient = fade },
            };
            var e = new GpuParticles3D
            {
                OneShot = true,
                Emitting = false,
                Amount = 6,
                Lifetime = 0.45f,
                Explosiveness = 0.9f,
                ProcessMaterial = pm,
                DrawPass1 = quad,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                LocalCoords = false,
            };
            e.AddToGroup(Core.GameRoot.WorldParticles);
            AddChild(e);
            _emitters[i] = e;
        }
    }

    /// <param name="push">먼지가 밀려 나가는 방향·세기 (m/s). 달리던 반대쪽으로 튀면 발을 박차는 느낌이 난다.</param>
    public static void Puff(Vector3 at, int amount = 6, Vector3 push = default)
    {
        if (_instance == null)
            return;
        var e = _instance._emitters[_instance._next];
        _instance._next = (_instance._next + 1) % Pool;
        if (e.Amount != amount)
            e.Amount = amount; // 바꾸면 버퍼를 다시 잡는다 — 같은 값이면 건드리지 않는다
        var pm = (ParticleProcessMaterial)e.ProcessMaterial;
        float strength = push.Length();
        pm.Direction = strength > 0.01f ? (push / strength + Vector3.Up * 0.6f).Normalized() : Vector3.Up;
        pm.InitialVelocityMax = 1.2f + strength;
        e.GlobalPosition = at + Vector3.Up * 0.08f;
        e.Restart();
    }
}
