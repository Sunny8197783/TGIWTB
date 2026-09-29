namespace PixelMmo.Combat;

/// <summary>
/// 손맛 수치는 전부 여기. 시간은 초, 괄호 안은 60fps 프레임 수.
/// 근거는 docs/design/REBUILD.md "전투 손맛 조사 요약".
/// </summary>
public static class CombatTuning
{
    // ── 이동 ──────────────────────────────────────────────
    /// <summary>달리기 속도 (m/s). 캐릭터 키 1.9m 기준 경쾌한 뜀박질.</summary>
    public const float RunSpeed = 5.2f;
    /// <summary>정지→최고속 시간 (4f). 짧을수록 손에 붙는다. 0 이면 로봇 같고 0.2 넘으면 미끄럽다.</summary>
    public const float AccelTime = 0.07f;
    /// <summary>최고속→정지 시간 (3f).</summary>
    public const float DecelTime = 0.05f;
    /// <summary>달리기 한 주기(8프레임) 동안 나아가는 거리 (m). 발이 땅에서 미끄러지지 않게 애니 속도를 여기에 맞춘다.</summary>
    public const float StrideLength = 2.3f;
    /// <summary>발이 땅에 닿는 달리기 프레임 (발소리·먼지). 8프레임 주기 기준.</summary>
    public static readonly int[] RunStepFrames = { 1, 5 };
    /// <summary>오를·내릴 수 있는 가장 가파른 기울기 (높이/거리). 1.2 ≈ 50°. 절벽은 막힌다.</summary>
    public const float MaxSlope = 1.2f;
    /// <summary>이 거리보다 먼 몬스터는 잔다 (m). 화면 밖 AI·물리를 돌리지 않는다.</summary>
    public const float MonsterSleepRange = 40f;
    /// <summary>얕은 물은 걷고, 이보다 깊으면 못 들어간다 (m).</summary>
    public const float WadeDepth = 0.45f;

    // ── 회피(돌진) ────────────────────────────────────────
    /// <summary>굳어 있는 동안 누른 입력을 기억하는 시간 (7f). 이게 없으면 "눌렀는데 안 나갔다"가 된다.</summary>
    public const float InputBuffer = 0.12f;
    /// <summary>돌진 속도 (m/s) × 시간 = 약 3m.</summary>
    public const float DashSpeed = 14f;
    /// <summary>돌진 시간 (13f).</summary>
    public const float DashTime = 0.22f;
    /// <summary>돌진 뒤 굳는 시간 (5f). 연타 방지 겸 착지감.</summary>
    public const float DashRecovery = 0.08f;
    /// <summary>다음 돌진까지 (21f).</summary>
    public const float DashCooldown = 0.35f;
    /// <summary>잔상 간격 (2f).</summary>
    public const float GhostInterval = 0.035f;
    /// <summary>잔상이 사라지는 시간 (15f).</summary>
    public const float GhostLife = 0.25f;

    // ── 카메라 ───────────────────────────────────────────
    /// <summary>달리는 쪽을 조금 더 보여 준다 (m).</summary>
    public const float LookAhead = 1.4f;
    /// <summary>앞보기가 따라붙는 빠르기 (1/s).</summary>
    public const float LookAheadRate = 2.5f;

    // ── 연출 ─────────────────────────────────────────────
    /// <summary>대기 숨쉬기 속도 (fps).</summary>
    public const float IdleFps = 5f;
    /// <summary>입력이 대각선 경계에서 떨릴 때 방향이 깜빡이지 않게 (도).</summary>
    public const float FacingHysteresisDeg = 8f;

    // ── 공격 (3연타) ─────────────────────────────────────
    // 각 타: 전체 길이 / 판정 순간 / 다음 타·회피로 넘어갈 수 있는 시점 / 앞으로 내딛는 거리 / 피해 / 사거리 / 부채꼴 반각 / 강타
    //        / 그림 시작 칸 / 칼이 닿는 칸 — 원화는 준비 동작이 길다. 준비 칸은 빨리 넘기고 닿는 칸이 판정 순간에 오게 재생을 휜다.
    public static readonly AttackStep[] Combo =
    {
        new("attack1", 0.36f, 0.14f, 0.20f, 0.9f, 10f, 1.9f, 75f, false, 2, 6), // 22f, 판정 8f
        new("attack2", 0.36f, 0.13f, 0.20f, 1.0f, 11f, 1.9f, 75f, false, 2, 5), // 22f, 판정 8f
        new("attack3", 0.62f, 0.36f, 0.46f, 1.3f, 24f, 2.3f, 55f, true, 1, 7),  // 37f, 판정 22f — 내려찍기
    };
    /// <summary>공격 중 다음 타 입력을 받기 시작하는 진행도. 이보다 이르면 연타가 씹히지 않고 버퍼에 남는다.</summary>
    public const float ComboBufferFrom = 0.35f;
    /// <summary>달리다 공격하면 달리던 속도를 이만큼 이어받는다 (질주 공격 — 멈추면 답답하다).</summary>
    public const float RunCarry = 0.6f;
    /// <summary>회피 중 공격 → 반격. 피해 배율.</summary>
    public const float CounterDamage = 1.5f;
    /// <summary>슬로우 중 피해 배율.</summary>
    public const float WitchDamage = 1.3f;

    // ── 막기 / 패링 ─────────────────────────────────────
    /// <summary>막기 누른 직후 이 시간 안에 맞으면 패링 (9f). 세키로 계열의 짧은 창.</summary>
    public const float ParryWindow = 0.15f;
    /// <summary>막는 중 이동 속도 배율.</summary>
    public const float GuardMoveScale = 0.35f;
    /// <summary>막을 수 있는 정면 반각 (도). 8방향 몸 돌리기 오차(±22.5°)를 넉넉히 덮는다.</summary>
    public const float GuardArcDeg = 100f;
    /// <summary>막기를 누르면 이 거리 안의 가장 가까운 적 쪽으로 돈다 (m).</summary>
    public const float GuardTurnRange = 5f;
    /// <summary>막았을 때 밀려나는 거리 (m/s 충격).</summary>
    public const float BlockPush = 4f;
    /// <summary>패링당한 적이 무방비로 굳는 시간 (84f). 이 동안 받는 피해 ×2.</summary>
    public const float StaggerTime = 1.4f;
    public const float StaggerDamage = 2f;

    // ── 회피 판정 ───────────────────────────────────────
    /// <summary>돌진 시작부터 무적 (12f).</summary>
    public const float DashIFrames = 0.2f;
    /// <summary>돌진 시작 이 시간 안에 공격이 닿을 뻔하면 완벽 회피 (9f).</summary>
    public const float PerfectDodgeWindow = 0.15f;
    /// <summary>완벽 회피 판정 사거리 여유 — 스치듯 피해도 인정.</summary>
    public const float PerfectDodgeReach = 1.35f;
    /// <summary>완벽 회피: 적·세계 시간 배율 (Witch Time). 명조 ~1초, 베요네타 ~3초의 중간.</summary>
    public const float WitchScale = 0.2f;
    public const float WitchTime = 2.4f;
    /// <summary>완벽 회피 잔상은 더 오래 남는다.</summary>
    public const float WitchGhostLife = 0.6f;

    // ── 피격 ────────────────────────────────────────────
    public const float HeroMaxHp = 100f;
    /// <summary>맞고 굳는 시간 (18f).</summary>
    public const float HurtTime = 0.3f;
    public const float HurtPush = 6f;
    /// <summary>맞은 뒤 무적 (48f) — 연속으로 두들겨 맞는 억울함 방지.</summary>
    public const float HurtIFrames = 0.8f;

    // ── 타격감 (히트스톱 초 / 킥 m/s / 흔들림 충격량) ─────────
    public static readonly Impact HitNormal = new(0.05f, 1.6f, 0.18f);   // 3f
    public static readonly Impact HitHeavy = new(0.095f, 3.2f, 0.42f);   // 6f
    public static readonly Impact HitCounter = new(0.075f, 2.4f, 0.3f);  // 5f
    public static readonly Impact Blocked = new(0.04f, 1.0f, 0.12f);     // 2f
    public static readonly Impact Parried = new(0.13f, 2.2f, 0.36f);     // 8f
    public static readonly Impact PerfectDodge = new(0.07f, 0f, 0.1f);   // 4f 멈칫 → 슬로우
    public static readonly Impact HeroHurt = new(0.08f, 2.6f, 0.5f);     // 5f
}

public readonly record struct AttackStep(string Anim, float Duration, float HitAt, float CancelAt, float Lunge,
    float Damage, float Reach, float HalfArcDeg, bool Heavy, int StartFrame, int HitFrame);

public readonly record struct Impact(float HitStop, float Kick, float Trauma);
