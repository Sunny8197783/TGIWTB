using System.Collections.Generic;
using Godot;
using PixelMmo.Core;
using PixelMmo.Render;

namespace PixelMmo.Combat;

/// <summary>
/// 타격감 연출 한곳: 카메라 킥·흔들림, 불꽃, 임팩트 별, 베기 궤적, 화면 섬광·비네트·고리.
/// 근거(REBUILD.md): 히트스톱·흔들림·파편이 같은 프레임에 맞물려야 하고, 흔들림은 무작위가 아니라 맞은 방향으로.
/// 카메라·화면 효과는 실제 시간으로 돈다 — 히트스톱으로 세상이 멈춘 동안에도 화면은 흔들려야 한다.
/// 궤적·별·불꽃은 미리 만들어 돌려 쓴다 — 타격 순간에 메시·재질·텍스처를 새로 만들면 그 프레임이 끊긴다.
/// </summary>
public partial class CombatFx : Node
{
    public enum Spark { Hit, Heavy, Block, Parry }

    private static CombatFx _i;

    // 카메라: 킥은 용수철(맞은 쪽으로 밀렸다 돌아옴), 흔들림은 충격량²에 비례하는 떨림
    private Vector3 _kick, _kickVel;
    private float _trauma;
    private float _t;
    private const float KickStiffness = 260f;
    private const float KickDamping = 22f;
    private const float ShakeMax = 0.16f;    // m (≈5 저해상도 픽셀)
    private const float TraumaDecay = 1.8f;  // 1/s

    // 화면
    private ShaderMaterial _screen;
    private float _flash;
    private float _vignette, _vignetteHold;
    private Color _vignetteColor;
    private float _ringAge = -1f;
    private Vector3 _ringAt;
    private const float RingLife = 0.45f;

    private sealed class Pooled
    {
        public MeshInstance3D Mesh;
        public ShaderMaterial Slash;
        public StandardMaterial3D Star;
        public float Age, Life;
        public bool Live;
    }
    private const int PoolSize = 6;
    private const float SlashTail = 0.85f;
    private readonly List<GpuParticles3D> _sparks = new();
    private readonly List<Pooled> _slashes = new();
    private readonly List<Pooled> _stars = new();
    private int _nextSpark, _nextSlash, _nextStar;
    private readonly Dictionary<(float, float, bool), ArrayMesh> _arcs = new();
    private readonly Dictionary<Spark, GradientTexture1D> _ramps = new();
    private Shader _slashShader, _sparkShader;
    private ImageTexture _starTex;

    public override void _EnterTree() => _i = this;

    public override void _Ready()
    {
        Name = "CombatFx";
        _screen = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/screen.gdshader") };
        GameRoot.Instance.View.Screen.Material = _screen;
        _slashShader = GD.Load<Shader>("res://shaders/slash.gdshader");
        _sparkShader = GD.Load<Shader>("res://shaders/particle_spark.gdshader");
        _starTex = MakeStar();
        for (int i = 0; i < 8; i++)
            _sparks.Add(MakeSparkEmitter());
        foreach (Spark k in System.Enum.GetValues<Spark>())
            _ramps[k] = MakeRamp(SparkLook(k).head, SparkLook(k).tail);
        for (int i = 0; i < PoolSize; i++)
        {
            _slashes.Add(MakePooled(new ShaderMaterial { Shader = _slashShader }, null));
            _stars.Add(MakePooled(null, new StandardMaterial3D
            {
                AlbedoTexture = _starTex,
                TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest,
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                Transparency = BaseMaterial3D.TransparencyEnum.AlphaScissor,
                BlendMode = BaseMaterial3D.BlendModeEnum.Add,
                BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled,
                NoDepthTest = true,
                EmissionEnabled = true,
                EmissionEnergyMultiplier = 2.2f,
            }));
        }
        CallDeferred(nameof(Prewarm));
    }

    /// <summary>
    /// 이펙트 셰이더를 시작할 때 한 번씩 그려 둔다. Mobile 렌더러는 처음 그릴 때 파이프라인을 만들어서,
    /// 안 해 두면 첫 베기·첫 불꽃에서 화면이 멈칫한다 (타격감을 가장 망치는 순간).
    /// 전부 보이지 않게: 궤적은 progress 0(전부 버림), 별은 검정(가산이라 0), 불꽃은 땅속.
    /// </summary>
    private void Prewarm()
    {
        var hero = Hero.Instance;
        Vector3 at = hero != null ? hero.GlobalPosition : GameRoot.Instance.Rig.Target;
        Slash(at + Vector3.Up, Vector3.Forward, Vector3.Left, 1f, 90f, 0.2f);
        Star(at, 0.5f, Colors.Black, 0.2f);
        Sparks(at + Vector3.Down * 3f, Vector3.Forward, Spark.Hit);
        Dust.Puff(at + Vector3.Down * 3f);
    }

    // ── 카메라 ─────────────────────────────────────────

    /// <summary>화면을 dir 쪽으로 툭 민다 (m/s 충격).</summary>
    public static void Kick(Vector3 dir, float strength)
    {
        dir.Y = 0f;
        if (dir.LengthSquared() > 1e-6f)
            _i._kickVel += dir.Normalized() * strength;
    }

    /// <summary>떨림 충격량 0..1 을 더한다. 떨림 크기는 충격량의 제곱 (작은 건 거의 안 흔들리고 큰 건 확 흔들린다).</summary>
    public static void Shake(float trauma) => _i._trauma = Mathf.Min(1f, _i._trauma + trauma);

    // ── 화면 ───────────────────────────────────────────

    public static void Flash(Color c, float amount)
    {
        _i._flash = Mathf.Max(_i._flash, amount);
        _i._screen.SetShaderParameter("flash_color", c);
    }

    /// <summary>가장자리 색을 hold 초 동안 띄웠다가 걷는다.</summary>
    public static void Vignette(Color c, float amount, float hold)
    {
        _i._vignetteColor = c;
        _i._vignette = Mathf.Max(_i._vignette, amount);
        _i._vignetteHold = hold;
    }

    public static void Ring(Vector3 worldAt)
    {
        _i._ringAt = worldAt;
        _i._ringAge = 0f;
    }

    // ── 월드 이펙트 ─────────────────────────────────────

    /// <summary>불꽃: 맞은 방향(dir)으로 튄다.</summary>
    public static void Sparks(Vector3 at, Vector3 dir, Spark kind)
    {
        var e = _i._sparks[_i._nextSpark];
        _i._nextSpark = (_i._nextSpark + 1) % _i._sparks.Count;
        var pm = (ParticleProcessMaterial)e.ProcessMaterial;
        var look = SparkLook(kind);
        dir.Y = 0f;
        dir = dir.LengthSquared() > 1e-6f ? dir.Normalized() : Vector3.Forward;
        pm.Direction = (dir + Vector3.Up * 0.35f).Normalized();
        pm.Spread = look.spread;
        pm.InitialVelocityMin = look.vmin;
        pm.InitialVelocityMax = look.vmax;
        pm.ColorRamp = _i._ramps[kind];
        if (e.Amount != look.n)
            e.Amount = look.n; // 바꾸면 버퍼를 다시 잡는다
        e.Lifetime = look.life;
        e.GlobalPosition = at;
        e.Restart();
    }

    private static (int n, float vmin, float vmax, float spread, float life, Color head, Color tail) SparkLook(Spark kind) => kind switch
    {
        Spark.Heavy => (20, 6f, 12f, 55f, 0.28f, new Color(1f, 1f, 1f), new Color(0.3f, 0.9f, 1f)),
        Spark.Block => (9, 3f, 6.5f, 60f, 0.2f, new Color(1f, 0.92f, 0.6f), new Color(1f, 0.45f, 0.1f)),
        Spark.Parry => (30, 7f, 15f, 80f, 0.34f, new Color(1f, 1f, 1f), new Color(1f, 0.8f, 0.25f)),
        _ => (11, 5f, 9f, 38f, 0.18f, new Color(1f, 1f, 1f), new Color(0.55f, 0.95f, 1f)),
    };

    /// <summary>
    /// 맞은 자리에 번쩍이는 4갈래 별. 히트스톱으로 세상이 멈춘 동안에도 떠 있어서 "맞았다"가 한 장면으로 박힌다.
    /// </summary>
    public static void Star(Vector3 at, float sizeM, Color color, float life)
    {
        var st = _i._stars[_i._nextStar];
        _i._nextStar = (_i._nextStar + 1) % PoolSize;
        st.Star.AlbedoColor = color;
        st.Star.Emission = color;
        ((QuadMesh)st.Mesh.Mesh).Size = new Vector2(sizeM, sizeM);
        st.Mesh.GlobalPosition = GameRoot.Instance.View.SnapToTexel(at);
        st.Mesh.Scale = Vector3.One;
        st.Mesh.Visible = true;
        st.Age = 0f;
        st.Life = life;
        st.Live = true;
    }

    /// <summary>
    /// 베기 궤적. 고리 조각을 center 둘레에 깔고 머리가 호를 따라 쓸고 간다.
    /// forward = 호의 가운데가 향하는 쪽, bend = 호가 휘어 나가는 쪽 (가로 베기면 둘 다 수평, 내려찍기면 bend = 위).
    /// </summary>
    public static void Slash(Vector3 center, Vector3 forward, Vector3 bend, float radius, float arcDeg,
        float duration, bool reverse = false, float thickness = 1f)
    {
        var key = (radius, arcDeg, reverse);
        if (!_i._arcs.TryGetValue(key, out var arc))
            _i._arcs[key] = arc = ArcMesh(radius, arcDeg, reverse);
        var sl = _i._slashes[_i._nextSlash];
        _i._nextSlash = (_i._nextSlash + 1) % PoolSize;
        sl.Mesh.Mesh = arc;
        // 메시는 로컬 XZ 평면(X = 정면, Z = 휘는 쪽)에 만들어 두고 방향은 기저로 돌린다
        forward = forward.Normalized();
        bend = bend.Normalized();
        sl.Mesh.GlobalTransform = new Transform3D(new Basis(forward, bend.Cross(forward).Normalized(), bend), center);
        sl.Slash.SetShaderParameter("progress", 0f);
        sl.Slash.SetShaderParameter("dissolve", 0f);
        sl.Slash.SetShaderParameter("thickness", thickness);
        sl.Slash.SetShaderParameter("seed", GD.Randf() * 100f);
        sl.Mesh.Visible = true;
        sl.Age = 0f;
        sl.Life = duration;
        sl.Live = true;
    }

    public override void _Process(double delta)
    {
        float real = (float)delta;
        float heroDt = real * GameRoot.Instance.HeroScale;
        _t += real;

        // 카메라 용수철 + 떨림
        Vector3 acc = -_kick * KickStiffness - _kickVel * KickDamping;
        _kickVel += acc * real;
        _kick += _kickVel * real;
        _trauma = Mathf.Max(0f, _trauma - TraumaDecay * real);
        float s = _trauma * _trauma * ShakeMax;
        var shake = new Vector3(
            Mathf.Sin(_t * 71f) * 0.6f + Mathf.Sin(_t * 113f + 1.7f) * 0.4f,
            Mathf.Sin(_t * 89f + 0.3f) * 0.6f + Mathf.Sin(_t * 127f + 2.9f) * 0.4f, 0f) * s;
        GameRoot.Instance.Rig.Shake = _kick + shake;

        // 화면
        _flash = Mathf.MoveToward(_flash, 0f, real * 9f);
        _vignetteHold -= real;
        if (_vignetteHold <= 0f)
            _vignette = Mathf.MoveToward(_vignette, 0f, real * 2.5f);
        float slow = GameRoot.Instance.SlowAmount;
        _screen.SetShaderParameter("flash", _flash);
        _screen.SetShaderParameter("vignette", Mathf.Max(_vignette, slow * 0.9f));
        _screen.SetShaderParameter("vignette_color", _vignette > slow ? _vignetteColor : new Color(0.3f, 0.12f, 0.55f));
        if (_ringAge >= 0f)
        {
            _ringAge += real;
            var view = GameRoot.Instance.View;
            Vector2 sp = view.Camera.UnprojectPosition(_ringAt) / (Vector2)view.Viewport.Size;
            _screen.SetShaderParameter("ring_center", sp);
            _screen.SetShaderParameter("ring_radius", _ringAge < RingLife ? Mathf.Sqrt(_ringAge / RingLife) * 1.1f : -1f);
            _screen.SetShaderParameter("ring_width", Mathf.Lerp(0.012f, 0.004f, _ringAge / RingLife));
            if (_ringAge >= RingLife)
                _ringAge = -1f;
        }

        // 베기 궤적은 주인공 시간 — 히트스톱 동안 궤적도 멈춘다
        foreach (var sl in _slashes)
        {
            if (!sl.Live)
                continue;
            sl.Age += heroDt;
            // 앞 45% 에 끝까지 긋고, 나머지 동안 천천히 밀려나며 흩어진다
            float u = sl.Age / sl.Life;
            const float Sweep = 0.45f;
            float p = u < Sweep ? u / Sweep : 1f + (u - Sweep) / (1f - Sweep) * SlashTail * 0.5f;
            sl.Slash.SetShaderParameter("progress", p);
            sl.Slash.SetShaderParameter("dissolve", Mathf.Clamp((u - Sweep) / (1f - Sweep), 0f, 1f));
            if (u >= 1f)
                sl.Live = sl.Mesh.Visible = false;
        }
        // 별은 실제 시간 — 히트스톱 동안 떠 있다가 풀리면 사라진다
        foreach (var st in _stars)
        {
            if (!st.Live)
                continue;
            st.Age += real;
            float k = st.Age / st.Life;
            if (k >= 1f)
            {
                st.Live = st.Mesh.Visible = false;
                continue;
            }
            st.Mesh.Scale = Vector3.One * (k < 0.5f ? 1f : Mathf.Lerp(1f, 0.3f, (k - 0.5f) * 2f));
        }
    }

    private Pooled MakePooled(ShaderMaterial slash, StandardMaterial3D star)
    {
        var mesh = new MeshInstance3D
        {
            Mesh = star != null ? new QuadMesh { Size = Vector2.One } : null,
            MaterialOverride = slash != null ? slash : star,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Visible = false,
        };
        GameRoot.Instance.Stage.AddChild(mesh);
        return new Pooled { Mesh = mesh, Slash = slash, Star = star };
    }

    private GpuParticles3D MakeSparkEmitter()
    {
        var pm = new ParticleProcessMaterial
        {
            EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Sphere,
            EmissionSphereRadius = 0.1f,
            Gravity = new Vector3(0f, -9f, 0f),
            DampingMin = 6f,
            DampingMax = 10f,
            ScaleMin = 0.7f,
            ScaleMax = 1.2f,
        };
        var mat = new ShaderMaterial { Shader = _sparkShader };
        var e = new GpuParticles3D
        {
            OneShot = true,
            Emitting = false,
            Amount = 11,
            Lifetime = 0.2f,
            Explosiveness = 1f,
            ProcessMaterial = pm,
            // 2x10 픽셀 줄을 날아가는 방향으로 세운다
            DrawPass1 = new QuadMesh { Size = new Vector2(2f / Px.PerMeter, 10f / Px.PerMeter), Material = mat },
            TransformAlign = GpuParticles3D.TransformAlignEnum.ZBillboardYToVelocity,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            LocalCoords = false,
            VisibilityAabb = new Aabb(new Vector3(-6, -3, -6), new Vector3(12, 8, 12)),
        };
        e.AddToGroup(GameRoot.WorldParticles);
        GameRoot.Instance.Stage.AddChild(e);
        return e;
    }

    private static GradientTexture1D MakeRamp(Color head, Color tail)
    {
        var ramp = new Gradient();
        ramp.SetColor(0, head);
        ramp.SetColor(1, new Color(tail, 0f));
        ramp.AddPoint(0.35f, tail);
        return new GradientTexture1D { Gradient = ramp };
    }

    /// <summary>호 메시 (로컬 XZ 평면, X = 호 가운데). UV.x = 휘두르는 방향으로 0..1, UV.y = 안 0 .. 밖 1.</summary>
    private static ArrayMesh ArcMesh(float radius, float arcDeg, bool reverse)
    {
        const int Segs = 28;
        const float Inner = 0.35f; // 안쪽 반지름 비율
        var verts = new Vector3[(Segs + 1) * 2];
        var uvs = new Vector2[verts.Length];
        var idx = new List<int>();
        float half = Mathf.DegToRad(arcDeg) * 0.5f;
        for (int i = 0; i <= Segs; i++)
        {
            float u = i / (float)Segs;
            float th = Mathf.Lerp(-half, half, reverse ? 1f - u : u);
            // th=0 이 +X(정면), 음수 쪽이 -Z (휘는 쪽의 반대)
            var dir = new Vector3(Mathf.Cos(th), 0f, Mathf.Sin(th));
            verts[i * 2] = dir * radius * Inner;
            verts[i * 2 + 1] = dir * radius;
            uvs[i * 2] = new Vector2(u, 0f);
            uvs[i * 2 + 1] = new Vector2(u, 1f);
            if (i < Segs)
            {
                int b = i * 2;
                idx.AddRange(new[] { b, b + 1, b + 3, b, b + 3, b + 2 });
            }
        }
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = verts;
        arrays[(int)Mesh.ArrayType.TexUV] = uvs;
        arrays[(int)Mesh.ArrayType.Index] = idx.ToArray();
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        return mesh;
    }

    /// <summary>15x15 픽셀 4갈래 별 (가운데 흰 덩어리 + 십자 빛줄기).</summary>
    private static ImageTexture MakeStar()
    {
        const int N = 15, C = 7;
        var img = Image.CreateEmpty(N, N, false, Image.Format.Rgba8);
        for (int y = 0; y < N; y++)
        {
            for (int x = 0; x < N; x++)
            {
                int dx = Mathf.Abs(x - C), dy = Mathf.Abs(y - C);
                bool core = dx + dy <= 2;
                bool ray = (dx == 0 && dy <= 7) || (dy == 0 && dx <= 7) || (dx == 1 && dy <= 3) || (dy == 1 && dx <= 3);
                if (core || ray)
                    img.SetPixel(x, y, Colors.White);
            }
        }
        return ImageTexture.CreateFromImage(img);
    }
}
