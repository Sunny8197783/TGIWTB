using Godot;
using PixelMmo.Balance;

namespace PixelMmo.Combat;

/// <summary>
/// 고블린 궁수 — 노란 삼각형. 거리 120px 를 유지하며 쏜다.
/// 제자리 딜링을 불가능하게 만드는 것이 존재 이유. (§D-2)
/// </summary>
public partial class GoblinArcher : MonsterBase
{
    public override MonsterStats Stats => MonsterTuning.GoblinArcher;

    private float _cooldown;

    protected override void UpdateAi(float delta)
    {
        if (_cooldown > 0f)
            _cooldown -= delta;

        if (!PlayerIsAlive)
        {
            Velocity = Vector2.Zero;
            SetState(MonsterState.Idle);
            return;
        }

        switch (State)
        {
            case MonsterState.Windup:
                Velocity = Vector2.Zero;
                if (StateTimer <= 0f)
                    Fire();
                return;

            case MonsterState.Recover:
                Velocity = Vector2.Zero;
                if (StateTimer <= 0f)
                    SetState(MonsterState.Chase);
                return;
        }

        float distance = DistanceToPlayer();
        if (distance > Stats.AggroRange)
        {
            Velocity = Vector2.Zero;
            SetState(MonsterState.Idle);
            return;
        }

        Vector2 toPlayer = DirectionToPlayer();
        if (toPlayer != Vector2.Zero)
            Facing = toPlayer;

        SetState(MonsterState.Chase);
        Velocity = KeepDistanceVelocity(distance, toPlayer);

        if (_cooldown <= 0f)
        {
            _cooldown = MonsterTuning.GoblinAttack.Cooldown;
            SetState(MonsterState.Windup, MonsterTuning.GoblinAttack.Windup);
            Velocity = Vector2.Zero;
        }
    }

    /// <summary>유지 거리보다 가까우면 후퇴, 멀면 접근, 허용 오차 안이면 정지. (§D-2)</summary>
    private Vector2 KeepDistanceVelocity(float distance, Vector2 toPlayer)
    {
        float keep = MonsterTuning.GoblinAttack.KeepDistance;
        float tolerance = MonsterTuning.GoblinAttack.DistanceTolerance;

        if (distance < keep - tolerance)
            return -toPlayer * Stats.MoveSpeed;
        if (distance > keep + tolerance)
            return toPlayer * Stats.MoveSpeed;
        return Vector2.Zero;
    }

    private void Fire()
    {
        SetState(MonsterState.Recover, MonsterTuning.GoblinAttack.Recover);

        if (!PlayerIsAlive)
            return;

        Vector2 direction = DirectionToPlayer();
        if (direction == Vector2.Zero)
            return;

        var arrow = new Arrow { Position = GlobalPosition + direction * (Stats.Radius + 4f) };
        GetParent().AddChild(arrow);
        arrow.Setup(direction, MonsterTuning.GoblinAttack.ArrowSpeed,
            MonsterTuning.GoblinAttack.ArrowDamage, this);
    }

    /// <summary>노란 삼각형. 꼭짓점이 바라보는 방향. (§A 아트 방침)</summary>
    protected override void DrawShape(Color color)
    {
        float r = Stats.Radius;
        float angle = Facing.Angle();

        var points = new[]
        {
            Vector2.Right.Rotated(angle) * r,
            Vector2.Right.Rotated(angle + Mathf.Tau / 3f) * r,
            Vector2.Right.Rotated(angle - Mathf.Tau / 3f) * r,
        };
        DrawColoredPolygon(points, color);

        // 선딜 동안 테두리를 밝혀서 '쏜다'를 예고한다. 예고 없는 원거리는 불공평하다.
        if (State == MonsterState.Windup)
            DrawArc(Vector2.Zero, r + 3f, 0f, Mathf.Tau, 16, new Color(1f, 0.9f, 0.4f, 0.8f), 1f);

        float width = r * 2f;
        float y = r + 3f;
        DrawRect(new Rect2(-r, y, width, 1.5f), new Color(0f, 0f, 0f, 0.5f));
        DrawRect(new Rect2(-r, y, width * HpRatio, 1.5f), new Color(0.9f, 0.35f, 0.35f));
    }

    public override void _PhysicsProcess(double delta)
    {
        base._PhysicsProcess(delta);
        QueueRedraw();
    }
}
