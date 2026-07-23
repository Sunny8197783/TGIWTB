using Godot;
using PixelMmo.Balance;

namespace PixelMmo.Combat;

/// <summary>
/// 고블린 궁수의 화살. 도형 하나(작은 사각형)로 그린다. (§A 아트 방침)
/// 벽에 닿으면 사라지고, 플레이어에 닿으면 피해를 준다.
/// </summary>
public partial class Arrow : Area2D
{
    private Vector2 _velocity;
    private float _damage;
    private float _life;
    private Node2D _owner;

    public void Setup(Vector2 direction, float speed, float damage, Node2D shooter)
    {
        _velocity = direction.Normalized() * speed;
        _damage = damage;
        _life = MonsterTuning.GoblinAttack.ArrowLifeSeconds;
        _owner = shooter;
        Rotation = _velocity.Angle();
    }

    public override void _Ready()
    {
        Name = "Arrow";
        CollisionLayer = CollisionLayers.Projectile;
        CollisionMask = CollisionLayers.World | CollisionLayers.Player;
        ZIndex = 5;

        var shape = new CollisionShape2D
        {
            Shape = new CircleShape2D { Radius = MonsterTuning.GoblinAttack.ArrowRadius },
        };
        AddChild(shape);

        BodyEntered += OnBodyEntered;
    }

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
        Position += _velocity * dt;

        _life -= dt;
        if (_life <= 0f)
            QueueFree();
    }

    public override void _Draw()
    {
        float r = MonsterTuning.GoblinAttack.ArrowRadius;
        // 회전 적용 후 좌표계이므로 진행 방향이 +X 다.
        DrawRect(new Rect2(-r * 2f, -r * 0.5f, r * 4f, r), MonsterTuning.GoblinAttack.ArrowColor);
    }

    private void OnBodyEntered(Node2D body)
    {
        if (body == _owner)
            return;

        if (body is IDamageable target && body is PlayerCharacter)
        {
            target.TakeDamage(new DamageInfo
            {
                Amount = _damage,
                Direction = _velocity.Normalized(),
                Heavy = false,
                SkillId = MonsterTuning.GoblinArcher.Id,
                Source = _owner,
            });
        }

        // 벽이든 플레이어든 닿으면 소멸.
        QueueFree();
    }
}
