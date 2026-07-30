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

    /// <summary>이번 휘두르기에서 이미 피해를 넣었는가. 한 스윙에 한 번만.</summary>
    private bool _struckThisSwing;

    protected override void UpdateAi(float delta)
    {
        if (_attackCooldown > 0f)
            _attackCooldown -= delta;

        // 몽둥이를 치켜드는 중 — 멈춰서 예고한다. 여기서 플레이어가 피하거나 막는다.
        if (State == MonsterState.Windup)
        {
            Velocity = Vector2.Zero;
            if (PlayerIsAlive)
                Facing = DirectionToPlayer();  // 선딜엔 조준을 따라온다
            if (StateTimer <= 0f)
                SetState(MonsterState.Attack, MonsterTuning.SlimeAttack.Strike);
            return;
        }

        // 내려치는 순간 — 사거리 안이면 이번 스윙에 한 번 피해.
        if (State == MonsterState.Attack)
        {
            Velocity = Vector2.Zero;
            if (!_struckThisSwing)
            {
                _struckThisSwing = true;
                if (PlayerIsAlive && DistanceToPlayer() <= MonsterTuning.SlimeAttack.ContactRange)
                    StrikePlayer(MonsterTuning.SlimeAttack.ContactDamage, Facing);
            }
            if (StateTimer <= 0f)
                SetState(MonsterState.Recover, MonsterTuning.SlimeAttack.Recover);
            return;
        }

        if (State == MonsterState.Recover)
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

        // 사거리 안 + 쿨다운 끝 → 몽둥이 치켜들기 시작. (선딜 → 내려치기 → 후딜)
        if (distance <= MonsterTuning.SlimeAttack.ContactRange && _attackCooldown <= 0f)
        {
            _attackCooldown = MonsterTuning.SlimeAttack.Cooldown;
            _struckThisSwing = false;
            SetState(MonsterState.Windup, MonsterTuning.SlimeAttack.Windup);
            Velocity = Vector2.Zero;
            return;
        }

        Velocity = direction * Stats.MoveSpeed;
    }

    private void Decelerate(float delta)
        => Velocity = Velocity.MoveToward(Vector2.Zero, Stats.MoveSpeed * delta * 4f);

    /// <summary>초록 원 + 휘두르는 몽둥이. (§A 아트 방침)</summary>
    protected override void DrawShape(Color color)
    {
        DrawCircle(Vector2.Zero, Stats.Radius, color);
        DrawClub();

        // 남은 체력을 원 아래 짧은 막대로. UI 없이도 진행이 보이게.
        float width = Stats.Radius * 2f;
        float y = Stats.Radius + 3f;
        DrawRect(new Rect2(-Stats.Radius, y, width, 1.5f), new Color(0f, 0f, 0f, 0.5f));
        DrawRect(new Rect2(-Stats.Radius, y, width * HpRatio, 1.5f), new Color(0.9f, 0.35f, 0.35f));
    }

    /// <summary>
    /// 몽둥이. 선딜엔 뒤로 치켜들고(예고), 내려치기엔 앞으로 휘두른다.
    /// 도형(선분)으로만 그린다. (규칙 5)
    /// </summary>
    private void DrawClub()
    {
        if (State is not (MonsterState.Windup or MonsterState.Attack or MonsterState.Recover))
            return;

        float baseAngle = Facing.Angle();

        // -1(뒤로 치켜듦) → +1(앞으로 내려침) 로 스윙 진행.
        float swing = State switch
        {
            MonsterState.Windup => -0.9f * StateProgress,          // 뒤로 감아올림
            MonsterState.Attack => Mathf.Lerp(-0.9f, 1.0f, StateProgress),  // 빠르게 내려침
            _ => Mathf.Lerp(1.0f, 0f, StateProgress),               // 후딜에 복귀
        };

        float angle = baseAngle + swing;
        Vector2 dir = Vector2.Right.Rotated(angle);
        Vector2 start = dir * (Stats.Radius * 0.4f);
        Vector2 end = dir * (Stats.Radius + 12f);

        Color shaft = State == MonsterState.Attack
            ? new Color(1f, 0.85f, 0.4f)   // 내려칠 때 밝게
            : new Color(0.5f, 0.35f, 0.2f);
        DrawLine(start, end, shaft, 2.5f);
        DrawCircle(end, 3f, shaft);        // 뭉툭한 끝
    }
}
