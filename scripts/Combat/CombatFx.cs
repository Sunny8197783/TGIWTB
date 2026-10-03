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

    /// <summary>4단 색 램프: 흰 심 → 밝은 → 중간 → 짙은. 이펙트는 이 넷만으로 칠해 픽셀아트답게.</summary>
    public readonly record struct Palette(Color Core, Color Bright, Color Mid, Color Deep);

    public static readonly Dictionary<string, Palette> Palettes = new()
    {
        ["teal"] = new(Colors.White, new Color(0.55f, 0.98f, 1f), new Color(0.16f, 0.72f, 0.82f), new Color(0.04f, 0.3f, 0.42f)),
        ["moon"] = new(Colors.White, new Color(0.75f, 0.9f, 1f), new Color(0.4f, 0.6f, 1f), new Color(0.2f, 0.22f, 0.6f)),
        ["sakura"] = new(Colors.White, new Color(1f, 0.82f, 0.9f), new Color(1f, 0.5f, 0.7f), new Color(0.6f, 0.18f, 0.42f)),
        ["thunder"] = new(Colors.White, new Color(1f, 0.97f, 0.6f), new Color(0.6f, 0.8f, 1f), new Color(0.3f, 0.3f, 0.9f)),
    };
    public static Palette PaletteOf(string name) => name != null && Palettes.TryGetValue(name, out var p) ? p : Palettes["teal"];

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
    private readonly List<Pooled> _rings = new();
    private readonly List<Pooled> _bolts = new();
    private readonly List<GpuParticles3D> _sparkles = new();
    private readonly List<GpuParticles3D> _petals = new();
    private int _nextRing, _nextBolt, _nextSparkle, _nextPetal;
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
        var ringShader = GD.Load<Shader>("res://shaders/shockwave.gdshader");
        var boltShader = GD.Load<Shader>("res://shaders/lightning.gdshader");
        for (int i = 0; i < 6; i++) // 진화형 뇌격이 번개 다섯 줄기를 한꺼번에 쓴다
        {
            var ring = MakePooled(new ShaderMaterial { Shader = ringShader }, null);
            ring.Mesh.Mesh = new PlaneMesh { Size = Vector2.One };
            _rings.Add(ring);
            var bolt = MakePooled(new ShaderMaterial { Shader = boltShader }, null);
            bolt.Mesh.Mesh = new QuadMesh { Size = new Vector2(32f / Px.PerMeter, 8f), CenterOffset = new Vector3(0f, 4f, 0f) };
            _bolts.Add(bolt);
            _sparkles.Add(MakeBurstEmitter(sparkle: true));
            _petals.Add(MakeBurstEmitter(sparkle: false));
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
        Shockwave(at + Vector3.Down * 3f, 1f, 0.1f, Palettes["teal"]);
        Lightning(at + Vector3.Down * 12f, 0.05f, Palettes["thunder"]);
        SparkleBurst(at + Vector3.Down * 3f, 4, 0.5f, Palettes["teal"]);
        Petals(at + Vector3.Down * 3f, Vector3.Forward, 4, 0.5f);
        // 적 공격 예고 원 (Enemy.ShowWarning) — 첫 고블린 내려찍기에서 멈칫했다
        var warn = new MeshInstance3D
        {
            Mesh = new PlaneMesh { Size = Vector2.One },
            MaterialOverride = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/ground_warn.gdshader") },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        GameRoot.Instance.Stage.AddChild(warn);
        warn.GlobalPosition = at + Vector3.Down * 3f;
        GetTree().CreateTimer(0.2).Timeout += warn.QueueFree;
        // 궤적 메시도 처음 쓰는 순간 만들면 그 프레임이 끊긴다 — 기본 공격·스킬에 나오는 모양을 전부 미리
        foreach (var step in CombatTuning.Combo)
        {
            WarmArc(step.Reach, step.Heavy ? 170f : 200f, step.Heavy || step.Anim == "attack2");
            WarmArc(step.Reach, 200f, false);
        }
        foreach (var def in Data.SkillDef.All.Values)
            foreach (var ev in def.Events)
                if (ev.Fx == "slash")
                    WarmArc(ev.Radius, ev.Shape == "spin" ? 360f : ev.Arc, ev.Shape is "reverse" or "vertical");
        // 개발: --fxtest 로 번개 하나를 오래 세워 둔다 (그림 확인용)
        foreach (string a in OS.GetCmdlineUserArgs())
            if (a == "--fxtest")
                GetTree().CreateTimer(1.2).Timeout += () => Lightning(Hero.Instance.GlobalPosition + new Vector3(1.5f, 0f, 0f), 5f, Palettes["thunder"]);
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
        _i._screen.SetShaderParameter(Uniform.FlashColor, c);
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
    private static readonly bool NoSparks = Dev.DevCapture.Disabled().Contains("sparks");

    public static void Sparks(Vector3 at, Vector3 dir, Spark kind)
    {
        if (NoSparks)
            return;
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
        float duration, bool reverse = false, float thickness = 1f, Palette? palette = null)
    {
        var pal = palette ?? Palettes["teal"];
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
        sl.Slash.SetShaderParameter(Uniform.Progress, 0f);
        sl.Slash.SetShaderParameter(Uniform.Dissolve, 0f);
        sl.Slash.SetShaderParameter(Uniform.Thickness, thickness);
        sl.Slash.SetShaderParameter(Uniform.Seed, GD.Randf() * 100f);
        sl.Slash.SetShaderParameter(Uniform.Core, pal.Core);
        sl.Slash.SetShaderParameter(Uniform.Bright, pal.Bright);
        sl.Slash.SetShaderParameter(Uniform.Mid, pal.Mid);
        sl.Slash.SetShaderParameter(Uniform.Deep, pal.Deep);
        sl.Mesh.Visible = true;
        sl.Age = 0f;
        sl.Life = duration;
        sl.Live = true;
    }

    /// <summary>바닥 충격파 고리. radius = 다 퍼졌을 때 반지름 (m).</summary>
    public static void Shockwave(Vector3 center, float radius, float life, Palette pal)
    {
        var r = _i._rings[_i._nextRing];
        _i._nextRing = (_i._nextRing + 1) % _i._rings.Count;
        float d = radius * 2f;
        float sinPitch = Mathf.Sin(Mathf.DegToRad(Px.PitchDeg));
        r.Mesh.Scale = new Vector3(d, 1f, d / sinPitch); // 화면에서 동그랗게
        r.Mesh.GlobalPosition = new Vector3(center.X, GameRoot.Instance.World.WalkHeightAt(center.X, center.Z) + 0.08f, center.Z);
        r.Slash.SetShaderParameter(Uniform.Px, d * Px.PerMeter);
        r.Slash.SetShaderParameter(Uniform.Core, pal.Core);
        r.Slash.SetShaderParameter(Uniform.Bright, pal.Bright);
        r.Slash.SetShaderParameter(Uniform.Mid, pal.Mid);
        r.Slash.SetShaderParameter(Uniform.Deep, pal.Deep);
        r.Slash.SetShaderParameter(Uniform.Progress, 0f);
        r.Mesh.Visible = true;
        r.Age = 0f;
        r.Life = life;
        r.Live = true;
    }

    /// <summary>하늘에서 땅으로 떨어지는 번개 줄기. 몇 프레임마다 모양이 바뀌며 번쩍인다.</summary>
    public static void Lightning(Vector3 ground, float life, Palette pal)
    {
        var b = _i._bolts[_i._nextBolt];
        _i._nextBolt = (_i._nextBolt + 1) % _i._bolts.Count;
        b.Mesh.GlobalPosition = ground;
        b.Slash.SetShaderParameter(Uniform.Core, pal.Core);
        b.Slash.SetShaderParameter(Uniform.Glow, pal.Bright);
        b.Slash.SetShaderParameter(Uniform.Fade, 1f);
        b.Mesh.Visible = true;
        b.Age = 0f;
        b.Life = life;
        b.Live = true;
    }

    /// <summary>반짝이 폭발: 사방으로 튀었다가 천천히 떠오르며 깜빡인다.</summary>
    public static void SparkleBurst(Vector3 center, int count, float radius, Palette pal)
    {
        var e = _i._sparkles[_i._nextSparkle];
        _i._nextSparkle = (_i._nextSparkle + 1) % _i._sparkles.Count;
        var pm = (ParticleProcessMaterial)e.ProcessMaterial;
        pm.EmissionSphereRadius = radius * 0.4f;
        pm.InitialVelocityMin = radius * 1.2f;
        pm.InitialVelocityMax = radius * 2.6f;
        pm.Color = pal.Bright;
        if (e.Amount != count)
            e.Amount = count;
        e.GlobalPosition = center;
        e.Restart();
    }

    /// <summary>꽃잎: dir 쪽으로 휘날리며 흩어진다.</summary>
    public static void Petals(Vector3 center, Vector3 dir, int count, float radius)
    {
        var e = _i._petals[_i._nextPetal];
        _i._nextPetal = (_i._nextPetal + 1) % _i._petals.Count;
        var pm = (ParticleProcessMaterial)e.ProcessMaterial;
        pm.EmissionSphereRadius = radius;
        pm.Direction = (dir.Normalized() + Vector3.Up * 0.5f).Normalized();
        if (e.Amount != count)
            e.Amount = count;
        e.GlobalPosition = center;
        e.Restart();
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
        _screen.SetShaderParameter(Uniform.Flash, _flash);
        _screen.SetShaderParameter(Uniform.Vignette, Mathf.Max(_vignette, slow * 0.9f));
        _screen.SetShaderParameter(Uniform.VignetteColor, _vignette > slow ? _vignetteColor : new Color(0.3f, 0.12f, 0.55f));
        if (_ringAge >= 0f)
        {
            _ringAge += real;
            var view = GameRoot.Instance.View;
            Vector2 sp = view.Camera.UnprojectPosition(_ringAt) / (Vector2)view.Viewport.Size;
            _screen.SetShaderParameter(Uniform.RingCenter, sp);
            _screen.SetShaderParameter(Uniform.RingRadius, _ringAge < RingLife ? Mathf.Sqrt(_ringAge / RingLife) * 1.1f : -1f);
            _screen.SetShaderParameter(Uniform.RingWidth, Mathf.Lerp(0.012f, 0.004f, _ringAge / RingLife));
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
            sl.Slash.SetShaderParameter(Uniform.Progress, p);
            sl.Slash.SetShaderParameter(Uniform.Dissolve, Mathf.Clamp((u - Sweep) / (1f - Sweep), 0f, 1f));
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
        // 충격파는 세계 시간 — 슬로우 중이면 천천히 퍼진다
        float worldDt = real * GameRoot.Instance.WorldScale;
        foreach (var r in _rings)
        {
            if (!r.Live)
                continue;
            r.Age += worldDt;
            r.Slash.SetShaderParameter(Uniform.Progress, Mathf.Min(r.Age / r.Life, 1f));
            if (r.Age >= r.Life)
                r.Live = r.Mesh.Visible = false;
        }
        foreach (var b in _bolts)
        {
            if (!b.Live)
                continue;
            b.Age += real;
            // 3프레임마다 새 모양, 끝으로 갈수록 깜빡이며 사라진다
            b.Slash.SetShaderParameter(Uniform.Seed, Mathf.Floor(b.Age * 20f));
            float k = b.Age / b.Life;
            b.Slash.SetShaderParameter(Uniform.Fade, k < 0.7f ? 1f : (Mathf.PosMod(b.Age, 0.06f) < 0.03f ? 1f - k : 0f));
            if (b.Age >= b.Life)
                b.Live = b.Mesh.Visible = false;
        }
    }

    private void WarmArc(float radius, float arcDeg, bool reverse)
    {
        var key = (radius, arcDeg, reverse);
        if (!_arcs.ContainsKey(key))
            _arcs[key] = ArcMesh(radius, arcDeg, reverse);
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

    private GpuParticles3D MakeBurstEmitter(bool sparkle)
    {
        var pm = new ParticleProcessMaterial
        {
            EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Sphere,
            EmissionSphereRadius = 0.5f,
            Direction = Vector3.Up,
            Spread = sparkle ? 180f : 60f,
            InitialVelocityMin = sparkle ? 2f : 3f,
            InitialVelocityMax = sparkle ? 5f : 7f,
            Gravity = sparkle ? new Vector3(0f, 0.8f, 0f) : new Vector3(0f, -1.2f, 0f),
            DampingMin = sparkle ? 5f : 2f,
            DampingMax = sparkle ? 8f : 3f,
            TurbulenceEnabled = !sparkle,
            TurbulenceNoiseStrength = 2f,
            TurbulenceNoiseScale = 2f,
            ScaleMin = sparkle ? 0.1f : 0.08f,
            ScaleMax = sparkle ? 0.16f : 0.12f,
            Color = sparkle ? Colors.White : new Color(1f, 0.72f, 0.84f),
        };
        var fade = new Gradient();
        fade.SetColor(0, Colors.White);
        fade.SetColor(1, new Color(1, 1, 1, 0));
        if (sparkle)
        {
            // 깜빡이며 사라지는 반짝임
            fade.AddPoint(0.5f, new Color(1, 1, 1, 0.3f));
            fade.AddPoint(0.6f, Colors.White);
            fade.AddPoint(0.75f, new Color(1, 1, 1, 0.2f));
        }
        pm.ColorRamp = new GradientTexture1D { Gradient = fade };
        if (!sparkle)
        {
            // 꽃잎은 분홍·연분홍·흰색이 섞인다
            var tint = new Gradient();
            tint.SetColor(0, new Color(1f, 0.6f, 0.78f));
            tint.SetColor(1, Colors.White);
            tint.AddPoint(0.5f, new Color(1f, 0.82f, 0.9f));
            pm.ColorInitialRamp = new GradientTexture1D { Gradient = tint };
        }
        var mat = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/particle_soft.gdshader") };
        mat.SetShaderParameter(Uniform.PxSize, sparkle ? 4f : 3f);
        mat.SetShaderParameter(Uniform.Softness, 0f);
        mat.SetShaderParameter(Uniform.Intensity, sparkle ? 2.6f : 1.1f);
        var e = new GpuParticles3D
        {
            OneShot = true,
            Emitting = false,
            Amount = 16,
            Lifetime = sparkle ? 0.7f : 1.2f,
            Explosiveness = sparkle ? 0.95f : 0.7f,
            ProcessMaterial = pm,
            DrawPass1 = new QuadMesh { Size = Vector2.One, Material = mat },
            LocalCoords = false,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            VisibilityAabb = new Aabb(new Vector3(-8, -3, -8), new Vector3(16, 10, 16)),
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
