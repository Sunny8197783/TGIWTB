using Godot;
using PixelMmo.Balance;
using PixelMmo.Data;
using PixelMmo.Runtime;

namespace PixelMmo.Combat;

/// <summary>
/// 플레이어가 쏜 것. 화살·불꽃·얼음창이 전부 이 하나다 —
/// 다른 것은 속도·개수·색·관통뿐이고, 그건 스킬 정의(projectile)에 있다.
///
/// 몬스터의 화살(<see cref="Arrow"/>)과 나누어 둔 이유는 진영이 반대라서다.
/// 이쪽은 몬스터를 맞히고, 그쪽은 플레이어를 맞힌다 — 충돌 마스크가 정반대다.
/// </summary>
public partial class SkillBolt : Area2D
{
    private Vector2 _velocity;
    private bool _spent;
    private readonly Godot.Collections.Array<Rid> _excluded = new();
    private float _damage;
    private float _life;
    private float _radius = 4f;
    private bool _pierce;
    private Color _color = Colors.White;
    public Color VisualColor => _color;
    private SkillDefinition _skill;
    private Node2D _shooter;

    /// <summary>관통일 때 같은 대상을 두 번 때리지 않게 한다.</summary>
    private readonly System.Collections.Generic.HashSet<ulong> _hit = new();

    public void Setup(Vector2 direction, SkillProjectile proj, float damage,
        SkillDefinition skill, Node2D shooter)
    {
        _velocity = direction.Normalized() * proj.SpeedPx;
        _damage = damage;
        _life = proj.LifeSeconds;
        _radius = proj.RadiusPx;
        _pierce = proj.Pierce;
        _color = PlayerCharacter.ParseColor(proj.Color, Colors.White);
        _skill = skill;
        _shooter = shooter;
        Rotation = _velocity.Angle();
    }

    public override void _Ready()
    {
        Name = "SkillBolt";
        CollisionLayer = CollisionLayers.Projectile;
        CollisionMask = CollisionLayers.World | CollisionLayers.Monster;

        // 바닥(-10)과 캐릭터(0) 사이가 아니라 위로 띄운다 — 날아가는 것은 가려지면 안 된다.
        ZIndex = 5;

        AddChild(new CollisionShape2D { Shape = new CircleShape2D { Radius = _radius } });
        Monitoring = false;
        Monitorable = false;
        _excluded.Add(GetRid());
        if (_shooter is CollisionObject2D source) _excluded.Add(source.GetRid());
    }

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
        if (_spent) return;
        Vector2 end = GlobalPosition + _velocity * Mathf.Min(dt, _life);
        for (int contacts = 0; contacts < 64; contacts++)
        {
            if (!CombatCollision.Sweep(this, GlobalPosition, end - GlobalPosition,
                _radius, CollisionLayers.World | CollisionLayers.Monster, _excluded, out var body, out var point))
            { GlobalPosition = end; break; }
            GlobalPosition = point;
            if (body is CollisionObject2D collider) _excluded.Add(collider.GetRid());
            if (body == null) { _spent = true; QueueFree(); break; }
            OnBodyEntered(body);
            if (_spent) break;
            if (contacts == 63) { _spent = true; QueueFree(); }
        }

        _life -= dt;
        if (_life <= 0f)
            QueueFree();
    }

    public override void _Draw()
    {
        // 회전 적용 후 좌표계이므로 진행 방향이 +X 다.
        // 꼬리를 흐릿하게 남겨 방향과 속도가 한눈에 읽히게 한다.
        var tail = new Color(_color.R, _color.G, _color.B, 0.35f);
        DrawRect(new Rect2(-_radius * 5f, -_radius * 0.4f, _radius * 5f, _radius * 0.8f), tail);
        DrawRect(new Rect2(-_radius, -_radius * 0.6f, _radius * 3f, _radius * 1.2f), _color);
    }

    private void OnBodyEntered(Node2D body)
    {
        if (body == _shooter)
            return;

        if (body is MonsterBase monster && monster.IsAlive)
        {
            // 관통이라도 같은 대상은 한 번만. 몸이 두꺼운 몬스터를 스치면
            // 한 프레임에 두세 번 겹쳐 들어간다.
            if (!_hit.Add(monster.GetInstanceId()))
                return;

            monster.TakeDamage(new DamageInfo
            {
                Amount = _damage,
                Direction = _velocity.Normalized(),
                Heavy = _skill?.Heavy ?? false,
                SkillId = _skill?.Id ?? "",
                Source = _shooter,
            });
            CombatFeedback.Instance?.OnHit(GlobalPosition, _damage, _skill?.Heavy ?? false, !monster.IsAlive, _velocity.Normalized());

            // 명중해야 숙련이 오른다. 허공에 쏜 화살은 '유효한 사용'이 아니다. (규칙 3)
            if (_shooter is PlayerCharacter player)
                // 위 조건에서 이미 살아 있는 것만 걸렀으므로 targetWasAlive 는 항상 참이다.
                player.ReportProjectileHit(_skill, monster, targetWasAlive: true);

            if (_pierce)
                return;
        }

        // 벽이든 몬스터든 닿으면 소멸(관통은 위에서 이미 빠져나갔다).
        _spent = true;
        QueueFree();
    }
}
