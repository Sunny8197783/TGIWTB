namespace PixelMmo.Runtime;

/// <summary>
/// 애니메이션 상태 id. data/animations.json 의 "id" 와 1:1로 대응한다.
/// 문자열을 코드 여기저기 흩뿌리지 않기 위한 상수 모음.
/// </summary>
public static class AnimationStates
{
    public const string Idle = "idle";
    public const string Walk = "walk";
    public const string Run = "run";
    public const string Dash = "dash";
    public const string AttackWindup = "attack_windup";
    public const string AttackActive = "attack_active";
    public const string AttackRecovery = "attack_recovery";
    public const string GuardHold = "guard_hold";
    public const string HeavyWindup = "heavy_windup";
    public const string HeavyActive = "heavy_active";
    public const string HeavyRecovery = "heavy_recovery";
    public const string Hurt = "hurt";
    public const string Dead = "dead";
}

/// <summary>
/// 애니메이션 상태머신이 대상(플레이어/몬스터)에게 묻는 것들.
/// 머신은 이것만 '읽는다' — 대상을 조종하지 않으므로 기존 전투 로직과 커플링되지 않는다.
/// 새 캐릭터 종류를 붙이려면 이 인터페이스만 구현하면 된다. (NPC/적 재사용)
/// </summary>
public interface IAnimationDriver
{
    /// <summary>지금 프레임의 애니메이션 상태 id. AnimationStates 의 값 중 하나.</summary>
    string CurrentAnimationState { get; }

    /// <summary>이번 프레임에 히트박스가 열려 있는가. hit_frame 시그널의 근거.</summary>
    bool AnimationHitboxActive { get; }
}
