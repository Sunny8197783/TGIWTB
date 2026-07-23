namespace PixelMmo.Balance;

/// <summary>
/// 숙련 획득량과 매크로 방어 수치. 손맛 수치(CombatTuning)와 성격이 달라 분리한다.
/// CLAUDE.md 규칙 3: 숙련도는 '사용 횟수'가 아니라 '유효한 사용'만 센다.
/// </summary>
public static class MasteryTuning
{
    /// <summary>유효한 1회 사용의 기본 획득량.</summary>
    public static readonly float BaseGainPerValidUse = 1.0f;

    /// <summary>같은 대상을 이 횟수까지는 감쇠 없음. 초과분부터 지수 감쇠. (§F)</summary>
    public static readonly int SameTargetFreeUses = 5;

    /// <summary>감쇠 반감 주기(횟수). factor = 0.5^((streak - Free) / HalfLife)</summary>
    public static readonly float SameTargetHalfLife = 2.0f;

    /// <summary>다른 대상을 때리거나 이 시간이 지나면 연타 카운트가 초기화된다.</summary>
    public static readonly float SameTargetStreakResetSeconds = 20.0f;

    /// <summary>감쇠 후 이 값 미만이면 0 으로 떨어뜨린다 (무한 파밍 차단).</summary>
    public static readonly float MinGain = 0.02f;

    /// <summary>내 레벨보다 이만큼 낮은 대상까지는 감점 없음.</summary>
    public static readonly int LevelDiffTolerance = 3;

    /// <summary>허용치를 넘는 레벨 차 1당 깎이는 비율.</summary>
    public static readonly float LevelDiffPenaltyPerLevel = 0.15f;

    /// <summary>레벨 차 감점의 하한.</summary>
    public static readonly float MinLevelFactor = 0.1f;

    /// <summary>스킬 JSON 에 minInterval 이 없을 때 쓰는 기본 최소 간격(초).</summary>
    public static readonly float DefaultMinInterval = 0.2f;
}
