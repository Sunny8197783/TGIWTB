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
    public Vector2[] PatrolRoute { get; set; } = System.Array.Empty<Vector2>();
    private int _patrolIndex;
    private float _patrolTime;

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
            Patrol(delta);
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

    private void Patrol(float delta)
    {
        if (PatrolRoute.Length == 0) { Velocity = Vector2.Zero; SetState(MonsterState.Idle); return; }
        _patrolTime -= delta;
        Vector2 step = PatrolRoute[_patrolIndex] - GlobalPosition;
        if (_patrolTime <= 0f)
        { _patrolIndex = (_patrolIndex + 1) % PatrolRoute.Length; _patrolTime = 5f; }
        if (step.Length() < 5f) { Velocity = Vector2.Zero; SetState(MonsterState.Idle); return; }
        Facing = step.Normalized();
        Velocity = Facing * Stats.MoveSpeed * .35f;
        SetState(MonsterState.Chase);
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

        var arrow = new Arrow();
        arrow.Setup(direction, MonsterTuning.GoblinAttack.ArrowSpeed,
            MonsterTuning.GoblinAttack.ArrowDamage, this);
        arrow.GlobalPosition = GlobalPosition;
        GetParent().AddChild(arrow);
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

        // 외곽선(살짝 큰 삼각형) + 본체 + 좌상단 하이라이트.
        float k = (r + OutlineWidth) / r;
        DrawColoredPolygon(new[] { points[0] * k, points[1] * k, points[2] * k }, OutlineColor);
        DrawColoredPolygon(points, color);
        var hi = new Vector2(-r * 0.18f, -r * 0.22f);
        DrawColoredPolygon(new[] { points[0] * 0.5f + hi, points[1] * 0.5f + hi, points[2] * 0.5f + hi }, Lit(color));

        DrawBow();

        float width = r * 2f;
        float y = r + 3f;
        DrawRect(new Rect2(-r, y, width, 1.5f), new Color(0f, 0f, 0f, 0.5f));
        DrawRect(new Rect2(-r, y, width * HpRatio, 1.5f), new Color(0.9f, 0.35f, 0.35f));
    }

    /// <summary>
    /// 활. 선딜 동안 시위를 뒤로 당기고(예고), 발사 순간 튕겨 나간다.
    /// 활 = 정면 앞의 호, 시위 = 활 양 끝과 화살 오늬를 잇는 선. (규칙 5)
    /// </summary>
    protected override void DrawAttackOverlay() => DrawBow();

    private void DrawBow()
    {
        if (State is not (MonsterState.Windup or MonsterState.Recover))
            return;

        float r = Stats.Radius;
        float angle = Facing.Angle();
        Vector2 forward = Vector2.Right.Rotated(angle);
        Vector2 side = Vector2.Right.Rotated(angle + Mathf.Pi / 2f);

        // 활대는 몸 앞쪽에.
        Vector2 bowCenter = forward * (r + 3f);
        Vector2 top = bowCenter + side * (r + 2f);
        Vector2 bottom = bowCenter - side * (r + 2f);

        var wood = new Color(0.55f, 0.38f, 0.2f);
        DrawLine(top, bowCenter + forward * 3f, wood, 1.5f);
        DrawLine(bottom, bowCenter + forward * 3f, wood, 1.5f);

        // 시위 당김: 선딜이 진행될수록 뒤로. 발사(후딜) 순간엔 앞으로 튕긴다.
        float pull = State == MonsterState.Windup
            ? Mathf.Lerp(0f, r + 4f, StateProgress)
            : Mathf.Lerp(r + 4f, -2f, Mathf.Min(1f, StateProgress * 3f));

        Vector2 nock = bowCenter - forward * pull;
        var stringColor = new Color(0.9f, 0.9f, 0.85f);
        DrawLine(top, nock, stringColor, 1f);
        DrawLine(bottom, nock, stringColor, 1f);

        // 메긴 화살은 선딜에만. 시위 위치에서 앞으로.
        if (State == MonsterState.Windup)
            DrawLine(nock, nock + forward * (r + 8f), new Color(0.95f, 0.9f, 0.6f), 1.5f);
    }

}
