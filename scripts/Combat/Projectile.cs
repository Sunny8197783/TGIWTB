using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using PixelMmo.Core;
using PixelMmo.Data;
using PixelMmo.Render;

namespace PixelMmo.Combat;

/// <summary>
/// 주인공이 쏘는 투사체 — 화살·마법탄·터지는 구슬 (직업 평타 JobDef.ProjectileDef, 스킬 이벤트 "projectile").
/// 가슴 높이로 수평으로 날아가고, 적의 반지름 안에 들면 맞는다. 언덕에 박히거나 사거리를 다 가면 멈춘다(구슬은 터진다).
/// 판은 카메라를 보고 서 있고 가로축이 날아가는 쪽 — 화살은 비스듬히 날아도 화면에서 그 방향을 가리킨다.
/// </summary>
public partial class Projectile : Node3D
{
    private const float Height = 0.9f;          // 날아가는 높이 (m, 발에서)
    private const float HomingSight = 9f;       // 유도: 이 거리 안의 적만 쫓는다
    private const float HomingCone = 70f;       // 유도: 앞쪽 반각 (도)

    private JobDef.ProjectileDef _def;
    private float _damage;
    private bool _heavy;
    private Vector3 _dir;
    private float _travelled;
    private int _pierceLeft;
    private CombatFx.Palette _pal;
    private Action<Enemy> _onHit;
    private readonly HashSet<Enemy> _hit = new();
    private MeshInstance3D _mesh;
    private ShaderMaterial _mat;
    private float _trail;

    private static Shader _shader;
    private static readonly QuadMesh Quad = new() { Size = Vector2.One };

    public static void Fire(Vector3 feet, Vector3 dir, JobDef.ProjectileDef def, float damage, bool heavy, CombatFx.Palette pal,
                            Action<Enemy> onHit = null)
    {
        int n = Math.Max(1, def.Count);
        for (int i = 0; i < n; i++)
        {
            float a = n == 1 ? 0f : Mathf.DegToRad(Mathf.Lerp(-def.SpreadDeg, def.SpreadDeg, i / (float)(n - 1)));
            var p = new Projectile
            {
                _def = def, _damage = damage, _heavy = heavy, _dir = dir.Rotated(Vector3.Up, a).Normalized(),
                _pierceLeft = def.Pierce, _pal = pal, _onHit = onHit,
            };
            GameRoot.Instance.Stage.AddChild(p);
            p.GlobalPosition = feet + Vector3.Up * Height + p._dir * 0.5f;
        }
    }

    private const float FallHeight = 9f;
    private static readonly Vector3 FallDir = new Vector3(0.18f, -1f, 0.12f).Normalized();

    /// <summary>화살비: center 둘레 radius 안에 count 개가 duration 동안 하늘에서 떨어진다</summary>
    public static void Rain(Vector3 center, float radius, int count, float duration, JobDef.ProjectileDef def, float damage,
                            CombatFx.Palette pal, Action<Enemy> onHit = null)
    {
        var tree = GameRoot.Instance.GetTree();
        var rng = new RandomNumberGenerator();
        for (int i = 0; i < count; i++)
        {
            float a = rng.Randf() * Mathf.Tau, r = Mathf.Sqrt(rng.Randf()) * radius;
            Vector3 spot = center + new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
            tree.CreateTimer(duration * i / count).Timeout += () =>
            {
                var p = new Projectile { _def = def, _damage = damage, _heavy = false, _dir = FallDir, _pal = pal, _onHit = onHit };
                GameRoot.Instance.Stage.AddChild(p);
                p.GlobalPosition = new Vector3(spot.X, GameRoot.Instance.World.WalkHeightAt(spot.X, spot.Z), spot.Z) - FallDir * FallHeight;
            };
        }
    }

    public override void _Ready()
    {
        _shader ??= GD.Load<Shader>("res://shaders/projectile.gdshader");
        _mat = new ShaderMaterial { Shader = _shader };
        bool arrow = _def.Kind == "arrow";
        float lenPx = arrow ? 18f : _def.Kind == "orb" ? 28f : 20f;
        float thickPx = arrow ? 5f : _def.Kind == "orb" ? 20f : 12f;
        _mat.SetShaderParameter("kind", arrow ? 0 : 1);
        _mat.SetShaderParameter("px", new Vector2(lenPx, thickPx));
        _mat.SetShaderParameter(Uniform.Core, _pal.Core);
        _mat.SetShaderParameter(Uniform.Bright, _pal.Bright);
        _mat.SetShaderParameter(Uniform.Mid, _pal.Mid);
        _mat.SetShaderParameter(Uniform.Deep, _pal.Deep);
        _mesh = new MeshInstance3D { Mesh = Quad, MaterialOverride = _mat, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                                     Scale = new Vector3(lenPx, thickPx, 1f) / Px.PerMeter };
        AddChild(_mesh);
        Orient();
        if (!arrow)
            CombatFx.SparkleBurst(GlobalPosition, 8, 0.4f, _pal);
    }

    /// <summary>판의 가로축을 날아가는 쪽에, 면은 카메라 쪽(위로 기운 남쪽)에 둔다.</summary>
    private void Orient()
    {
        var toCam = new Vector3(0f, Mathf.Sin(Mathf.DegToRad(Px.PitchDeg)), Mathf.Cos(Mathf.DegToRad(Px.PitchDeg)));
        Vector3 x = (_dir - toCam * _dir.Dot(toCam)).Normalized();
        Vector3 y = toCam.Cross(x).Normalized();
        _mesh.Basis = new Basis(x, y, toCam).Scaled(_mesh.Scale);
    }

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta * GameRoot.Instance.HeroScale;
        if (dt <= 0f)
            return;
        if (_def.Homing > 0f)
            Home(dt);
        float step = _def.Speed * dt;
        GlobalPosition += _dir * step;
        _travelled += step;
        _mat.SetShaderParameter("flicker", _travelled);
        if (_def.Kind != "arrow")
        {
            _trail -= dt;
            if (_trail <= 0f)
            {
                _trail = 0.05f;
                CombatFx.SparkleBurst(GlobalPosition, 2, 0.15f, _pal);
            }
        }

        var p = GlobalPosition;
        if (_def.Fall)
        {
            float ground = GameRoot.Instance.World.WalkHeightAt(p.X, p.Z);
            if (p.Y > ground + 0.4f)
                return;
            // 땅에 꽂힌 자리 둘레를 친다
            foreach (var e in Enemy.All.ToArray())
            {
                if (!e.Alive || new Vector2(e.GlobalPosition.X - p.X, e.GlobalPosition.Z - p.Z).Length() > _def.Radius + e.Radius)
                    continue;
                _onHit?.Invoke(e);
                Hero.Instance.Strike(e, _damage, Vector3.Down, false);
            }
            Dust.Puff(new Vector3(p.X, ground, p.Z), 2, Vector3.Zero);
            QueueFree();
            return;
        }
        foreach (var e in Enemy.All.ToArray())
        {
            if (!e.Alive || _hit.Contains(e))
                continue;
            Vector3 to = e.GlobalPosition - p;
            if (new Vector2(to.X, to.Z).Length() > _def.Radius + e.Radius)
                continue;
            _hit.Add(e);
            if (_def.Explode > 0f)
            {
                Burst();
                return;
            }
            var hero = Hero.Instance;
            _onHit?.Invoke(e); // 맞히기 전에 (숙련은 산 적에게 맞은 시전만 — 이 한 방에 쓰러질 수도 있다)
            hero.Strike(e, _damage, _dir, _heavy);
            hero.ApplyImpact(_heavy ? CombatTuning.HitHeavy : CombatTuning.HitProjectile, _dir);
            if (_pierceLeft-- <= 0)
            {
                QueueFree();
                return;
            }
        }
        // 언덕에 박히거나 다 날아갔다
        if (_travelled >= _def.Range || GameRoot.Instance.World.WalkHeightAt(p.X, p.Z) > p.Y - 0.2f)
        {
            if (_def.Explode > 0f)
                Burst();
            else
            {
                Dust.Puff(new Vector3(p.X, GameRoot.Instance.World.WalkHeightAt(p.X, p.Z), p.Z), 3, -_dir);
                QueueFree();
            }
        }
    }

    /// <summary>앞쪽 가까운 적 쪽으로 조금씩 휜다</summary>
    private void Home(float dt)
    {
        Enemy best = null;
        float bestD = HomingSight;
        foreach (var e in Enemy.All)
        {
            if (!e.Alive || _hit.Contains(e))
                continue;
            Vector3 to = e.GlobalPosition - GlobalPosition;
            to.Y = 0f;
            float d = to.Length();
            if (d < bestD && Mathf.RadToDeg(_dir.AngleTo(to)) < HomingCone)
            {
                bestD = d;
                best = e;
            }
        }
        if (best == null)
            return;
        Vector3 want = best.GlobalPosition - GlobalPosition;
        want.Y = 0f;
        float angle = _dir.SignedAngleTo(want.Normalized(), Vector3.Up);
        float max = Mathf.DegToRad(_def.Homing) * dt;
        _dir = _dir.Rotated(Vector3.Up, Mathf.Clamp(angle, -max, max)).Normalized();
        Orient();
    }

    /// <summary>구슬: 그 자리에서 터져 둘레를 친다</summary>
    private void Burst()
    {
        var hero = Hero.Instance;
        Vector3 at = GlobalPosition;
        int hits = 0;
        foreach (var e in Enemy.All.ToArray())
        {
            if (!e.Alive)
                continue;
            Vector3 to = e.GlobalPosition - at;
            to.Y = 0f;
            if (to.Length() - e.Radius > _def.Explode)
                continue;
            _onHit?.Invoke(e);
            hero.Strike(e, _damage, to.LengthSquared() > 0.01f ? to.Normalized() : _dir, true);
            hits++;
        }
        if (hits > 0)
            hero.ApplyImpact(CombatTuning.HitHeavy, _dir);
        CombatFx.Shockwave(at, _def.Explode * 1.3f, 0.45f, _pal);
        CombatFx.SparkleBurst(at, 30, _def.Explode * 0.7f, _pal);
        CombatFx.Star(at, 1.2f, _pal.Bright, 0.14f);
        Sfx.Play("impactPlate_heavy", -4f, 0.7f, 0.05f, "Hero");
        QueueFree();
    }
}
