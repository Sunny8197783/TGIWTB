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
        /// <summary>접촉 시 6 데미지.</summary>
        public static readonly float ContactDamage = 6f;

        public static readonly float Cooldown = 1.2f;

        /// <summary>중심 간 거리가 이 값 이하이면 접촉으로 본다.</summary>
        public static readonly float ContactRange = 13f;

        /// <summary>피해를 주는 순간의 짧은 정지.</summary>
        public static readonly float AttackDuration = 0.12f;
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

        /// <summary>선딜 0.5s</summary>
        public static readonly float Windup = 0.5f;

        public static readonly float Recover = 0.35f;
        public static readonly float Cooldown = 1.6f;

        /// <summary>탄속 140 px/s</summary>
        public static readonly float ArrowSpeed = 140f;

        public static readonly float ArrowDamage = 10f;
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
    };

    public static class IronjawAttack
    {
        /// <summary>패턴 1 물어뜯기 — 선딜 0.6s, 전방 부채꼴, 22 데미지.</summary>
        public static readonly float BiteWindup = 0.6f;
        public static readonly float BiteRecover = 0.4f;
        public static readonly float BiteDamage = 22f;
        public static readonly float BiteRange = 34f;
        public static readonly float BiteAngleDeg = 100f;

        /// <summary>패턴 2 돌진 — 선딜 0.8s, 260 px/s 로 1.0s 직진, 28 데미지.</summary>
        public static readonly float ChargeWindup = 0.8f;
        public static readonly float ChargeSpeed = 260f;
        public static readonly float ChargeDuration = 1.0f;
        public static readonly float ChargeDamage = 28f;
        public static readonly float ChargeRecover = 0.6f;

        /// <summary>패턴 3 포효 — HP 40% 이하에서 1회. 이후 공격 속도 25% 증가.</summary>
        public static readonly float RoarHpRatio = 0.4f;
        public static readonly float RoarDuration = 0.9f;
        public static readonly float EnragedSpeedScale = 0.75f;

        /// <summary>패턴 사이 간격.</summary>
        public static readonly float PatternCooldown = 1.4f;

        /// <summary>이 거리 안이면 물어뜯기, 밖이면 돌진.</summary>
        public static readonly float BiteDecisionRange = 46f;

        public static readonly string FlagDefeated = "defeated_ironjaw";
        public static readonly string CounterDeaths = "died_to_ironjaw";
        public static readonly int DeathsForFlag = 7;
        public static readonly string FlagDeathsReached = "died_to_ironjaw_x7";
    }

    // --- 스폰 (§G) ---------------------------------------------------------

    public static readonly int MeadowSlimeCount = 6;
    public static readonly int MeadowGoblinCount = 3;
    public static readonly float MeadowRespawnSeconds = 12f;

    public static readonly int DenIronjawCount = 1;
    public static readonly float DenRespawnSeconds = 45f;
}
