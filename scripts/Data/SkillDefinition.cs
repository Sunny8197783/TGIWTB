using System.Collections.Generic;

namespace PixelMmo.Data;

/// <summary>
/// data/skills/*.json 1:1 매핑. 스킬 수치는 전부 여기서 온다. (CLAUDE.md 규칙 1)
/// </summary>
public sealed class SkillDefinition
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";

    /// <summary>히든 스킬. 습득 조건을 UI 에 절대 노출하지 않는다. (규칙 4)</summary>
    public bool Hidden { get; set; }

    /// <summary>"active" | "passive" | "hold"</summary>
    public string Kind { get; set; } = "active";

    /// <summary>InputSetup 이 등록한 액션 이름. 빈 문자열이면 키 입력으로 발동하지 않는다.</summary>
    public string InputAction { get; set; } = "";

    public float Cooldown { get; set; }

    /// <summary>
    /// 소모 마나. 0 이면 마나를 쓰지 않는다(전사의 기본 베기 등).
    ///
    /// 쿨다운만으로는 직업 색이 나지 않는다 — 마법사는 쓸 수 있는 횟수 자체가
    /// 자원이어야 하고, 전사는 그 자원이 없어야 한다. 그래서 쿨다운과 마나를
    /// 둘 다 두고 직업마다 어느 쪽에 기대는지를 데이터로 정한다.
    /// </summary>
    public float ManaCost { get; set; }

    /// <summary>강타격 판정. 히트스톱/넉백/화면흔들림이 강화된다. (§C-3, §C-4, §C-6)</summary>
    public bool Heavy { get; set; }

    /// <summary>어느 직업의 스킬인가. 같은 키에 여러 직업 스킬이 물릴 때 현재 직업 것을 먼저 고른다.</summary>
    public string Job { get; set; } = "";

    public SkillShape Shape { get; set; } = new();
    public SkillDamage Damage { get; set; } = new();
    public SkillMastery Mastery { get; set; } = new();

    public SkillBuff Buff { get; set; }
    public SkillGuard Guard { get; set; }
    public SkillDash Dash { get; set; }
    public SkillStun Stun { get; set; }
    public SkillPassive Passive { get; set; }
    public SkillLearnTrigger LearnTrigger { get; set; }
    public SkillHeal Heal { get; set; }
    public SkillProjectile Projectile { get; set; }
    public SkillEffect Effect { get; set; }

    public bool IsActive => Kind == "active" || Kind == "hold";
}

public sealed class SkillShape
{
    /// <summary>"cone" | "circle" | "self"</summary>
    public string Kind { get; set; } = "cone";
    public float AngleDeg { get; set; }
    public float RangePx { get; set; }
}

public sealed class SkillDamage
{
    public float Base { get; set; }

    /// <summary>스탯 이름 → 계수. 최종 = Base + Σ(stat * 계수)</summary>
    public Dictionary<string, float> Scaling { get; set; } = new();
}

public sealed class SkillMastery
{
    /// <summary>이 간격보다 빠른 재사용은 숙련이 오르지 않는다 (매크로 방어). (§F)</summary>
    public float MinInterval { get; set; } = 0.2f;

    /// <summary>숙련 100당 데미지 배수 증가분. 배수 = 1 + mastery/100 * 이 값.</summary>
    public float DamageBonusPer100 { get; set; }

    public SkillEvolution Evolution { get; set; }
}

public sealed class SkillEvolution
{
    public float At { get; set; }
    public string Into { get; set; } = "";

    /// <summary>이 플래그가 전부 있어야 진화가 성립한다. 비어 있으면 숙련만으로 진화. (§F)</summary>
    public List<string> RequiredFlags { get; set; } = new();

    /// <summary>진화 시 화면 중앙에 뜨는 한 줄. 조건은 설명하지 않는다. (§E)</summary>
    public string Announce { get; set; } = "";
}

public sealed class SkillBuff
{
    public float DurationSeconds { get; set; }
    public float AttackPowerMultiplier { get; set; } = 1f;
    public float DamageTakenMultiplier { get; set; } = 1f;
}

public sealed class SkillGuard
{
    public float DamageMultiplier { get; set; } = 1f;
    public float MoveSpeedMultiplier { get; set; } = 1f;

    /// <summary>가드 시작 후 이 시간 안에 맞으면 퍼펙트 가드. (§E sk_guard)</summary>
    public float PerfectWindow { get; set; }
    public float RiposteSeconds { get; set; }

    /// <summary>퍼펙트 가드 카운터 — 받을 뻔한 피해의 이 배율만큼 돌려준다.</summary>
    public float CounterMultiplier { get; set; }

    /// <summary>카운터가 닿는 거리(px). 근접 공격만 되받아친다. 화살은 밖이라 반사되지 않는다.</summary>
    public float CounterRange { get; set; }

    /// <summary>카운터에 맞은 상대가 기절하는 시간(초).</summary>
    public float CounterStunSeconds { get; set; }

    /// <summary>
    /// 가드를 뗀 뒤의 쿨다운(초). 이 동안은 다시 가드를 올릴 수 없다.
    /// 단, 퍼펙트 가드에 성공하면 쿨다운이 초기화된다 — K 남발로 퍼펙트를
    /// 얻는 것을 막고, 정확히 막았을 때만 계속 막게 하려는 것. (§E)
    /// </summary>
    public float CooldownSeconds { get; set; }
}

public sealed class SkillDash
{
    public float DistancePx { get; set; }
}

public sealed class SkillStun
{
    public float RadiusPx { get; set; }
    public float DurationSeconds { get; set; }
}

public sealed class SkillPassive
{
    /// <summary>0 보다 크면 대시 무적 시간을 이 값으로 덮어쓴다. (§E sk_hidden_deathline)</summary>
    public float DashInvulnSeconds { get; set; }
}

/// <summary>
/// 회복. 힐러 계열의 뼈대다.
///
/// 회복량 공식은 데미지와 같은 모양(고정값 + 스탯 계수)이되, 최대 체력 비율을
/// 하나 더 둔다 — 고정값만으로는 레벨이 오를수록 회복이 무의미해지고,
/// 비율만으로는 초반에 아무 것도 안 되기 때문이다.
/// </summary>
public sealed class SkillHeal
{
    public float Base { get; set; }

    /// <summary>스탯 이름 → 계수. 회복량 += stat * 계수.</summary>
    public Dictionary<string, float> Scaling { get; set; } = new();

    /// <summary>최대 체력의 이 비율만큼 추가 회복.</summary>
    public float MaxHpRatio { get; set; }

    /// <summary>지속 회복이면 이 시간(초) 동안 나눠서 들어간다. 0 이면 즉시.</summary>
    public float OverSeconds { get; set; }
}

/// <summary>
/// 날아가는 것. 궁수의 화살과 마법사의 탄이 같은 구조다 —
/// 다른 것은 속도·개수·퍼짐·색뿐이라 굳이 둘로 나눌 이유가 없었다.
/// </summary>
public sealed class SkillProjectile
{
    public float SpeedPx { get; set; } = 320f;

    /// <summary>한 번에 몇 발. 2 이상이면 SpreadDeg 안에 부채꼴로 편다.</summary>
    public int Count { get; set; } = 1;

    public float SpreadDeg { get; set; }
    public float LifeSeconds { get; set; } = 1.4f;
    public float RadiusPx { get; set; } = 4f;

    /// <summary>맞고도 계속 날아가는가. 관통 화살·꿰뚫는 얼음창.</summary>
    public bool Pierce { get; set; }

    /// <summary>"ffcc66" 같은 16진 색. 비우면 흰색.</summary>
    public string Color { get; set; } = "";
}

/// <summary>
/// 발동 순간의 연출. 어떤 도형을 어느 색으로 띄울지만 정한다 —
/// 스무 개가 넘는 스킬에 이펙트를 코드로 하나씩 붙이면 그 자체가 유지보수 짐이 된다.
/// </summary>
public sealed class SkillEffect
{
    /// <summary>"arc" | "cone" | "ring" | "wind" | "" (없음)</summary>
    public string Vfx { get; set; } = "";

    /// <summary>"66ccff" 같은 16진 색. 비우면 흰색.</summary>
    public string Color { get; set; } = "";

    /// <summary>ring 의 반지름(px). 나머지 도형은 무시한다.</summary>
    public float RadiusPx { get; set; } = 48f;
}

/// <summary>히든 스킬의 습득 트리거. type = "situational_repeat" 만 P1 에서 처리한다. (§F)</summary>
public sealed class SkillLearnTrigger
{
    public string Type { get; set; } = "";

    /// <summary>IPlayerContext 카운터 키.</summary>
    public string Counter { get; set; } = "";

    /// <summary>이 HP 비율 이하일 때만 카운터가 오른다.</summary>
    public float HpRatioAtMost { get; set; } = 1f;

    public int RequiredCount { get; set; }

    /// <summary>습득 시 한 줄만 출력. 조건 설명 금지. (규칙 4)</summary>
    public string Announce { get; set; } = "";
}
