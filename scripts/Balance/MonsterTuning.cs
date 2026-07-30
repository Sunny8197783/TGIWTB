using Godot;

namespace PixelMmo.Balance;

/// <summary>몬스터 1종의 공통 수치.</summary>
public sealed class MonsterStats
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public float MaxHp { get; init; }
    public float MoveSpeed { get; init; }
    public int Level { get; init; } = 1;

    /// <summary>도형 반지름(px). 히트 판정과 그리기에 같이 쓴다.</summary>
    public float Radius { get; init; }

    public Color Color { get; init; }

    /// <summary>이 거리 안에 플레이어가 들어오면 추적을 시작한다.</summary>
    public float AggroRange { get; init; }

    /// <summary>
    /// 처치 시 마지막 일격 스킬에 주는 숙련 보너스. 0 이면 없음.
    /// 보스처럼 잡기 어려운 대상이 큰 성장으로 이어지게 하는 값. (§F)
    /// </summary>
    public float MasteryKillBonus { get; init; }
}

/// <summary>§D-2 몬스터 3종 수치. 여기 없는 몬스터 상수는 코드에 있으면 안 된다.</summary>
public static class MonsterTuning
{
    // --- 1) 슬라임 — 학습용 -----------------------------------------------

    public static readonly MonsterStats Slime = new()
    {
        Id = "slime",
        Name = "슬라임",
        MaxHp = 30f,
        MoveSpeed = 40f,
        Level = 1,
        Radius = 7f,
        Color = new Color(0.35f, 0.78f, 0.42f),
        AggroRange = 140f,
    };

    public static class SlimeAttack
    {
        /// <summary>몽둥이 휘두르기 데미지.</summary>
        public static readonly float ContactDamage = 6f;

        public static readonly float Cooldown = 1.4f;

        /// <summary>이 거리 안이면 공격을 시작한다. 몸+몽둥이 사거리.</summary>
        public static readonly float ContactRange = 20f;

        /// <summary>몽둥이 자루 끝 ~ 뭉툭한 끝까지. 그리기와 판정이 같은 값을 쓴다.</summary>
        public static readonly float ClubInner = 2.8f;   // Radius * 0.4
        public static readonly float ClubOuter = 19f;    // Radius + 12
        public static readonly float ClubWidth = 3f;

        /// <summary>스윙 각도 — 뒤로 감아올린 각(rad)에서 앞으로 내려친 각까지.</summary>
        public static readonly float ClubSwingFrom = -0.9f;
        public static readonly float ClubSwingTo = 1.0f;

        /// <summary>
        /// 선딜 0.3s — 몽둥이를 치켜드는 예고. 이 동안 피하거나 (퍼펙트) 가드할 수 있다.
        /// 예고 없이 닿자마자 때리면 근접 몹에게 붙는 순간 무조건 맞는 문제가 생긴다.
        /// 0.45s 는 피하기엔 너무 굼떠 보여 0.3s 로 줄였다.
        /// </summary>
        public static readonly float Windup = 0.3f;

        /// <summary>내려치는 순간. 이 시점에 사거리 안이면 피해가 들어간다.</summary>
        public static readonly float Strike = 0.1f;

        public static readonly float Recover = 0.35f;
    }

    // --- 2) 고블린 궁수 — 위치 이동 강제 -----------------------------------

    public static readonly MonsterStats GoblinArcher = new()
    {
        Id = "goblin_archer",
        Name = "고블린 궁수",
        MaxHp = 24f,
        MoveSpeed = 60f,
        Level = 2,
        Radius = 7f,
        Color = new Color(0.88f, 0.80f, 0.28f),
        AggroRange = 220f,
    };

    public static class GoblinAttack
    {
        /// <summary>플레이어와 이 거리를 유지한다. 가까우면 후퇴.</summary>
        public static readonly float KeepDistance = 120f;

        /// <summary>유지 거리 앞뒤로 이만큼은 그냥 서 있는다 (덜덜 떨림 방지).</summary>
        public static readonly float DistanceTolerance = 16f;

        /// <summary>
        /// 선딜 0.45s. 0.7s 는 활 당기는 게 굼떠 보여 줄였다.
        /// (아래 쿨다운이 넉넉해서 선딜이 짧아도 압박이 과하지 않다.)
        /// </summary>
        public static readonly float Windup = 0.45f;

        public static readonly float Recover = 0.35f;

        /// <summary>
        /// 2.8s. 명세 1.6s 로는 초원에 궁수가 3마리라 화살이 끊이지 않아
        /// 한 대도 안 맞고 접근하는 것이 사실상 불가능했다.
        /// </summary>
        public static readonly float Cooldown = 2.8f;

        /// <summary>탄속 140 px/s</summary>
        public static readonly float ArrowSpeed = 140f;

        /// <summary>8. 명세 10 에서 낮췄다 — 위 쿨다운과 함께 원거리 압박을 줄인다.</summary>
        public static readonly float ArrowDamage = 8f;
        public static readonly float ArrowLifeSeconds = 3f;
        public static readonly float ArrowRadius = 2.5f;
        public static readonly Color ArrowColor = new(0.95f, 0.90f, 0.60f);
    }

    // --- 3) 철턱(Ironjaw) — 미니보스 ---------------------------------------

    public static readonly MonsterStats Ironjaw = new()
    {
        Id = "ironjaw",
        Name = "철턱",
        MaxHp = 220f,
        MoveSpeed = 55f,
        Level = 5,
        Radius = 16f,
        Color = new Color(0.82f, 0.24f, 0.22f),
        AggroRange = 260f,
        // 보스 처치 = 마지막 일격 스킬 숙련 대폭 상승 (진화 임계값 300의 40%).
        MasteryKillBonus = 120f,
    };

    public static class IronjawAttack
    {
        /// <summary>패턴 1 물어뜯기 — 선딜 0.6s, 전방 부채꼴.</summary>
        public static readonly float BiteWindup = 0.6f;

        /// <summary>0.8s. 패턴이 끝난 뒤 반격할 틈을 주는 구간이라 명세 0.4s 에서 늘렸다.</summary>
        public static readonly float BiteRecover = 0.8f;

        /// <summary>18. 명세 22 에서 낮췄다.</summary>
        public static readonly float BiteDamage = 18f;
        public static readonly float BiteRange = 34f;
        public static readonly float BiteAngleDeg = 100f;

        /// <summary>패턴 2 돌진 — 선딜 0.8s, 260 px/s 로 1.0s 직진.</summary>
        public static readonly float ChargeWindup = 0.8f;
        public static readonly float ChargeSpeed = 260f;
        public static readonly float ChargeDuration = 1.0f;

        /// <summary>22. 명세 28 에서 낮췄다.</summary>
        public static readonly float ChargeDamage = 22f;

        /// <summary>1.1s. 돌진 후 크게 숨을 고른다 — 여기가 주요 공격 기회다.</summary>
        public static readonly float ChargeRecover = 1.1f;

        /// <summary>패턴 3 포효 — HP 40% 이하에서 1회. 이후 공격 속도 25% 증가.</summary>
        public static readonly float RoarHpRatio = 0.4f;
        public static readonly float RoarDuration = 0.9f;
        public static readonly float EnragedSpeedScale = 0.75f;

        /// <summary>
        /// 패턴 사이 간격 2.6s. 명세 1.4s 로는 (선딜 0.6 + 후딜 0.4) 가 끝나면
        /// 0.4s 만에 다음 패턴이 나와서 때릴 틈이 사실상 없었다.
        /// 물어뜯기 기준 후딜이 끝나고도 1.2s 가 비어 3~4대는 넣을 수 있다.
        /// </summary>
        public static readonly float PatternCooldown = 2.6f;

        /// <summary>이 거리 안이면 물어뜯기, 밖이면 돌진.</summary>
        public static readonly float BiteDecisionRange = 46f;

        public static readonly string FlagDefeated = "defeated_ironjaw";
        public static readonly string CounterDeaths = "died_to_ironjaw";
        public static readonly int DeathsForFlag = 7;
        public static readonly string FlagDeathsReached = "died_to_ironjaw_x7";
    }

    // --- 허수아비 — 마을 훈련용 -------------------------------------------

    public static readonly MonsterStats TrainingDummy = new()
    {
        Id = "training_dummy",
        Name = "허수아비",
        MaxHp = 1f,          // 불사(Immortal)라 실제로는 쓰이지 않는다.
        MoveSpeed = 0f,
        Level = 1,
        Radius = 8f,
        Color = new Color(0.78f, 0.66f, 0.42f),   // 볏짚 색
        AggroRange = 0f,
    };

    // --- 스폰 (§G) ---------------------------------------------------------

    public static readonly int MeadowSlimeCount = 6;
    public static readonly int MeadowGoblinCount = 3;
    public static readonly float MeadowRespawnSeconds = 12f;

    public static readonly int DenIronjawCount = 1;
    public static readonly float DenRespawnSeconds = 45f;
}
