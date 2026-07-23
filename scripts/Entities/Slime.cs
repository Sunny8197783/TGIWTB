using Godot;
using PixelMmo.Balance;

namespace PixelMmo.Combat;

/// <summary>
/// 슬라임 — 초록 원. 느리고 예측 가능. 플레이어가 조작을 익히는 대상. (§D-2)
/// </summary>
public partial class Slime : MonsterBase
{
    public override MonsterStats Stats => MonsterTuning.Slime;

    private float _attackCooldown;

    protected override void UpdateAi(float delta)
    {
        if (_attackCooldown > 0f)
            _attackCooldown -= delta;

        if (State == MonsterState.Attack)
        {
            Velocity = Vector2.Zero;
            if (StateTimer <= 0f)
                SetState(MonsterState.Chase);
            return;
        }

        if (!PlayerIsAlive)
        {
            Decelerate(delta);
            SetState(MonsterState.Idle);
            return;
        }

        float distance = DistanceToPlayer();

        if (distance > Stats.AggroRange)
        {
            Decelerate(delta);
            SetState(MonsterState.Idle);
            return;
        }

        SetState(MonsterState.Chase);
        Vector2 direction = DirectionToPlayer();
        if (direction != Vector2.Zero)
            Facing = direction;

        // 접촉 공격 — 선딜 없이 닿으면 들어간다. 예측 가능해야 학습용이 된다.
        if (distance <= MonsterTuning.SlimeAttack.ContactRange && _attackCooldown <= 0f)
        {
            _attackCooldown = MonsterTuning.SlimeAttack.Cooldown;
            SetState(MonsterState.Attack, MonsterTuning.SlimeAttack.AttackDuration);
            Velocity = Vector2.Zero;
            StrikePlayer(MonsterTuning.SlimeAttack.ContactDamage, direction);
            return;
        }

        Velocity = direction * Stats.MoveSpeed;
    }

    private void Decelerate(float delta)
        => Velocity = Velocity.MoveToward(Vector2.Zero, Stats.MoveSpeed * delta * 4f);

    /// <summary>초록 원. (§A 아트 방침)</summary>
    protected override void DrawShape(Color color)
    {
        DrawCircle(Vector2.Zero, Stats.Radius, color);

        // 남은 체력을 원 아래 짧은 막대로. UI 없이도 진행이 보이게.
        float width = Stats.Radius * 2f;
        float y = Stats.Radius + 3f;
        DrawRect(new Rect2(-Stats.Radius, y, width, 1.5f), new Color(0f, 0f, 0f, 0.5f));
        DrawRect(new Rect2(-Stats.Radius, y, width * HpRatio, 1.5f), new Color(0.9f, 0.35f, 0.35f));
    }
}
