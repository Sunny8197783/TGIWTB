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

    /// <summary>지난 프레임의 몽둥이 각도. 프레임 사이를 훑는 띠를 만드는 데 쓴다.</summary>
    private float _prevClubAngle;

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
            {
                SetState(MonsterState.Attack, MonsterTuning.SlimeAttack.Strike);
                _prevClubAngle = MonsterTuning.SlimeAttack.ClubSwingFrom;
            }
            return;
        }

        // 내려치는 중 — 몽둥이가 실제로 훑고 지나간 자리에만 피해가 들어간다.
        if (State == MonsterState.Attack)
        {
            Velocity = Vector2.Zero;
            SweepClub();
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

    /// <summary>
    /// 몽둥이가 이번 프레임에 훑은 띠로 판정한다. 보이는 궤적이 곧 히트박스라
    /// 헛스윙은 확실히 헛스윙이 된다. 한 스윙에 한 번만 맞는다.
    /// </summary>
    private void SweepClub()
    {
        if (_struckThisSwing || !PlayerIsAlive)
            return;

        float current = ClubSwingOffset();
        bool hit = Hitbox.SweptArc(GlobalPosition, Facing.Angle(), _prevClubAngle, current,
            MonsterTuning.SlimeAttack.ClubInner, MonsterTuning.SlimeAttack.ClubOuter,
            MonsterTuning.SlimeAttack.ClubWidth,
            Player.GlobalPosition, PlayerTuning.BodySize * 0.5f);

        _prevClubAngle = current;

        if (!hit)
            return;

        _struckThisSwing = true;
        StrikePlayer(MonsterTuning.SlimeAttack.ContactDamage, Facing);
    }

    /// <summary>몽둥이의 현재 각도(바라보는 방향 기준). 그리기와 판정이 공유한다.</summary>
    private float ClubSwingOffset()
    {
        float from = MonsterTuning.SlimeAttack.ClubSwingFrom;
        float to = MonsterTuning.SlimeAttack.ClubSwingTo;

        return State switch
        {
            MonsterState.Windup => from * StateProgress,
            MonsterState.Attack => Mathf.Lerp(from, to, StateProgress),
            MonsterState.Recover => Mathf.Lerp(to, 0f, StateProgress),
            _ => 0f,
        };
    }

    private void Decelerate(float delta)
        => Velocity = Velocity.MoveToward(Vector2.Zero, Stats.MoveSpeed * delta * 4f);

    /// <summary>초록 원 + 명암 + 휘두르는 몽둥이. (§A 아트 방침)</summary>
    protected override void DrawShape(Color color)
    {
        float r = Stats.Radius;
        DrawCircle(Vector2.Zero, r + OutlineWidth, OutlineColor);   // 외곽선
        DrawCircle(Vector2.Zero, r, color);                         // 본체
        // 아래쪽 그늘(어두운 반달) + 좌상단 하이라이트.
        DrawArc(Vector2.Zero, r * 0.62f, 0.25f, Mathf.Pi - 0.25f, 12, Dark(color), r * 0.55f);
        DrawCircle(new Vector2(-r * 0.32f, -r * 0.32f), r * 0.42f, Lit(color));
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
        float swing = ClubSwingOffset();

        float inner = MonsterTuning.SlimeAttack.ClubInner;
        float outer = MonsterTuning.SlimeAttack.ClubOuter;

        Color shaft = State == MonsterState.Attack
            ? new Color(1f, 0.85f, 0.4f)   // 내려칠 때 밝게
            : new Color(0.5f, 0.35f, 0.2f);

        // 내려치는 중이면 지금까지 훑은 띠를 깐다 — 이 띠가 그대로 히트박스다.
        if (State == MonsterState.Attack)
        {
            float from = MonsterTuning.SlimeAttack.ClubSwingFrom;
            DrawArc(Vector2.Zero, (inner + outer) * 0.5f,
                baseAngle + Mathf.Min(from, swing), baseAngle + Mathf.Max(from, swing), 10,
                new Color(shaft.R, shaft.G, shaft.B, 0.25f), outer - inner);
        }

        Vector2 dir = Vector2.Right.Rotated(baseAngle + swing);
        DrawLine(dir * inner, dir * outer, shaft, MonsterTuning.SlimeAttack.ClubWidth);
        DrawCircle(dir * outer, 3f, shaft);   // 뭉툭한 끝
    }
}
