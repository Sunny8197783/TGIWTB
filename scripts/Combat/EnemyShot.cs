using Godot;
using PixelMmo.Core;
using PixelMmo.Data;
using PixelMmo.Render;
using T = PixelMmo.Combat.CombatTuning;

namespace PixelMmo.Combat;

/// <summary>
/// 적이 쏘는 탄 (MonsterDef.ShotDef). 세계 시간으로 난다 — 완벽 회피 슬로우 때 같이 느려진다.
///  - 스치듯 피하면 완벽 회피, 막으면 사라지고,
///  - 패링하면 되돌아가 쏜 놈(과 길목의 적)을 친다: 멀리서 쏘는 적을 칼로 잡는 길.
/// </summary>
public partial class EnemyShot : Node3D
{
    private const float Height = 0.85f;
    private const float HeroRadius = 0.4f;

    private MonsterDef.ShotDef _def;
    private Enemy _owner;
    private float _damage;
    private Vector3 _dir;
    private float _travelled;
    private bool _reflected;
    private bool _spent;       // 주인공을 한 번 지나쳤다 (무적 프레임으로 빠져나감) — 다시 판정하지 않는다
    private CombatFx.Palette _pal;
    private MeshInstance3D _mesh;
    private ShaderMaterial _mat;
    private float _trail;

    private static Shader _shader;
    private static readonly QuadMesh Quad = new() { Size = Vector2.One };
    private static readonly StringName FlickerName = "flicker";

    public static void Fire(Enemy owner, Vector3 origin, Vector3 dir, MonsterDef.ShotDef def, float damage)
    {
        if (def == null)
            return;
        int n = Mathf.Max(1, def.Count);
        for (int i = 0; i < n; i++)
        {
            float a = n == 1 ? 0f : Mathf.DegToRad(Mathf.Lerp(-def.SpreadDeg, def.SpreadDeg, i / (float)(n - 1)));
            var s = new EnemyShot
            {
                _def = def, _owner = owner, _damage = damage, _dir = dir.Rotated(Vector3.Up, a).Normalized(),
                _pal = CombatFx.PaletteOf(def.Palette),
            };
            GameRoot.Instance.Stage.AddChild(s);
            s.GlobalPosition = origin + Vector3.Up * Height;
        }
    }

    public override void _Ready()
    {
        _shader ??= GD.Load<Shader>("res://shaders/projectile.gdshader");
        _mat = new ShaderMaterial { Shader = _shader };
        var px = new Vector2(18f, 12f);
        _mat.SetShaderParameter("kind", 1);
        _mat.SetShaderParameter("px", px);
        _mesh = new MeshInstance3D
        {
            Mesh = Quad, MaterialOverride = _mat, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Scale = new Vector3(px.X, px.Y, 1f) / Px.PerMeter,
        };
        AddChild(_mesh);
        Paint();
        Orient();
    }

    private void Paint()
    {
        _mat.SetShaderParameter(Uniform.Core, _pal.Core);
        _mat.SetShaderParameter(Uniform.Bright, _pal.Bright);
        _mat.SetShaderParameter(Uniform.Mid, _pal.Mid);
        _mat.SetShaderParameter(Uniform.Deep, _pal.Deep);
    }

    private void Orient()
    {
        var toCam = new Vector3(0f, Mathf.Sin(Mathf.DegToRad(Px.PitchDeg)), Mathf.Cos(Mathf.DegToRad(Px.PitchDeg)));
        Vector3 x = (_dir - toCam * _dir.Dot(toCam)).Normalized();
        Vector3 y = toCam.Cross(x).Normalized();
        _mesh.Basis = new Basis(x, y, toCam).Scaled(_mesh.Scale);
    }

    public override void _PhysicsProcess(double delta)
    {
        // 되받아친 탄은 주인공의 것 — 슬로우에도 제 속도
        float dt = (float)delta * (_reflected ? GameRoot.Instance.HeroScale : GameRoot.Instance.WorldScale);
        if (dt <= 0f)
            return;
        var hero = Hero.Instance;
        if (!_reflected && _def.Homing > 0f && hero != null && !_spent)
        {
            Vector3 want = hero.GlobalPosition - GlobalPosition;
            want.Y = 0f;
            if (want.LengthSquared() > 0.01f)
            {
                float angle = _dir.SignedAngleTo(want.Normalized(), Vector3.Up);
                float max = Mathf.DegToRad(_def.Homing) * dt;
                _dir = _dir.Rotated(Vector3.Up, Mathf.Clamp(angle, -max, max)).Normalized();
                Orient();
            }
        }
        float speed = _def.Speed * (_reflected ? T.ReflectSpeed : 1f);
        GlobalPosition += _dir * speed * dt;
        _travelled += speed * dt;
        _mat.SetShaderParameter(FlickerName, _travelled);
        _trail -= dt;
        if (_trail <= 0f)
        {
            _trail = 0.07f;
            CombatFx.SparkleBurst(GlobalPosition, 2, 0.12f, _pal);
        }

        Vector3 p = GlobalPosition;
        var world = GameRoot.Instance.World;
        if (_reflected)
        {
            foreach (var e in Enemy.All.ToArray())
            {
                if (!e.Alive)
                    continue;
                if (new Vector2(e.GlobalPosition.X - p.X, e.GlobalPosition.Z - p.Z).Length() > _def.Radius + e.Radius + 0.15f)
                    continue;
                hero?.Strike(e, _damage * T.ReflectDamage, _dir, true);
                hero?.ApplyImpact(T.HitHeavy, _dir);
                Burst();
                return;
            }
        }
        else if (hero != null && hero.IsAlive && !_spent)
        {
            Vector3 to = hero.GlobalPosition - p;
            float d = new Vector2(to.X, to.Z).Length();
            Enemy src = IsInstanceValid(_owner) ? _owner : null;
            if (d <= (_def.Radius + HeroRadius) * T.PerfectDodgeReach && hero.TryPerfectDodge(src))
                _spent = true;
            else if (d <= _def.Radius + HeroRadius)
            {
                switch (hero.ReceiveHit(p - _dir * 0.6f, _damage, _dir))
                {
                    case HitResult.Parried:
                        Reflect(hero, src);
                        return;
                    case HitResult.Dodged:
                        _spent = true;
                        break;
                    default:
                        Burst();
                        return;
                }
            }
        }
        if (_travelled >= _def.Range * (_reflected ? 2f : 1f) || world.WalkHeightAt(p.X, p.Z) > p.Y - 0.2f)
            Burst();
    }

    /// <summary>패링: 쏜 놈에게 되돌아간다 (없으면 날아온 길로)</summary>
    private void Reflect(Hero hero, Enemy src)
    {
        _reflected = true;
        _travelled = 0f;
        Vector3 back = src != null && src.Alive ? src.GlobalPosition - GlobalPosition : -_dir;
        back.Y = 0f;
        _dir = back.LengthSquared() > 0.01f ? back.Normalized() : -_dir;
        _pal = CombatFx.PaletteOf(hero.JobPalette);
        Paint();
        Orient();
        hero.CountDeed("reflect");
        CombatFx.SparkleBurst(GlobalPosition, 16, 0.6f, _pal);
    }

    private void Burst()
    {
        CombatFx.SparkleBurst(GlobalPosition, 10, 0.45f, _pal);
        CombatFx.Star(GlobalPosition, 0.5f, _pal.Bright, 0.08f);
        QueueFree();
    }
}
